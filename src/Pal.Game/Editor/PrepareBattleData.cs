using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Godot;
using Pal.Core;
using Pal.Game.Profiles;

namespace Pal.Game.Editor;

// Explicit offline numeric preparation; runtime startup never calls this node.
public partial class PrepareBattleData : Node
{
    private const int MaximumInputBytes = 16 * 1024 * 1024;
    private const string OutputFile = "BattleContent.tres";

    public override void _Ready()
    {
        try
        {
            string[] args = OS.GetCmdlineUserArgs();
            Require(args.Length == 4 && args[0] == "--battle-input" && args[2] == "--battle-output",
                "invalid battle-data prepare arguments");
            _ = Prepare(args[1], args[3]);
            GD.Print("PAL_PREPARE_BATTLE_DATA:" + OutputFile);
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PrintErr("Battle data native import failed: " + error.Message);
            GetTree().Quit(1);
        }
    }

    public static string Prepare(string inputDirectory, string outputDirectory)
    {
        string input = Normalize(inputDirectory), output = Normalize(outputDirectory);
        string? temporary = null;
        var createdDirectories = new List<string>();
        try
        {
            ValidatePaths(input, output);
            string assets = Normalize(ProjectSettings.GlobalizePath("res://Assets"));
            string relative = Path.GetRelativePath(assets, output);
            Require(!Path.IsPathRooted(relative) && relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).All(part => part != ".."),
                "battle data native output must remain inside project Assets");
            // Godot LocalizePath compares the physical project prefix case-sensitively, even on Windows.
            output = Normalize(Path.Combine(assets, relative));
            string outputUri = "res://Assets" + (relative == "." ? "" : "/" + relative.Replace('\\', '/'));
            CheckOutput(output, null);
            string source = Path.Combine(input, "battle-data.json");
            using FileStream lease = PreparationPathSafety.OpenStableRead(source);
            Require(lease.Length > 0 && lease.Length <= MaximumInputBytes, "battle-data.json must contain 1..16777216 bytes");
            string hash = PreparationPathSafety.Sha256(lease);
            byte[] bytes = new byte[checked((int)lease.Length)];
            lease.ReadExactly(bytes);
            lease.Position = 0;
            string json;
            try
            {
                int bom = bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? 3 : 0;
                json = new UTF8Encoding(false, true).GetString(bytes, bom, bytes.Length - bom);
            }
            catch (DecoderFallbackException error) { throw new InvalidDataException("battle-data.json must be valid UTF-8", error); }
            var content = ModernBattleContent.Parse(json);
            Recheck(input, output, source, lease, hash, null);
            var native = NativeBattleContent.Write(content);
            Recheck(input, output, source, lease, hash, null);
            CreateOwnedDirectory(output, createdDirectories);
            Recheck(input, output, source, lease, hash, null);
            string destination = Path.Combine(output, OutputFile);
            temporary = Path.Combine(output, ".battle-data-" + Guid.NewGuid().ToString("N") + ".tmp.tres");
            string temporaryUri = outputUri + "/" + Path.GetFileName(temporary);
            Require(ResourceSaver.Save(native, temporaryUri) == Error.Ok, "could not save native battle data");
            Recheck(input, output, source, lease, hash, temporary);
            _ = NativeBattleContent.Load(temporaryUri);
            Recheck(input, output, source, lease, hash, temporary);
            File.Move(temporary, destination, false);
            temporary = null;
            return outputUri + "/" + OutputFile;
        }
        catch
        {
            // Never recursively remove output: another caller may have added an entry.
            if (temporary is not null)
                try
                {
                    PreparationPathSafety.CheckNoReparse(temporary, allowMissing: true);
                    File.Delete(temporary);
                }
                catch { }
            for (int i = createdDirectories.Count - 1; i >= 0; i--)
                try
                {
                    PreparationPathSafety.CheckNoReparse(createdDirectories[i], allowMissing: true);
                    Directory.Delete(createdDirectories[i], false);
                }
                catch { }
            throw;
        }
    }

    private static void Recheck(string input, string output, string source, FileStream lease, string hash, string? temporary)
    {
        ValidatePaths(input, output);
        Require(PreparationPathSafety.MatchesStableRead(source, lease, hash), "battle-data.json source changed during preparation");
        CheckOutput(output, temporary);
    }

    private static void ValidatePaths(string input, string output)
    {
        Require(IsInside(output, ProjectSettings.GlobalizePath("res://Assets")), "battle data native output must be under project Assets");
        Require(!IsInside(input, output) && !IsInside(output, input), "battle data input and output must not overlap in either direction");
        Require(!IsInside(input, ProjectSettings.GlobalizePath("res://")), "battle data input must be outside src/Pal.Game");
        PreparationPathSafety.CheckNoReparse(input, allowMissing: true);
        Require(Directory.Exists(input), "battle data input must be an existing directory");
        PreparationPathSafety.CheckTreeNoReparse(input);
        string[] entries = Directory.EnumerateFileSystemEntries(input).ToArray();
        Require(entries.Length == 1 && Path.GetFileName(entries[0]) == "battle-data.json" && File.Exists(entries[0]),
            "battle data input must contain exactly battle-data.json and no other entries");
        PreparationPathSafety.CheckNoReparse(output, allowMissing: true);
        Require(!File.Exists(output), "battle data native output is an existing file");
    }

    private static void CheckOutput(string output, string? temporary)
    {
        if (!Directory.Exists(output)) return;
        PreparationPathSafety.CheckTreeNoReparse(output);
        Require(Directory.EnumerateFileSystemEntries(output).All(path => temporary is not null && path == temporary && File.Exists(path)),
            "battle data native output must be new or empty; no overwrite or unexpected entries are supported");
    }

    private static void CreateOwnedDirectory(string path, List<string> created)
    {
        var missing = new Stack<string>();
        string? current = path;
        while (current is not null && !Directory.Exists(current))
        {
            missing.Push(current);
            current = Path.GetDirectoryName(current);
        }
        while (missing.Count > 0)
        {
            current = missing.Pop();
            PreparationPathSafety.CheckNoReparse(current, allowMissing: true);
            if (Directory.Exists(current)) continue;
            Directory.CreateDirectory(current);
            created.Add(current);
        }
    }

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    private static bool IsInside(string child, string parent)
    {
        child = Normalize(child); parent = Normalize(parent);
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string prefix = Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar;
        return child.Equals(parent, comparison) || child.StartsWith(prefix, comparison);
    }
    private static void Require(bool condition, string diagnostic)
    {
        if (!condition) throw new InvalidDataException(diagnostic);
    }
}
