using System;
using System.Linq;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using Pal.Core;
using static GdUnit4.Assertions;

namespace Pal.Game.Tests;

/// <summary>#227 (parent #148): the formal system-menu load flow on the story Main. The host
/// drives the production menu chain with real input events at real update-completion boundaries
/// (the SystemSaveSceneTests SceneRunner pattern); no test-only save host, state shortcut or
/// second save chain exists. Independent expectations come from the authored PartyWin sample
/// (battle-data roles words, authored start (320,320) with the first walkable west step at
/// (304,312) — see the suite provenance note below),
/// the frozen #221 contract and the #223 codec — never values produced by the chain under test.
/// The formal chain reads from one test-owned user:// namespace (the production
/// --save-directory argument); the hand-crafted slot file is an independent fixture this test
/// owns, and cleanup removes only that namespace.</summary>
/// #227 walk provenance: the authored arena's challenge NPC (event object 1, the search
/// target AuthoredBattlePackageTests pins at search sample 1) stands on the south neighbour
/// tile (304,328) with a blocking state, so the south step is a wall. The west neighbour
/// (304,312) is the authored arena's walkable first step — the same tile geometry the M2
/// west route pins — and no object of the south tile can block it (|dx| + 2|dy| >= 16).
[TestSuite, RequireGodotRuntime]
public class SystemLoadSceneTests
{
    [TestCase(Timeout = 90000)]
    public async Task Formal_menu_load_switches_the_rebuilt_session_at_a_production_boundary()
    {
        using var fixture = new AuthoredStoryFixture("PartyWin");
        string saveRoot = "user://load-tests-" + Guid.NewGuid().ToString("N");
        var selection = GameSceneConfiguration.FromArguments("--story-profile", fixture.StoryUri,
            "--battle-visual-profile", fixture.VisualUri, "--battle-seed", "1", "--story-language", "en",
            "--save-directory", saveRoot);
        using var host = new LoadHost(selection);
        try
        {
            host.AssertHealthy();
            await host.Wait(() => host.Updates.Count > 0);
            var menu = host.Main.Menu!;
            var storage = host.Main.SaveStorage!;
            string identity = host.Main.SaveContentIdentity!;
            AssertThat(host.Main.CurrentRecoverySlot).IsNull(); // Story 37: no default slot

            // Save slot 2 at the authored start, then walk one west step — the world's live
            // state the loaded snapshot must rebuild stands apart from the authored one.
            await SaveThroughFormalMenu(host, slot: 2);
            AssertThat(host.Main.CurrentRecoverySlot).IsEqual(2);
            await WalkOneWestStep(host);
            AssertThat((host.Main.Exploration!.Leader.X, host.Main.Exploration!.Leader.Y)).IsEqual(((short)304, (short)312));
            var dialogueBefore = host.Main.Dialogue;
            var menuBefore = host.Main.Menu;

            // Formal load of slot 2 through the system items' LOAD row (Stories 27/39).
            await OpenSystemItems(host);
            await OpenSlotLayer(host, row: 1); // the LOAD row, by its observed cursor
            await host.Wait(() => menu.Mode == ExplorationMenuMode.SystemMenu);
            AssertThat(menu.SystemIntent).IsEqual(ExplorationSystemIntent.Load);
            AssertThat(menu.SelectedSaveSlot).IsEqual(2); // the remembered cursor names slot 2
            host.Tap(Key.Enter);
            await host.Wait(() => host.Main.Menu!.Mode == ExplorationMenuMode.Closed
                && (host.Main.Exploration!.Leader.X, host.Main.Exploration.Leader.Y) == ((short)320, (short)320));
            AssertThat(host.Main.CurrentRecoverySlot).IsEqual(2); // Story 39: the completed read switches it

            // Story 31: the switch happened at one committed production update boundary — the
            // walked sample is immediately followed by the restored sample in the synchronous
            // UpdateCompleted sampling, never a mixed state.
            int walked = host.Leaders.FindLastIndex(leader => leader == ((short)304, (short)312));
            AssertThat(walked >= 0).IsTrue();
            AssertThat(host.Leaders[walked + 1]).IsEqual(((short)320, (short)320));

            // Stories 32/33: the rebuilt session is a new identity (the old dialogue node, menu
            // session and their request/receipt state are retired), and the menu reads the same
            // rebuilt true state the exploration continues from.
            AssertThat(ReferenceEquals(dialogueBefore, host.Main.Dialogue)).IsFalse();
            AssertThat(ReferenceEquals(menuBefore, host.Main.Menu)).IsFalse();
            host.Tap(Key.Escape);
            await host.Wait(() => host.Main.Menu!.Mode == ExplorationMenuMode.MainMenu);
            host.Tap(Key.Enter); // the remembered cursor is the status entry
            await host.Wait(() => host.Main.Menu!.Mode == ExplorationMenuMode.Status);
            var status = host.Main.Menu!.StatusView();
            AssertThat((status.Level, status.Hp, status.MaxHp, status.Mp, status.MaxMp)).IsEqual((2, 100, 150, 20, 50));
            host.Tap(Key.Escape);
            await host.Wait(() => host.Main.Menu!.Mode == ExplorationMenuMode.Closed);

            // The loaded lineage continues: the next save writes savedTimes 2 (#227's count
            // continuity from the loaded snapshot), and it captures the rebuilt owners.
            await WalkOneWestStep(host);
            AssertThat((host.Main.Exploration!.Leader.X, host.Main.Exploration.Leader.Y)).IsEqual(((short)304, (short)312));
            await SaveThroughFormalMenu(host, slot: 3);
            AssertThat(PlayerSaveJson.Decode(storage.ReadSlot(3)!, identity).SavedTimes).IsEqual(2);

            // Decision 27: a crafted slot 5 file carrying a foreign saveSlot word 4 never
            // overrides this session's actually selected slot.
            var slot2 = PlayerSaveJson.Decode(storage.ReadSlot(2)!, identity);
            var crafted = new PlayerSaveV1(slot2.FormatVersion, saveSlot: 4, slot2.SavedTimes, slot2.ContentIdentity,
                slot2.Roles, slot2.PartyRoleIds, slot2.Inventory, slot2.Cash, slot2.World);
            using (var file = FileAccess.Open(storage.SlotPath(5), FileAccess.ModeFlags.Write))
                file!.StoreBuffer(PlayerSaveJson.EncodeToUtf8(crafted));
            await LoadThroughFormalMenu(host, fromSlot: 3, toSlot: 5);
            AssertThat(host.Main.CurrentRecoverySlot).IsEqual(5);
            AssertThat((host.Main.Exploration!.Leader.X, host.Main.Exploration.Leader.Y)).IsEqual(((short)320, (short)320));

            // Story 36: repeated reads of two slots stay isolated — slot 3 restores its own
            // walked position and the loaded slot 2 file was never dirtied by any of it.
            await LoadThroughFormalMenu(host, fromSlot: 1, toSlot: 3);
            AssertThat(host.Main.CurrentRecoverySlot).IsEqual(3);
            AssertThat((host.Main.Exploration!.Leader.X, host.Main.Exploration.Leader.Y)).IsEqual(((short)304, (short)312));
            AssertThat(PlayerSaveJson.Decode(storage.ReadSlot(2)!, identity).SavedTimes).IsEqual(1);
        }
        finally
        {
            CleanupNamespace(saveRoot);
        }
    }

