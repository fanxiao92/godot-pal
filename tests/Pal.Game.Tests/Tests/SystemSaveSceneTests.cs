using System;
using System.Linq;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using Pal.Core;
using InvalidDataException = System.IO.InvalidDataException;
using static GdUnit4.Assertions;

namespace Pal.Game.Tests;

/// <summary>#224 (parent #148): the formal system-menu save entry on the story Main. The host
/// drives the production menu chain with real input events at real update-completion
/// boundaries (the MenuSceneTests SceneRunner pattern); no test-only save host, state
/// shortcut or second save chain exists. Independent expectations come from the authored
/// PartyWin sample (battle-data.json roles/party words; Exploration.tres leader/scene words),
/// the frozen #221 v1 contract and the #223 codec — never values produced by the save chain
/// under test. The formal chain writes into one test-owned user:// namespace (passed through
/// the production --save-directory argument); cleanup removes only that namespace.</summary>
[TestSuite, RequireGodotRuntime]
[TestCategory("SaveLoad")]
public class SystemSaveSceneTests
{
    [TestCase(Timeout = 60000)]
    public async Task System_menu_save_writes_the_decodable_slot_file_through_formal_main()
    {
        using var fixture = new AuthoredStoryFixture("PartyWin");
        string saveRoot = "user://save-tests-" + Guid.NewGuid().ToString("N");
        string faultRoot = saveRoot + "-fault";
        string occupiedRoot = saveRoot + "-occupied-root";
        var selection = GameSceneConfiguration.FromArguments("--story-profile", fixture.StoryUri,
            "--battle-visual-profile", fixture.VisualUri, "--battle-seed", "1", "--story-language", "en",
            "--save-directory", saveRoot);
        using var host = new SaveHost(selection);
        try
        {
            host.AssertHealthy();
            await host.Wait(() => host.Updates.Count > 0);
            AssertThat(host.Main.Menu).IsNotNull();
            AssertThat(host.Main.Menu!.Mode).IsEqual(ExplorationMenuMode.Closed);

            // input.c kKeyMenu (Escape) opens the in-game menu at a real update boundary (play.c 551).
            host.Tap(Key.Escape);
            await host.Wait(() => host.Main.Menu!.Mode == ExplorationMenuMode.MainMenu);
            // #222's declared formal M6-C save entry: the main menu's system item (uigame.c
            // GAMEMENU_LABEL_SYSTEM, the last entry after the authored operative pages) — the
            // staged RED's first milestone, now exposed on the formal story menu.
            AssertThat(host.Main.Menu!.Entries.Count).IsEqual(2);
            AssertThat(host.Main.Menu.EntryLabel(1)).IsEqual("SYSTEM");
            host.Tap(Key.Down);
            host.Tap(Key.Enter);
            await host.Wait(() => host.Main.Menu!.Mode == ExplorationMenuMode.SystemItems);
            // #227: the reference system menu's item rows (uigame.c PAL_SystemMenu SAVE/LOAD) —
            // confirming the remembered SAVE row enters the five-slot layer with the save intent.
            AssertThat(host.Main.Menu!.SystemItemLabel(0)).IsEqual("SAVE");
            AssertThat(host.Main.Menu!.SystemItemLabel(1)).IsEqual("LOAD");
            host.Tap(Key.Enter);
            await host.Wait(() => host.Main.Menu!.Mode == ExplorationMenuMode.SystemMenu);

            var menu = host.Main.Menu!;
            var storage = host.Main.SaveStorage!;
            string identity = host.Main.SaveContentIdentity!;

            // Stories 3/4: browsing only moves the remembered slot cursor — no file appears, no
            // slot is written and nothing switches.
            AssertThat(menu.SelectedSaveSlot).IsEqual(1);
            AssertThat(storage.SlotExists(1)).IsFalse();
            AssertThat(host.Main.CurrentRecoverySlot).IsNull();
            host.Tap(Key.Down);
            await host.Wait(() => menu.SelectedSaveSlot == 2);
            AssertThat(storage.SlotExists(2)).IsFalse();
            AssertThat(host.Main.CurrentRecoverySlot).IsNull();

            // Confirm: the host completes the whole chain — fresh #222 gate request, one-shot
            // stable capture, #223 v1 encode, protected five-slot write through the real
            // FileAccess user:// adapter — inside this same committed production update
            // boundary. Only the completed outcome switches the recovery slot (#148 Decision
            // 27) and the layer returns to browsing (Stories 9/12).
            host.Tap(Key.Enter);
            await host.Wait(() => host.Main.CurrentRecoverySlot == 2);
            AssertThat(menu.PendingSave).IsNull();
            AssertThat(menu.Mode).IsEqual(ExplorationMenuMode.SystemMenu);

            // Five-slot isolation: only the confirmed slot's file exists.
            AssertThat(storage.SlotExists(1)).IsFalse();
            AssertThat(storage.SlotExists(3)).IsFalse();
            AssertThat(storage.SlotExists(5)).IsFalse();

            // The controlled user:// slot file read back through FileAccess and decoded with the
            // production codec (#148 Testing Decision 4). Every expected value is an independent
            // literal from the authored sample and the frozen contracts.
            string slotPath = storage.SlotPath(2);
            AssertThat(slotPath.EndsWith("/slot2.json")).IsTrue();
            byte[] bytes;
            using (var file = FileAccess.Open(slotPath, FileAccess.ModeFlags.Read))
            {
                AssertThat(file).IsNotNull();
                bytes = file!.GetBuffer(checked((long)file.GetLength()));
            }
            var save = PlayerSaveJson.Decode(bytes, identity);
            AssertThat(save.FormatVersion).IsEqual(PlayerSaveContract.CurrentVersion);
            AssertThat(save.SaveSlot).IsEqual(2);
            AssertThat(save.SavedTimes).IsEqual(1);
            AssertThat(string.Join(",", save.PartyRoleIds)).IsEqual("0,1");
            AssertThat(save.Roles.Count).IsEqual(PlayerSaveContract.RoleCount); // the frozen six-row table
            AssertThat((save.Roles[0].Level, save.Roles[0].Hp, save.Roles[0].MaxHp, save.Roles[0].Mp, save.Roles[0].MaxMp))
                .IsEqual((2, 100, 150, 20, 50));
            AssertThat((save.Roles[1].Level, save.Roles[1].Hp, save.Roles[1].MaxHp)).IsEqual((2, 95, 150));
            AssertThat(save.Roles[2].Level).IsEqual(0); // the padded unused rows stay empty records
            AssertThat(save.Inventory.Count).IsEqual(0);
            AssertThat(save.Cash).IsEqual(0u);
            AssertThat(save.World.SceneId).IsEqual(1);
            AssertThat((save.World.Leader.X, save.World.Leader.Y, save.World.Leader.Direction,
                save.World.Leader.Layer)).IsEqual(((short)320, (short)320, 0, 0));
            // The walk phase is the live leader word at capture: idle exploration toggles the
            // standing phase between 0 and 2 every update tick (play.c idle rule), never the
            // mid-walk 1/3 — the party never moved from the authored start.
            AssertThat(save.World.Leader.StepPhase is 0 or 2).IsTrue();

            // Story 12: one confirmed operation commits exactly once — the second confirm is a
            // new operation on the same slot and overwrites it (savedTimes 2), and keyboard echo
            // never double-issues (the host filters echo presses; the parked layer rejects every
            // key until the owned receipt).
            host.Tap(Key.Enter);
            await host.Wait(() => PlayerSaveJson.Decode(storage.ReadSlot(2)!, identity).SavedTimes == 2);
            AssertThat(host.Main.CurrentRecoverySlot).IsEqual(2);
            AssertThat(menu.Mode).IsEqual(ExplorationMenuMode.SystemMenu);

            // kKeyMenu leaves the slot layer back to the system items, then the main menu; the
            // menu closes and exploration input resumes on the next update (uigame.c).
            host.Tap(Key.Escape);
            await host.Wait(() => menu.Mode == ExplorationMenuMode.SystemItems);
            host.Tap(Key.Escape);
            await host.Wait(() => menu.Mode == ExplorationMenuMode.MainMenu);
            host.Tap(Key.Escape);
            await host.Wait(() => menu.Mode == ExplorationMenuMode.Closed);

            // Story 53: the slot number alone derives the path — out-of-range slots reject
            // before anything opens.
            foreach (int slot in new[] { 0, 6, -1 })
            {
                bool rejected = false;
                try { storage.SlotPath(slot); }
                catch (InvalidDataException) { rejected = true; }
                AssertThat(rejected).IsTrue();
            }

            // #148 Decision 21 / Story 53 (controlled path): --save-directory is the only
            // path-bearing input of the whole save chain. The production parser accepts
            // exactly one controlled relative user:// namespace — filesystem paths, parent
            // traversal, a missing scheme, a trailing slash, a backslash separator, a second
            // scheme and over-length values all reject before the save namespace is ever
            // touched. This leaf's capability test owns the executed rejection boundary.
            string[] baseArguments = ["--story-profile", fixture.StoryUri, "--battle-visual-profile", fixture.VisualUri];
            foreach (string invalid in new[] { "C:/Saves", "user://../saves", "saves",
                "user://saves/", "user://sa\\ves", "user://a://b", "user://" + new string('a', 154) })
            {
                string[] withInvalid = [.. baseArguments, "--save-directory", invalid];
                bool rejected = false;
                try { GameSceneConfiguration.FromArguments(withInvalid); }
                catch (InvalidDataException) { rejected = true; }
                AssertThat(rejected).IsTrue();
            }

            // #148 Testing Decision 5 (real-filesystem negatives, separately marked): every
            // write-fault class a real filesystem reproduces reliably must show the located
            // reason, the old valid slot kept byte-identical and cleanup touching only this
            // adapter's own artifacts. (a) namespace-create: a regular file occupying the
            // namespace root fails as a located result, never an unlocated exception that
            // would crash the session instead of reporting a save failure (Story 48);
            // (b) temp-open: the adapter's own temp artifact blocked by a foreign directory;
            // (c) backup-rename: a valid old slot whose backup-rename publish step is blocked
            // by a foreign directory — the write already succeeded, the publish fails, and the
            // old valid slot survives byte-identical with the temp cleaned (Story 10);
            // (d) publish-rename: a foreign directory occupying the target slot path (no old
            // valid slot to lose; the blocker is never deleted). Each slot accepts writes
            // again once the foreign blocker is gone. Short-write injection and a restore
            // that fails after a failed publish are not constructible without a fault seam
            // and stay explicit evidence gaps (#148 Testing Decision 5).
            var faulted = new PlayerSaveStorage(faultRoot);
            AssertThat(faulted.WriteSlot(3, bytes).Published).IsTrue();
            byte[] replacement = [1, 2, 3];

            using (var blocker = FileAccess.Open(occupiedRoot, FileAccess.ModeFlags.Write))
            {
                blocker!.StoreBuffer(replacement);
            }
            var namespaceBlocked = new PlayerSaveStorage(occupiedRoot);
            var namespaceFailed = namespaceBlocked.WriteSlot(1, bytes);
            AssertThat(namespaceFailed.Published).IsFalse();
            AssertThat(namespaceFailed.Reason).IsEqual("namespace-create");
            using (var blockerCheck = FileAccess.Open(occupiedRoot, FileAccess.ModeFlags.Read))
            {
                AssertThat(Enumerable.SequenceEqual(
                    blockerCheck!.GetBuffer(checked((long)blockerCheck.GetLength())), replacement)).IsTrue();
            }

            AssertThat(DirAccess.MakeDirRecursiveAbsolute(faulted.Root + "/slot3.json.tmp") == Error.Ok).IsTrue();
            var failed = faulted.WriteSlot(3, replacement);
            AssertThat(failed.Published).IsFalse();
            AssertThat(failed.Reason).IsEqual("temp-open");
            AssertThat(Enumerable.SequenceEqual(faulted.ReadSlot(3)!, bytes)).IsTrue();
            AssertThat(DirAccess.RemoveAbsolute(faulted.Root + "/slot3.json.tmp") == Error.Ok).IsTrue();

            AssertThat(faulted.WriteSlot(4, bytes).Published).IsTrue();
            AssertThat(DirAccess.MakeDirRecursiveAbsolute(faulted.Root + "/slot4.json.bak") == Error.Ok).IsTrue();
            var backupBlocked = faulted.WriteSlot(4, replacement);
            AssertThat(backupBlocked.Published).IsFalse();
            AssertThat(backupBlocked.Reason).IsEqual("backup-rename");
            AssertThat(Enumerable.SequenceEqual(faulted.ReadSlot(4)!, bytes)).IsTrue();
            AssertThat(FileAccess.FileExists(faulted.Root + "/slot4.json.tmp")).IsFalse();
            AssertThat(DirAccess.RemoveAbsolute(faulted.Root + "/slot4.json.bak") == Error.Ok).IsTrue();

            AssertThat(DirAccess.MakeDirRecursiveAbsolute(faulted.Root + "/slot5.json") == Error.Ok).IsTrue();
            var targetBlocked = faulted.WriteSlot(5, replacement);
            AssertThat(targetBlocked.Published).IsFalse();
            AssertThat(targetBlocked.Reason).IsEqual("publish-rename");
            AssertThat(DirAccess.DirExistsAbsolute(faulted.Root + "/slot5.json")).IsTrue();
            AssertThat(FileAccess.FileExists(faulted.Root + "/slot5.json.tmp")).IsFalse();
            AssertThat(DirAccess.RemoveAbsolute(faulted.Root + "/slot5.json") == Error.Ok).IsTrue();

            AssertThat(faulted.WriteSlot(3, replacement).Published).IsTrue();
            AssertThat(Enumerable.SequenceEqual(faulted.ReadSlot(3)!, replacement)).IsTrue();
            AssertThat(faulted.WriteSlot(4, replacement).Published).IsTrue();
            AssertThat(Enumerable.SequenceEqual(faulted.ReadSlot(4)!, replacement)).IsTrue();
            AssertThat(faulted.WriteSlot(5, replacement).Published).IsTrue();
            AssertThat(Enumerable.SequenceEqual(faulted.ReadSlot(5)!, replacement)).IsTrue();
        }
        finally
        {
            if (FileAccess.FileExists(occupiedRoot)) DirAccess.RemoveAbsolute(occupiedRoot);
            CleanupNamespace(saveRoot);
            CleanupNamespace(faultRoot);
        }
    }

