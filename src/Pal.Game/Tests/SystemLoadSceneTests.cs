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

    [TestCase(Timeout = 120000)]
    public async Task Load_accepts_a_saved_consumed_runtime_trigger_entry_and_keeps_the_restored_word()
    {
        // #234 (parent #129 T03; #227 matrix D11): the production completion writes the returned
        // trigger word into the runtime world (play.c 153) and the save chain captures that word,
        // so a committed load rebuilds the interaction over a restored object whose entry has moved
        // off the authored entry. The restored world object entry is authoritative retained state:
        // the load completes and keeps it, while the authored entry stays the checked dispatch
        // closure (never an unvalidated address supplied by file bytes). The crafted slot is an
        // independent fixture this test owns: the actual capture with only the searched object's
        // retained trigger word replaced by the authored end address 6 — the shape #234's
        // control-flow completion writes (StoryRoundtripSceneTests' Main chain) — never a value
        // produced by the chain under test.
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
            var storage = host.Main.SaveStorage!;
            string identity = host.Main.SaveContentIdentity!;
            await SaveThroughFormalMenu(host, slot: 2);
            AssertThat(host.Main.CurrentRecoverySlot).IsEqual(2);
            var saved = PlayerSaveJson.Decode(storage.ReadSlot(2)!, identity);
            var objects = saved.World.Objects.Select(obj => obj.ObjectId == 1
                ? new PlayerSaveObject(obj.ObjectId, obj.X, obj.Y, obj.Direction, obj.CurrentFrame,
                    obj.State, obj.VanishTime, obj.TriggerMode, 6, obj.AutoEntry, obj.AutoIdleCount,
                    obj.TriggerIdleCount)
                : obj).ToArray();
            var crafted = new PlayerSaveV1(saved.FormatVersion, saved.SaveSlot, saved.SavedTimes, saved.ContentIdentity,
                saved.Roles, saved.PartyRoleIds, saved.Inventory, saved.Cash,
                new PlayerSaveWorld(saved.World.SceneId, saved.World.Leader, objects,
                    saved.World.SceneScripts, saved.World.ObjectScripts, saved.World.Battlefield, saved.World.Music));
            using (var file = FileAccess.Open(storage.SlotPath(4), FileAccess.ModeFlags.Write))
                file!.StoreBuffer(PlayerSaveJson.EncodeToUtf8(crafted));

            await LoadThroughFormalMenu(host, fromSlot: 2, toSlot: 4);
            AssertThat(host.Main.Menu!.Mode).IsEqual(ExplorationMenuMode.Closed);
            AssertThat(host.Main.CurrentRecoverySlot).IsEqual(4);
            AssertThat(host.Main.Interaction!.Objects.Single(o => o.Id == 1).TriggerEntry).IsEqual((ushort)6);
            AssertThat(host.Main.Failure).IsNull();

            // The rebuilt session still consumes its checked authored entry on a real search.
            host.Tap(Key.Enter);
            await host.Wait(() => host.Main.Dialogue!.Session is { PendingDisplay: not null });
            AssertThat(host.Main.Dialogue!.Session!.StartedEntry!.SourceAddress).IsEqual(1);
            AssertThat(host.Main.Interaction!.PendingTrigger is not null).IsTrue();
            AssertThat(host.Main.Failure).IsNull();
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

    [TestCase(Timeout = 120000)]
    public async Task Lost_reload_reads_the_recovery_slot_and_switches_the_session_at_the_next_production_boundary()
    {
        // #228 (parent #148 Stories 41-43, Decisions 28-29): the completed failure story parks
        // the call at requestReload; the host registers that request once and reloads the
        // current recovery slot through the same #227 read/decode/commit chain at the NEXT
        // committed production boundary — never re-entering the script stack that parked it.
        // The saved panel expectations are the authored sample words plus the fixture's
        // explicit playerHp word (level 2 / hp 150 / maxHp 150 / mp 20 / maxMp 50), never a
        // value produced by the chain under test (Testing Decision 13).
        using var fixture = new AuthoredStoryFixture("LostReload", playerHp: 150);
        string saveRoot = "user://lost-reload-tests-" + Guid.NewGuid().ToString("N");
        var selection = GameSceneConfiguration.FromArguments("--story-profile", fixture.StoryUri,
            "--battle-visual-profile", fixture.VisualUri, "--battle-seed", "1", "--story-language", "en",
            "--save-directory", saveRoot);
        using var host = new LoadHost(selection);
        try
        {
            host.AssertHealthy();
            await host.Wait(() => host.Updates.Count > 0);
            var menu = host.Main.Menu!;
            string identity = host.Main.SaveContentIdentity!;
            AssertThat(host.Main.CurrentRecoverySlot).IsNull(); // Story 37: no default slot

            await SaveThroughFormalMenu(host, slot: 2);
            AssertThat(host.Main.CurrentRecoverySlot).IsEqual(2);
            var dialogueBefore = host.Main.Dialogue;
            var interactionBefore = host.Main.Interaction;
            // Synchronous UpdateCompleted sampling (the #227 boundary proof): every committed
            // update records whether the lost session still owns the host and whether the
            // terminal holds — a same-frame re-entry or a delayed switch both show up here.
            var samples = new System.Collections.Generic.List<(bool OldSession, bool Terminal)>();
            host.Main.UpdateCompleted += () =>
                samples.Add((ReferenceEquals(host.Main.Dialogue, dialogueBefore), host.Main.StoryLostTerminal));

            await DriveActualDefeat(host);

            // Registration boundary (Decision 28): the request's own committed update only
            // parked the terminal — it never rebuilt anything inside the script stack.
            int registered = samples.FindIndex(sample => sample.Terminal);
            AssertThat(registered >= 0).IsTrue();
            AssertThat(samples[registered].OldSession).IsTrue();
            // Processing happens at the NEXT committed boundary: after a bounded window of
            // further boundaries, the consecutive sample must already show the switched session.
            int boundary = host.Updates.Count;
            await host.Wait(() => host.Updates.Count >= boundary + 5);
            AssertThat(samples[registered + 1].OldSession).OverrideFailureMessage(
                "#228 lost reload: the registered requestReload must process at the next committed production boundary (read the current recovery slot and switch the rebuilt session); this run stayed parked in the retired PAL_LOST_RELOAD_TODO terminal.").IsFalse();
            AssertThat(samples[registered + 1].Terminal).IsFalse();

            // Story 43 / Testing Decision 13: the new session is a rebuilt identity over the
            // saved snapshot — the terminal, its red presentation and the failed world retire,
            // and the recovery slot stays this session's actually selected one (Decision 27).
            AssertThat(host.Main.StoryLostTerminal).IsFalse();
            AssertThat(ReferenceEquals(dialogueBefore, host.Main.Dialogue)).IsFalse();
            AssertThat(ReferenceEquals(interactionBefore, host.Main.Interaction)).IsFalse();
            AssertThat(host.Main.CurrentRecoverySlot).IsEqual(2);
            AssertThat((host.Main.Exploration!.Leader.X, host.Main.Exploration.Leader.Y)).IsEqual(((short)320, (short)320));
            AssertThat(host.Main.Interaction!.Phase).IsEqual(NpcInteractionPhase.Exploring);
            AssertThat(host.Main.StoryRedFadeStep).IsEqual(-1);

            // Story 32: the rebuilt menu consumes the same true state — the saved role panel,
            // not the dead lost world (every member fell to 0 hp before the reload).
            var status = await OpenStatusFromRememberedCursor(host);
            AssertThat((status.Level, status.Hp, status.MaxHp, status.Mp, status.MaxMp)).IsEqual((2, 150, 150, 20, 50));
            // uigame.c PAL_PlayerStatus: every status-page exit closes the whole menu
            // (PAL_InGameMenu case 1 continues with 'goto out') — close before the next flow.
            host.Tap(Key.Escape);
            await host.Wait(() => menu.Mode == ExplorationMenuMode.Closed);

            // The loaded lineage continues (#227's continuity): the next save through the
            // rebuilt session writes savedTimes 2 — a fresh story context would restart at 1.
            await SaveThroughFormalMenu(host, slot: 3);
            AssertThat(PlayerSaveJson.Decode(host.Main.SaveStorage!.ReadSlot(3)!, identity).SavedTimes).IsEqual(2);

            // Story 46: stale requests and old receipts never re-switch — many committed
            // boundaries later the rebuilt session is still the one true session.
            var reloaded = host.Main.Dialogue;
            int settled = host.Updates.Count;
            await host.Wait(() => host.Updates.Count >= settled + 10);
            AssertThat(ReferenceEquals(reloaded, host.Main.Dialogue)).IsTrue();
            AssertThat(host.Main.StoryLostTerminal).IsFalse();
            AssertThat(host.Main.Failure).IsNull();
        }
        finally
        {
            CleanupNamespace(saveRoot);
        }
    }

    [TestCase("corrupt-archive", Timeout = 120000)]
    [TestCase("missing-file", Timeout = 120000)]
    [TestCase("incompatible-archive", Timeout = 120000)]
    [TestCase("foreign-content-identity", Timeout = 120000)]
    [TestCase("preparation-failure", Timeout = 120000)]
    public async Task Lost_reload_failure_keeps_the_lost_session_closable_and_never_retries(string kind)
    {
        // #228 (parent #148 Decision 30, Stories 44/45/48; Testing Decision 13's rejection
        // classes): a defeat recovery that cannot complete closes in the closable failure
        // terminal with one located reason — the original lost session, the recovery slot
        // and every live owner stay untouched, and the terminal never auto-retries or fakes
        // a resume. The corrupt archive's located reason is the #223 codec's own pre-decode
        // diagnostic (independent of this host path); the missing file names the slot fact;
        // the incompatible archive is an independent JSON-text fixture carrying an unknown
        // formatVersion that no constructor policy could produce; the foreign content
        // identity is a well-formed save bound to other gameplay content; and the
        // preparation failure is a domain-valid save whose crafted inventory id is outside
        // the prepared item closure, so the #225 restore plan rejects fail-closed inside
        // the defeat path's own CommitLoadedSession catch — never a leaked host failure.
        // The no-current-slot class stays with the real-process no-slot regressions
        // (ObserveStoryLost / StoryMainProcessTests). This is the defeat-recovery state —
        // the formal menu's own save/load failures keep their separate receipts (Story 48).
        using var fixture = new AuthoredStoryFixture("LostReload", playerHp: 150);
        string saveRoot = "user://lost-reload-failure-tests-" + Guid.NewGuid().ToString("N");
        var selection = GameSceneConfiguration.FromArguments("--story-profile", fixture.StoryUri,
            "--battle-visual-profile", fixture.VisualUri, "--battle-seed", "1", "--story-language", "en",
            "--save-directory", saveRoot);
        using var host = new LoadHost(selection);
        try
        {
            host.AssertHealthy();
            await host.Wait(() => host.Updates.Count > 0);
            await SaveThroughFormalMenu(host, slot: 2);
            AssertThat(host.Main.CurrentRecoverySlot).IsEqual(2);
            var storage = host.Main.SaveStorage!;
            string identity = host.Main.SaveContentIdentity!;
            if (kind == "corrupt-archive")
            {
                // A corrupted slot-2 file this test owns (never a real player's slot).
                using (var file = FileAccess.Open(storage.SlotPath(2), FileAccess.ModeFlags.Write))
                    file!.StoreBuffer(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0xFF, 0xFE });
            }
            else if (kind == "missing-file")
            {
                DirAccess.RemoveAbsolute(storage.SlotPath(2));
            }
            else
            {
                // Each remaining class rewrites slot 2 from the actually saved snapshot with
                // exactly one independent violation (Testing Decision 3's fixed-format text
                // fixtures and literal expectations — never values produced by the chain
                // under test).
                var saved = PlayerSaveJson.Decode(storage.ReadSlot(2)!, identity);
                byte[] bytes;
                if (kind == "incompatible-archive")
                {
                    // An independent foreign-version file: the encoded JSON with only the
                    // version literal replaced, so the rejection exercises the real header
                    // policy instead of any constructor-produced snapshot.
                    bytes = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8
                        .GetString(PlayerSaveJson.EncodeToUtf8(saved))
                        .Replace("\"formatVersion\":1,", "\"formatVersion\":2,", StringComparison.Ordinal));
                }
                else
                {
                    PlayerSaveV1 crafted = kind == "foreign-content-identity"
                        ? new PlayerSaveV1(saved.FormatVersion, saved.SaveSlot, saved.SavedTimes,
                            "foreign-gameplay-content", saved.Roles, saved.PartyRoleIds,
                            saved.Inventory, saved.Cash, saved.World)
                        : new PlayerSaveV1(saved.FormatVersion, saved.SaveSlot, saved.SavedTimes,
                            saved.ContentIdentity, saved.Roles, saved.PartyRoleIds,
                            saved.Inventory.Append(new PlayerSaveInventorySlot(4242, 1, 0)).ToArray(),
                            saved.Cash, saved.World);
                    bytes = PlayerSaveJson.EncodeToUtf8(crafted);
                }
                using (var file = FileAccess.Open(storage.SlotPath(2), FileAccess.ModeFlags.Write))
                    file!.StoreBuffer(bytes);
            }
            var dialogueBefore = host.Main.Dialogue;

            await DriveActualDefeat(host);
            await host.Wait(() => host.Main.StoryLostReloadFailure is not null);
            string reason = host.Main.StoryLostReloadFailure!;
            // Each class checks its own located reason (Story 44): the codec's, the version
            // policy's, the identity binding's or the restore closure's diagnostic — the
            // missing file is the only exact-form slot fact.
            string expectedReason = kind switch
            {
                "missing-file" => "slot-missing",
                "incompatible-archive" => "version 2 is unknown and never migrates",
                "foreign-content-identity" => "is not the prepared context",
                "preparation-failure" => "is not in the prepared item closure",
                _ => "not valid UTF-8",
            };
            if (kind == "missing-file")
                AssertThat(reason).IsEqual(expectedReason);
            else
                AssertThat(reason.Contains(expectedReason, StringComparison.Ordinal)).IsTrue();
            AssertThat(host.Main.StoryLostTerminal).IsTrue(); // the closable terminal holds
            AssertThat(ReferenceEquals(dialogueBefore, host.Main.Dialogue)).IsTrue(); // the lost session is kept
            AssertThat(host.Main.CurrentRecoverySlot).IsEqual(2); // never switched away
            AssertThat(host.Main.Interaction!.Phase).IsEqual(NpcInteractionPhase.Trigger); // no business advanced
            AssertThat(host.Main.Failure).IsNull(); // a located rejection, not a host crash

            // Stories 45/46: closable and inert — real input and many more committed boundaries
            // change nothing: one registration, the same single failure, no retry, no resume.
            int boundary = host.Updates.Count;
            host.Tap(Key.Enter);
            host.Tap(Key.Q);
            await host.Wait(() => host.Updates.Count >= boundary + 20);
            AssertThat(host.Main.StoryLostReloadFailure).IsEqual(reason);
            AssertThat(host.Main.StoryLostReloadDiagnostics).IsEqual(1);
            AssertThat(host.Main.StoryLostTerminal).IsTrue();
            AssertThat(host.Main.Interaction!.Phase).IsEqual(NpcInteractionPhase.Trigger);
        }
        finally
        {
            CleanupNamespace(saveRoot);
        }
    }

    /// <summary>Drives the actual defeat through the formal chain (the ObserveStoryLost input
    /// sequence at real SceneRunner update boundaries): the search starts the pre-battle
    /// dialogue, the committed attack rounds trade into the killing enemy strike, the centered
    /// loss text advances and the completed failure story parks the call at requestReload.
    /// Never a private callback, a state shortcut or an injected Lost (Testing Decision 12).</summary>
    private static async Task DriveActualDefeat(LoadHost host)
    {
        host.Tap(Key.Enter); // the search that starts the checked selected trigger
        await host.Wait(() => host.Main.Dialogue!.Session!.PendingDisplay is not null);
        await host.Wait(() => host.Main.Dialogue!.Session!.PendingDisplay is
            { Display.Current.Phase: DialogueDisplayPhase.WaitingForAdvance });
        host.Tap(Key.Enter); // advance the pre-battle text into the action menu
        await host.Wait(() => host.Main.StoryBattleMode == Pal.Game.Dialogue.StoryBattlePresentation.AttackMenu);
        ulong deadline = Time.GetTicksMsec() + 60000;
        while (host.Main.Dialogue!.Session?.PendingLostReload is null && Time.GetTicksMsec() < deadline)
        {
            var session = host.Main.Dialogue!.Session!;
            if (session.PendingDisplay is { Display.Current.Phase: DialogueDisplayPhase.WaitingForAdvance })
                host.Tap(Key.Enter); // the centered loss text
            else if (host.Main.StoryBattleMode == Pal.Game.Dialogue.StoryBattlePresentation.AttackMenu)
                host.Tap(Key.Enter); // commit the selecting member's attack
            else if (host.Main.StoryBattleMode == Pal.Game.Dialogue.StoryBattlePresentation.TargetMenu)
                host.Tap(Key.Enter); // confirm the first alive target
            await host.Wait(() => true);
        }
        await host.Wait(() => host.Main.StoryLostTerminal);
    }

    /// <summary>Opens the status page from the remembered main-menu cursor (uigame.c keeps
    /// iCurMenuItem across close/reopen — a save visit parks it on the SYSTEM entry) and
    /// returns the live status projection. Browses by the observed cursor, never an assumed
    /// fresh position; the caller continues from the open status page.</summary>
    private static async Task<Pal.Core.ExplorationMenuStatusView> OpenStatusFromRememberedCursor(LoadHost host)
    {
        var menu = host.Main.Menu!;
        host.Tap(Key.Escape);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.MainMenu);
        for (int step = 0; step < menu.Entries.Count && menu.SelectedEntry != 0; step++)
        {
            host.Tap(Key.Up);
            await host.Wait(() => true);
        }
        AssertThat(menu.SelectedEntry).IsEqual(0);
        host.Tap(Key.Enter);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.Status);
        return menu.StatusView();
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