    [TestCase(Timeout = 90000)]
    public async Task Released_session_and_a_fresh_main_read_the_saved_slot_through_the_formal_menu()
    {
        using var fixture = new AuthoredStoryFixture("PartyWin");
        string saveRoot = "user://load-tests-" + Guid.NewGuid().ToString("N");
        var selection = GameSceneConfiguration.FromArguments("--story-profile", fixture.StoryUri,
            "--battle-visual-profile", fixture.VisualUri, "--battle-seed", "1", "--story-language", "en",
            "--save-directory", saveRoot);
        // Host A: walk first, then save slot 1 at the walked position; the whole session (its
        // Main, owners and menu) is then fully released — Story 28's "released session" half of
        // the no-old-memory proof.
        using (var first = new LoadHost(selection))
        {
            first.AssertHealthy();
            await first.Wait(() => first.Updates.Count > 0);
            await WalkOneWestStep(first);
            AssertThat((first.Main.Exploration!.Leader.X, first.Main.Exploration.Leader.Y)).IsEqual(((short)304, (short)312));
            await SaveThroughFormalMenu(first, slot: 1);
            AssertThat(first.Main.CurrentRecoverySlot).IsEqual(1);
        }

        // Host B: a fresh Main over the same prepared content and the same save namespace reads
        // the slot through the formal menu; nothing of the old session's memory participates.
        using (var second = new LoadHost(selection))
        {
            second.AssertHealthy();
            await second.Wait(() => second.Updates.Count > 0);
            AssertThat(second.Main.CurrentRecoverySlot).IsNull(); // a fresh context starts without one
            AssertThat((second.Main.Exploration!.Leader.X, second.Main.Exploration.Leader.Y)).IsEqual(((short)320, (short)320));
            await LoadThroughFormalMenu(second, fromSlot: 1, toSlot: 1);
            AssertThat(second.Main.CurrentRecoverySlot).IsEqual(1);
            AssertThat((second.Main.Exploration!.Leader.X, second.Main.Exploration.Leader.Y)).IsEqual(((short)304, (short)312));
            AssertThat(second.Main.Menu!.Mode).IsEqual(ExplorationMenuMode.Closed);
        }
    }