    /// <summary>Removes one test-owned namespace completely; never a foreign path (#148 Testing
    /// Decision 4 — real player slots are never touched by automation).</summary>
    private static void CleanupNamespace(string root)
    {
        if (!DirAccess.DirExistsAbsolute(root)) return;
        RemoveDirectoryRecursive(root);
        DirAccess.RemoveAbsolute(root);
    }

    private static void RemoveDirectoryRecursive(string directory)
    {
        using var dir = DirAccess.Open(directory);
        if (dir is null) return;
        dir.ListDirBegin();
        string entry = dir.GetNext();
        while (entry.Length > 0)
        {
            if (entry is not "." and not "..")
            {
                string child = directory + "/" + entry;
                if (dir.CurrentIsDir()) RemoveDirectoryRecursive(child);
                else DirAccess.RemoveAbsolute(child);
            }
            entry = dir.GetNext();
        }
        dir.ListDirEnd();
    }

    // Narrow host: it only drives and observes the real Main through SceneRunner and real input
    // events; no game assembly, time advancement or state shortcut exists here.
    private sealed class SaveHost : IDisposable
    {
        internal Main Main { get; } = new();
        private readonly ISceneRunner runner;
        private readonly Control? focus = ((SceneTree)Engine.GetMainLoop()).Root.GuiGetFocusOwner();
        internal System.Collections.Generic.List<ExplorationState> Updates { get; } = [];
        internal System.Collections.Generic.List<string> Failures { get; } = [];

