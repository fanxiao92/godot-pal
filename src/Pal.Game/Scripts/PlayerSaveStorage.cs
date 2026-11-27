using System;
using Godot;
using Pal.Core;
using InvalidDataException = System.IO.InvalidDataException;
using Pal.Core.Save;

namespace Pal.Game;

/// <summary>One write outcome: <see cref="Published"/> is true only when the complete write,
/// byte-equal read-back verification and backup/rename publish all finished; every other value
/// carries exactly one located reason so hosts report a locatable failure instead of a generic
/// I/O crash (#148 Story 48/Decision 22).</summary>
public sealed record PlayerSaveWriteResult(bool Published, string? Reason);

/// <summary>M6-C leaf #224 (parent #148 Decision 21–22): the controlled five-slot user://
/// player-save storage adapter — the one new platform I/O boundary of the save chain. The
/// slot number alone derives the file name inside a single app-controlled namespace (Story 53:
/// save content can never choose a path; slot 0/6/negative reject before anything opens).
/// Writes go through this adapter's own temp artifact, a byte-equal read-back verification and
/// a backup/rename publish that keeps the old valid slot on every failure (Story 10); cleanup
/// touches only this adapter's own temp/backup artifacts. The adapter claims no atomicity or
/// power-loss safety beyond the verified Godot API behavior — short writes surface through the
/// read-back comparison, and anything not reliably constructible on a real filesystem stays an
/// explicit evidence gap, never a silent assumption (#148 Testing Decision 5). Reads are
/// bounded by the frozen pre-decode byte cap before any byte is returned (Decision 20).</summary>
public sealed class PlayerSaveStorage
{
    /// <summary>The production namespace of the five manual slots.</summary>
    public const string DefaultRoot = "user://saves";

    /// <summary>A controlled namespace is one relative user:// directory path: no parent
    /// traversal, no Windows separator, no trailing slash and a bounded length.</summary>
    public static bool IsValidNamespace(string candidate) =>
        candidate.StartsWith("user://", StringComparison.Ordinal)
        && candidate.Length > "user://".Length && candidate.Length <= 160
        && !candidate.EndsWith('/') && !candidate.Contains("..", StringComparison.Ordinal)
        && !candidate.Contains('\\') && candidate.IndexOf("://", 7, StringComparison.Ordinal) < 0;

    public string Root { get; }

    public PlayerSaveStorage(string? root = null)
    {
        Root = string.IsNullOrEmpty(root) ? DefaultRoot : root!;
        if (!IsValidNamespace(Root))
            throw new InvalidDataException("The save namespace must be one controlled relative user:// directory.");
    }

    public static bool IsValidSlot(int slot) =>
        slot is >= PlayerSaveContract.FirstSlot and <= PlayerSaveContract.LastSlot;

    /// <summary>The slot's file path inside the controlled namespace — derived from the slot
    /// number alone; payload never participates (#148 Story 53).</summary>
    public string SlotPath(int slot) => Root + "/slot" + Validated(slot) + ".json";

    public bool SlotExists(int slot) => FileAccess.FileExists(SlotPath(slot));

    /// <summary>Reads one slot's complete bytes; null when the slot has no file. A file over
    /// the frozen pre-decode cap rejects before any byte escapes (#148 Decision 20).</summary>
    public byte[]? ReadSlot(int slot) => ReadAll(SlotPath(slot));