    [TestCase(Timeout = 90000)]
    public async Task Load_rejections_keep_the_original_session_recovery_slot_and_return_to_gameplay()
    {
        using var fixture = new AuthoredStoryFixture("PartyWin");
        string saveRoot = "user://load-tests-" + Guid.NewGuid().ToString("N");
        var selection = GameSceneConfiguration.FromArguments("--story-profile", fixture.StoryUri,
            "--battle-visual-profile", fixture.VisualUri, "--battle-seed", "1", "--story-language", "en",
            "--save-directory", saveRoot);
        using var host = new LoadHost(selection);
        try
        {
            host.AssertHealthy();
            await host.Wait(() => host.Updates.Count > 0);
            var menu = host.Main.Menu!;
            var storage = host.Main.SaveStorage!;
            await SaveThroughFormalMenu(host, slot: 2);
            await WalkOneWestStep(host);
            AssertThat((host.Main.Exploration!.Leader.X, host.Main.Exploration.Leader.Y)).IsEqual(((short)304, (short)312));
            var dialogueBefore = host.Main.Dialogue;
            // A corrupted slot 4 file this test owns (never a real player's slot).
            using (var file = FileAccess.Open(storage.SlotPath(4), FileAccess.ModeFlags.Write))
                file!.StoreBuffer(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0xFF, 0xFE });

            // A missing slot rejects: no file, no read, no switch — the layer un-parks for
            // browsing, the original session and the current recovery slot stay untouched and
            // the player can cancel back to the gameplay exactly as it was (Stories 30/35).
            await LoadThroughFormalMenu(host, fromSlot: 2, toSlot: 3, parkOnly: true);
            host.Tap(Key.Enter);
            await host.Wait(() => menu.PendingLoad is null && menu.Mode == ExplorationMenuMode.SystemMenu);
            AssertThat((host.Main.Exploration!.Leader.X, host.Main.Exploration.Leader.Y)).IsEqual(((short)304, (short)312));
            AssertThat(ReferenceEquals(dialogueBefore, host.Main.Dialogue)).IsTrue();
            AssertThat(host.Main.CurrentRecoverySlot).IsEqual(2);
            host.Tap(Key.Escape);
            await host.Wait(() => menu.Mode == ExplorationMenuMode.SystemItems);
            host.Tap(Key.Escape);
            await host.Wait(() => menu.Mode == ExplorationMenuMode.MainMenu);
            host.Tap(Key.Escape);
            await host.Wait(() => menu.Mode == ExplorationMenuMode.Closed);
            AssertThat((host.Main.Exploration!.Leader.X, host.Main.Exploration!.Leader.Y)).IsEqual(((short)304, (short)312));

            // A corrupted slot rejects the same way: the #223 codec's located diagnostic, no
            // partial application anywhere (Story 29's whole-file validation before any switch).
            await LoadThroughFormalMenu(host, fromSlot: 3, toSlot: 4, parkOnly: true);
            host.Tap(Key.Enter);
            await host.Wait(() => menu.PendingLoad is null && menu.Mode == ExplorationMenuMode.SystemMenu);
            AssertThat((host.Main.Exploration!.Leader.X, host.Main.Exploration.Leader.Y)).IsEqual(((short)304, (short)312));
            AssertThat(host.Main.CurrentRecoverySlot).IsEqual(2);
            host.Tap(Key.Escape);
            await host.Wait(() => menu.Mode == ExplorationMenuMode.SystemItems);
            host.Tap(Key.Escape);
            await host.Wait(() => menu.Mode == ExplorationMenuMode.MainMenu);
            host.Tap(Key.Escape);
            await host.Wait(() => menu.Mode == ExplorationMenuMode.Closed);

            // Cancelling before the confirm never reads anything: browsing only moved cursors.
            await OpenSystemItems(host);
            await OpenSlotLayer(host, row: 1);
            host.Tap(Key.Down); // the slot cursor moves without any read
            host.Tap(Key.Escape);
            await host.Wait(() => menu.Mode == ExplorationMenuMode.SystemItems);
            host.Tap(Key.Escape);
            await host.Wait(() => menu.Mode == ExplorationMenuMode.MainMenu);
            host.Tap(Key.Escape);
            await host.Wait(() => menu.Mode == ExplorationMenuMode.Closed);
            AssertThat((host.Main.Exploration!.Leader.X, host.Main.Exploration.Leader.Y)).IsEqual(((short)304, (short)312));
            AssertThat(host.Main.CurrentRecoverySlot).IsEqual(2);
            AssertThat(storage.SlotExists(3)).IsFalse(); // the missing slot never materialized
        }
        finally
        {
            CleanupNamespace(saveRoot);
        }
    }

    /// <summary>Saves one slot through the formal menu chain (open → system → SAVE → confirm).
    /// Every layer is entered by its observed cursor: the reference keeps the main-menu and
    /// system-item cursors across close/reopen (uigame.c iCurMenuItem), so a revisit must
    /// browse, never assume the fresh-session position.</summary>
    private static async Task SaveThroughFormalMenu(LoadHost host, int slot)
    {
        var menu = host.Main.Menu!;
        await OpenSystemItems(host);
        await OpenSlotLayer(host, row: 0); // the SAVE row, by its observed cursor
        await BrowseToSlot(host, slot);
        host.Tap(Key.Enter);
        await host.Wait(() => menu.PendingSave is null && host.Main.CurrentRecoverySlot == slot);
        host.Tap(Key.Escape);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.SystemItems);
        host.Tap(Key.Escape);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.MainMenu);
        host.Tap(Key.Escape);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.Closed);
    }

    /// <summary>Enters the load-intent slot layer and browses from <paramref name="fromSlot"/>
    /// (the remembered cursor's expected slot) to <paramref name="toSlot"/>; with
    /// <paramref name="parkOnly"/> the caller confirms the read itself, otherwise the confirm
    /// and the completed switch are awaited here.</summary>
    private static async Task LoadThroughFormalMenu(LoadHost host, int fromSlot, int toSlot, bool parkOnly = false)
    {
        var menu = host.Main.Menu!;
        await OpenSystemItems(host);
        await OpenSlotLayer(host, row: 1); // the LOAD row, by its observed cursor
        AssertThat(menu.SelectedSaveSlot).IsEqual(fromSlot);
        await BrowseToSlot(host, toSlot);
        if (parkOnly) return;
        host.Tap(Key.Enter);
        await host.Wait(() => host.Main.Menu!.Mode == ExplorationMenuMode.Closed);
    }

    /// <summary>Opens the system items from the closed menu by browsing the REMEMBERED
    /// main-menu cursor (uigame.c keeps iCurMenuItem across close/reopen — the save helper's
    /// visit parks it on the SYSTEM entry), never by assuming a fresh cursor.</summary>
    private static async Task OpenSystemItems(LoadHost host)
    {
        var menu = host.Main.Menu!;
        host.Tap(Key.Escape);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.MainMenu);
        int system = menu.Entries.Count - 1; // the reference's always-present last item
        for (int step = 0; step < menu.Entries.Count && menu.SelectedEntry != system; step++)
        {
            host.Tap(Key.Down);
            await host.Wait(() => true);
        }
        AssertThat(menu.SelectedEntry).IsEqual(system);
        host.Tap(Key.Enter);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.SystemItems);
    }

    /// <summary>Confirms the system row (SAVE=0/LOAD=1) into the shared slot layer by
    /// browsing the remembered system-item cursor, mirroring the slot browsing below.</summary>
    private static async Task OpenSlotLayer(LoadHost host, int row)
    {
        var menu = host.Main.Menu!;
        for (int step = 0; step < 2 && menu.SelectedSystemItem != row; step++)
        {
            host.Tap(Key.Down);
            await host.Wait(() => true);
        }
        AssertThat(menu.SelectedSystemItem).IsEqual(row);
        host.Tap(Key.Enter);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.SystemMenu);
    }

    /// <summary>Browses the remembered slot cursor to one slot one committed boundary per move
    /// (a queued key applies only at the next production update).</summary>
    private static async Task BrowseToSlot(LoadHost host, int slot)
    {
        for (int step = 0; step < PlayerSaveContract.SaveSlotCount && host.Main.Menu!.SelectedSaveSlot != slot; step++)
        {
            host.Tap(Key.Down);
            await host.Wait(() => true);
        }
        AssertThat(host.Main.Menu!.SelectedSaveSlot).IsEqual(slot);
    }

    /// <summary>One held west step: the authored start (320,320) walks to its first west
    /// neighbour (304,312) — the south neighbour (304,328) carries the arena's blocking
    /// challenge NPC, so south is a wall in this authored scene (see the suite provenance
    /// note; MainSceneTests' south literal belongs to the M2 fixture, not this arena).</summary>
    private static async Task WalkOneWestStep(LoadHost host)
    {
        var before = (host.Main.Exploration!.Leader.X, host.Main.Exploration.Leader.Y);
        host.Press(Key.A);
        await host.Wait(() => (host.Main.Exploration!.Leader.X, host.Main.Exploration.Leader.Y) != before);
        host.Release(Key.A);
        await host.Wait(() => (host.Main.Exploration!.Leader.X, host.Main.Exploration.Leader.Y)
            == (((short)304, (short)312)));
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
    private sealed class LoadHost : IDisposable
    {
        internal Main Main { get; } = new();
        private readonly ISceneRunner runner;
        private readonly Control? focus = ((SceneTree)Engine.GetMainLoop()).Root.GuiGetFocusOwner();
        internal System.Collections.Generic.List<ExplorationState> Updates { get; } = [];
        internal System.Collections.Generic.List<(short X, short Y)> Leaders { get; } = [];
        internal System.Collections.Generic.List<string> Failures { get; } = [];

        public LoadHost(GameSceneConfiguration selection)
        {
            Main.Configure(selection);
            Main.Failed += failure => Failures.Add(failure);
            Main.UpdateCompleted += () =>
            {
                Updates.Add(Main.Exploration!);
                Leaders.Add((Main.Exploration!.Leader.X, Main.Exploration.Leader.Y));
            };
            var container = new Node();
            container.AddChild(Main);
            runner = ISceneRunner.Load(container, autoFree: true);
        }

        public void AssertHealthy()
        {
            if (Failures.Count > 0 || Main.Failure is not null)
                throw new InvalidOperationException("Load host failed: " + (Main.Failure ?? Failures[0]));
        }

        public void Tap(Key key) { Send(key, true); Send(key, false); }
        public void Press(Key key) => Send(key, true);
        public void Release(Key key) => Send(key, false);

        private static void Send(Key key, bool pressed)
        {
            using var input = new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed };
            Input.ParseInputEvent(input);
            Input.FlushBufferedEvents();
        }

        public async Task Wait(Func<bool> predicate, int timeoutMs = 15000)
        {
            ulong start = Time.GetTicksMsec();
            int before = Updates.Count;
            while (!predicate() || Updates.Count == before)
            {
                AssertHealthy();
                if (Time.GetTicksMsec() - start > (ulong)timeoutMs)
                    throw new TimeoutException("Load production boundary not observed.");
                await runner.AwaitIdleFrame();
            }
            AssertHealthy();
        }

        public void Dispose()
        {
            try
            {
                Send(Key.Enter, false); Send(Key.Down, false); Send(Key.S, false); Send(Key.A, false); Send(Key.Escape, false);
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