        public SaveHost(GameSceneConfiguration selection)
        {
            Main.Configure(selection);
            Main.Failed += failure => Failures.Add(failure);
            Main.UpdateCompleted += () => Updates.Add(Main.Exploration!);
            var container = new Node();
            container.AddChild(Main);
            runner = ISceneRunner.Load(container, autoFree: true);
        }

        public void AssertHealthy()
        {
            if (Failures.Count > 0 || Main.Failure is not null)
                throw new InvalidOperationException("Save host failed: " + (Main.Failure ?? Failures[0]));
        }

        public void Tap(Key key) { Send(key, true); Send(key, false); }

        private void Send(Key key, bool pressed)
        {
            if (pressed) runner.SimulateKeyPress(key);
            else runner.SimulateKeyRelease(key);
        }

        public async Task Wait(Func<bool> predicate, int timeoutMs = 15000)
        {
            ulong start = Time.GetTicksMsec();
            int before = Updates.Count;
            while (!predicate() || Updates.Count == before)
            {
                AssertHealthy();
                if (Time.GetTicksMsec() - start > (ulong)timeoutMs)
                    throw new TimeoutException("Save production boundary not observed.");
                await runner.AwaitIdleFrame();
            }
            AssertHealthy();
        }

        public void Dispose()
        {
            try
            {
                Send(Key.Enter, false); Send(Key.Down, false); Send(Key.Escape, false);
                runner.Dispose();
            }
            finally
            {
                if (GodotObject.IsInstanceValid(Main)) Main.Free();
                if (GodotObject.IsInstanceValid(focus)) focus!.GrabFocus();
            }
            AssertThat(GodotObject.IsInstanceValid(Main)).IsFalse();
        }
    }
}