    /// <summary>The protected write (Story 10/22): own temp artifact → byte-equal read-back →
    /// backup/rename publish keeping the old valid slot on every failure → published-file
    /// recheck → own-artifact cleanup. Success reports only after the whole chain finished;
    /// a namespace that cannot be created reports the located <c>namespace-create</c> reason
    /// instead of throwing (#148 Story 48).</summary>
    public PlayerSaveWriteResult WriteSlot(int slot, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        int validated = Validated(slot);
        if (bytes.Length > PlayerSaveContract.MaxFileBytes)
            return new(false, "payload-over-cap");
        string target = Root + "/slot" + validated + ".json";
        string temp = target + ".tmp";
        string backup = target + ".bak";
        if (!MakeRoot()) return new(false, "namespace-create");
        using (var writer = FileAccess.Open(temp, FileAccess.ModeFlags.Write))
        {
            if (writer is null) return new(false, "temp-open");
            writer.StoreBuffer(bytes);
        } // FileAccess flushes and closes on dispose; the read-back below is the verification.
        byte[]? written = ReadAll(temp);
        if (written is null) { TryRemoveOwnFile(temp); return new(false, "temp-readback"); }
        if (!written.AsSpan().SequenceEqual(bytes)) { TryRemoveOwnFile(temp); return new(false, "temp-mismatch"); }
        bool hadTarget = FileAccess.FileExists(target);
        bool movedToBackup = false;
        if (hadTarget)
        {
            TryRemoveOwnFile(backup); // a stale own backup from an interrupted earlier publish
            if (!Rename(target, backup)) { TryRemoveOwnFile(temp); return new(false, "backup-rename"); }
            movedToBackup = true;
        }
        if (!Rename(temp, target))
        {
            if (movedToBackup && !Rename(backup, target)) return new(false, "publish-restore-failed");
            TryRemoveOwnFile(temp);
            return new(false, "publish-rename");
        }
        byte[]? published = ReadAll(target);
        if (published is null || !published.AsSpan().SequenceEqual(bytes))
        {
            // The publish itself is untrustworthy: put the old valid slot back (Decision 22).
            TryRemoveOwnFile(target);
            if (movedToBackup && !Rename(backup, target)) return new(false, "publish-verify-restore-failed");
            return new(false, "publish-verify");
        }
        if (movedToBackup) TryRemoveOwnFile(backup); // own artifact cleanup, only after success
        return new(true, null);
    }

    private static int Validated(int slot) => IsValidSlot(slot)
        ? slot
        : throw new InvalidDataException(
            $"The save slot must be one of the five manual slots {PlayerSaveContract.FirstSlot}..{PlayerSaveContract.LastSlot}; {slot} is out of range.");

    /// <summary>Ensures the namespace directory exists. Only the directory identity of the
    /// root counts as success: Godot's recursive mkdir reports success for a path already
    /// occupied by any existing entry (a regular file returns ERR_ALREADY_EXISTS, which the
    /// recursive helper treats as done), so a foreign file occupying the root fails here as
    /// the located <c>namespace-create</c> write result instead of an exception — the host
    /// reports a locatable save failure and the session keeps running (#148 Story 48/Decision 22).</summary>
    private bool MakeRoot()
    {
        if (DirAccess.DirExistsAbsolute(Root)) return true;
        _ = DirAccess.MakeDirRecursiveAbsolute(Root);
        return DirAccess.DirExistsAbsolute(Root);
    }

    /// <summary>Reads a whole file bounded by the frozen byte cap; null when it does not exist.</summary>
    private static byte[]? ReadAll(string path)
    {
        if (!FileAccess.FileExists(path)) return null;
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file is null) return null;
        ulong length = file.GetLength();
        if (length > (ulong)PlayerSaveContract.MaxFileBytes)
            throw new InvalidDataException(
                $"{path}: the slot file is {length} bytes, over the frozen {PlayerSaveContract.MaxFileBytes}-byte pre-decode cap; rejected before reading.");
        return length == 0 ? [] : file.GetBuffer(checked((long)length));
    }

    /// <summary>Removes one of this adapter's own artifacts. A foreign blocker at that path
    /// (for example a directory) is not ours to delete; the failure is swallowed deliberately.</summary>
    private static void TryRemoveOwnFile(string path)
    {
        if (FileAccess.FileExists(path)) _ = DirAccess.RemoveAbsolute(path);
    }

    private static bool Rename(string from, string to) => DirAccess.RenameAbsolute(from, to) == Error.Ok;
}
