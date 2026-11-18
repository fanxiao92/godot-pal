using System.Text.Json.Nodes;
using Pal.Core;
using Xunit;

namespace Pal.Core.Tests;

/// <summary>M6-C leaf #222 (parent #148): the stable-exploration save gate. Independent oracles
/// are the frozen #221 matrix/contract (docs/milestones/m6/m6c-save-matrix.md: retain/clear/
/// rebuild domains and per-domain capture points), the fixed SDLPal reference save entry
/// (uigame.c PAL_InGameMenu's system item → PAL_SystemMenu; script.c/global.c boundary rules)
/// and hand-written literal fixture data — never values produced by the gate under test.</summary>
public sealed class ExplorationSaveGateTests
{
    [Fact]
    public void Stable_boundary_captures_the_committed_snapshot_once_without_advancing_any_owner()
    {
        var fixture = new SaveFixture();
        var gate = new ExplorationSaveGate(fixture.Roles, fixture.Inventory, fixture.Cash, fixture.World);
        gate.NotifyProductionUpdateCommitted();

        // The formal system-menu entry: the main menu's System item (uigame.c PAL_InGameMenu)
        // opens the system-menu layer; #227's SAVE row enters the slot layer a save request
        // can originate from.
        var menu = fixture.Menu;
        menu.Open();
        Assert.Equal(ExplorationMenuMode.MainMenu, menu.Mode);
        menu.Press(ExplorationMenuKey.Down);
        menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationMenuMode.SystemItems, menu.Mode);
        menu.Press(ExplorationMenuKey.Confirm);

        var activity = new ExplorationSaveActivity(Battle: null, Trigger: fixture.CompletedTrigger,
            Dialogue: null, NpcInteraction: null, Menu: menu);
        string before = fixture.DescribeOwners();
        var outcome = gate.TryCapture(gate.CreateRequest(), activity, 3, 7, "story/scene12@sha256:5e7f2c");

        Assert.True(outcome.Save is not null,
            "PAL_SAVE_GATE_RED: a committed stable system-menu boundary must capture the full frozen v1 snapshot in one shot");
        Assert.True(outcome.Rejection == ExplorationSaveRejection.None && outcome.Captured,
            "a stable boundary must report exactly the None rejection with a captured snapshot");
        var save = outcome.Save!;

        // Header words are the host's own confirmations, carried verbatim (#221 D0/D1).
        Assert.Equal(1, save.FormatVersion);
        Assert.Equal(3, save.SaveSlot);
        Assert.Equal(7, save.SavedTimes);
        Assert.Equal("story/scene12@sha256:5e7f2c", save.ContentIdentity);

        // Six complete role records from the shared live owner, ordered by role identity (#221 D2).
        Assert.Equal(6, save.Roles.Count);
        Assert.Equal(0, save.Roles[0].RoleId);
        Assert.Equal((90, 90, 12, 30), (save.Roles[0].Hp, save.Roles[0].MaxHp, save.Roles[0].Mp, save.Roles[0].MaxMp));
        Assert.Equal(new PlayerSaveExperience(0, 1, 0), save.Roles[0].PrimaryExperience);
        Assert.Equal(new PlayerSaveExperience(17, 3, 4), save.Roles[0].HiddenExperience["health"]);
        Assert.Equal(7, save.Roles[0].HiddenExperience.Count);
        Assert.Empty(save.Roles[0].Magics);
        Assert.Equal((12, 11, 10, 9, 8), (save.Roles[0].Attack, save.Roles[0].MagicPower,
            save.Roles[0].Defense, save.Roles[0].Dexterity, save.Roles[0].FleeRate));
        var dead = save.Roles[3];
        Assert.Equal((3, 0, 480), (dead.RoleId, dead.Hp, dead.MaxHp)); // a dead role stays dead words
        var worn = save.Roles[5];
        Assert.Equal((5, 99, 1, 65535, 0, 65535), (worn.RoleId, worn.Level, worn.Hp, worn.MaxHp, worn.Mp, worn.MaxMp));
        Assert.Equal(new[] { 1, 0, 0, 0, 0, 0 }, worn.Equipment);
        Assert.Equal(new[] { 65, 0 }, worn.Magics);
        Assert.Equal(4, worn.CoveredByRoleId);
        Assert.Equal((210, 205, 200, 195, 190), (worn.Attack, worn.MagicPower, worn.Defense,
            worn.Dexterity, worn.FleeRate));

        // Party identity/order, occupied inventory rows (the depleted row stays occupied) and cash.
        Assert.Equal(new[] { 2, 0, 5 }, save.PartyRoleIds);
        Assert.Equal(new[]
        {
            new PlayerSaveInventorySlot(7, 3, 2),
            new PlayerSaveInventorySlot(65535, 0, 65535),
        }, save.Inventory);
        Assert.Equal(123456789u, save.Cash);

        // World domain: scene identity, leader words, every object's dynamic words, retained
        // script slots (including the cross-scene write), battlefield and the last setMusic.
        Assert.Equal(12, save.World.SceneId);
        Assert.Equal(new PlayerSaveLeader(-3192, 1032, 3, 1, 2), save.World.Leader);
        Assert.Equal(new[]
        {
            new PlayerSaveObject(5, 304, 328, 0, 0, 2, 0, 1, 1, 0, 0),
            new PlayerSaveObject(6, 352, 320, 0, 0, 0, 0, 0, 0, 15, 0),
        }, save.World.Objects);
        // Story 7 reverse case: recorded-but-not-executed auto entries never block the boundary —
        // object 6 keeps its nonzero auto entry word in the frozen snapshot untouched.
        Assert.Equal(new[]
        {
            new PlayerSaveSceneScript(12, 4, 9),
            new PlayerSaveSceneScript(13, 40, 0),
        }, save.World.SceneScripts);
        Assert.Equal(new[] { new PlayerSaveObjectScript(5, 0, 328) }, save.World.ObjectScripts);
        Assert.Equal(7, save.World.Battlefield);
        Assert.Equal(new PlayerSaveMusic(3, 1), save.World.Music);

        // Capture advanced nothing: every sampled public owner word is bit-identical afterwards
        // (#148 Decision 7 — no script, world tick, randomness or owner write during capture).
        Assert.Equal(before, fixture.DescribeOwners());
    }

    [Fact]
    public void Blocking_sessions_reject_with_locatable_reasons_and_no_state_effects()
    {
        var fixture = new SaveFixture();
        var gate = new ExplorationSaveGate(fixture.Roles, fixture.Inventory, fixture.Cash, fixture.World);
        gate.NotifyProductionUpdateCommitted();
        string before = fixture.DescribeLiveOwners();

        // A battle lifecycle in flight (its ClassicBattleSession owns the frame) blocks first.
        var battle = new ClassicBattleLifecycleSession(
            new BattleRoleSnapshot(1, 2, 100, 150, 20, 50, new(5, 2, 17), SevenTracks(), new int[6], new int[32], new int[9], 0),
            new ClassicBattlePlayer(1, 100, 150, 40, 20, 100),
            new ClassicBattleEnemy(7, 0, 43, 0, 0, 0, 1),
            new ClassicBattleGrowthData(Enumerable.Repeat(100, 100).ToArray(), []),
            new FirstDrawRandom());
        Assert.Equal(ExplorationSaveRejection.BattleActive,
            Reject(gate, fixture, battle: battle));

        // A parked display dialogue keeps the trigger call active.
        var display = ParkedTrigger(fixture.World,
            """{"op":"setDialog","box":"upper","portraitId":0,"color":0,"rngWord":0}""",
            """{"op":"showDialog","messageId":61,"descriptionLineWord":0}""");
        Assert.Equal(TriggerScriptPhase.WaitingForDisplay, display.Phase);
        Assert.Equal(ExplorationSaveRejection.TriggerActive, Reject(gate, fixture, trigger: display));

        // Counted movements (0x0010) and scene-updating waits (0x0009) are half-executed world
        // state — even with the menu already at the system-menu save layer (#148 Story 6).
        var movement = ParkedTrigger(fixture.World, """{"op":"walkTo","x":9,"y":21,"h":0}""");
        Assert.Equal(TriggerScriptPhase.WaitingForMovement, movement.Phase);
        fixture.Menu.Open();
        fixture.Menu.Press(ExplorationMenuKey.Down);
        fixture.Menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationMenuMode.SystemItems, fixture.Menu.Mode); // #227's item layer
        fixture.Menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationMenuMode.SystemMenu, fixture.Menu.Mode);
        Assert.Equal(ExplorationSaveRejection.MovementWaitPending, Reject(gate, fixture, trigger: movement));
        fixture.Menu.Press(ExplorationMenuKey.Cancel); // slot layer -> the #227 system items
        fixture.Menu.Press(ExplorationMenuKey.Cancel); // system items -> MainMenu for the later variants
        var sceneWait = ParkedTrigger(fixture.World,
            """{"op":"waitUpdate","frames":3,"runTriggers":0,"updateGestures":0}""");
        Assert.Equal(TriggerScriptPhase.WaitingForSceneWait, sceneWait.Phase);
        Assert.Equal(ExplorationSaveRejection.MovementWaitPending, Reject(gate, fixture, trigger: sceneWait));
        var transition = ParkedTrigger(fixture.World, """{"op":"redraw","delayUnits":2,"updateGestures":0}""");
        Assert.Equal(TriggerScriptPhase.WaitingForTransition, transition.Phase);
        Assert.Equal(ExplorationSaveRejection.TransitionPending, Reject(gate, fixture, trigger: transition));

        // A typing/waiting dialogue display and the NPC owner inside its trigger window block.
        var dialogue = new DialogueDisplaySession(
            new DialogueMessageBlock(61, 61, [DialogueMessagePart.FromText("A")]), 0x4F, 0);
        Assert.Equal(DialogueDisplayPhase.Typing, dialogue.Current.Phase);
        Assert.Equal(ExplorationSaveRejection.DialogueActive, Reject(gate, fixture, dialogue: dialogue));
        var npc = NpcInteractionSession.ForSelectedEntry(fixture.Rules, SearchLeader(), fixture.Entry);
        npc.PressSearch();
        Assert.True(npc.Update(100));
        Assert.Equal(NpcInteractionPhase.Trigger, npc.Phase);
        Assert.Equal(ExplorationSaveRejection.NpcInteractionActive, Reject(gate, fixture, npc: npc));

        // Only the system-menu layer is savable: absent, closed, main-menu and deep pages reject.
        fixture.Menu.Press(ExplorationMenuKey.Cancel); // MainMenu -> Closed
        Assert.Equal(ExplorationSaveRejection.MenuLayerNotSavable, Reject(gate, fixture));
        fixture.Menu.Open();
        Assert.Equal(ExplorationMenuMode.MainMenu, fixture.Menu.Mode);
        Assert.Equal(ExplorationSaveRejection.MenuLayerNotSavable, Reject(gate, fixture));
        fixture.Menu.Press(ExplorationMenuKey.Up); // wrap back to entry 0 (the status page)
        fixture.Menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationMenuMode.Status, fixture.Menu.Mode);
        Assert.Equal(ExplorationSaveRejection.MenuLayerNotSavable, Reject(gate, fixture));

        // Every rejection left the owners and the parked sessions exactly as they were.
        Assert.Equal(before, fixture.DescribeLiveOwners());
        Assert.Equal(TriggerScriptPhase.WaitingForDisplay, display.Phase);
        Assert.Equal(TriggerScriptPhase.WaitingForMovement, movement.Phase);
        Assert.Equal(TriggerScriptPhase.WaitingForSceneWait, sceneWait.Phase);
        Assert.Equal(TriggerScriptPhase.WaitingForTransition, transition.Phase);
        Assert.Equal(NpcInteractionPhase.Trigger, npc.Phase);
    }

    private static ExplorationSaveRejection Reject(ExplorationSaveGate gate, SaveFixture fixture,
        ClassicBattleLifecycleSession? battle = null, TriggerScriptSession? trigger = null,
        DialogueDisplaySession? dialogue = null, NpcInteractionSession? npc = null)
    {
        var outcome = gate.TryCapture(gate.CreateRequest(),
            new ExplorationSaveActivity(battle, trigger ?? fixture.CompletedTrigger, dialogue, npc, fixture.Menu),
            3, 7, "story/scene12@sha256:5e7f2c");
        Assert.Null(outcome.Save);
        Assert.False(outcome.Captured);
        return outcome.Rejection;
    }

    [Fact]
    public void Stale_or_uncommitted_requests_reject_fail_closed()
    {
        var fixture = new SaveFixture();
        var gate = new ExplorationSaveGate(fixture.Roles, fixture.Inventory, fixture.Cash, fixture.World);
        fixture.Menu.Open();
        fixture.Menu.Press(ExplorationMenuKey.Down);
        fixture.Menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationMenuMode.SystemItems, fixture.Menu.Mode); // #227's item layer
        fixture.Menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationMenuMode.SystemMenu, fixture.Menu.Mode);
        var activity = new ExplorationSaveActivity(null, fixture.CompletedTrigger, null, null, fixture.Menu);

        // Before any committed production update there is no rebuildable boundary at all (#148
        // Decision 6): no request identity exists and any attempted capture rejects.
        Assert.Equal(0, gate.CommittedBoundary);
        Assert.False(gate.HasCommittedProductionUpdate);
        Assert.Throws<InvalidOperationException>(() => gate.CreateRequest());
        Assert.Equal(ExplorationSaveRejection.UncommittedUpdate,
            gate.TryCapture(new ExplorationSaveRequest(Guid.NewGuid(), 1), activity, 3, 7, "story/scene12@sha256:5e7f2c").Rejection);

        // A request identity is valid only for its own committed boundary; once a further
        // production update commits, the earlier request is explicitly stale (Decision 7).
        gate.NotifyProductionUpdateCommitted();
        var request = gate.CreateRequest();
        Assert.Equal(1, request.Boundary);
        gate.NotifyProductionUpdateCommitted();
        var stale = gate.TryCapture(request, activity, 3, 7, "story/scene12@sha256:5e7f2c");
        Assert.Equal(ExplorationSaveRejection.StaleRequest, stale.Rejection);
        Assert.Null(stale.Save);

        // The current boundary still captures, and header violations still fail closed through
        // the frozen v1 contract instead of clamping or partial capture.
        Assert.True(gate.TryCapture(gate.CreateRequest(), activity, 3, 7, "story/scene12@sha256:5e7f2c").Captured);
        Assert.StartsWith("$.saveSlot", Assert.Throws<InvalidDataException>(() =>
            gate.TryCapture(gate.CreateRequest(), activity, 6, 0, "story/scene12@sha256:5e7f2c")).Message);
    }

    [Fact]
    public void System_menu_entry_declares_the_reference_save_layer_and_cancels_back()
    {
        var fixture = new SaveFixture();
        var menu = fixture.Menu;
        Assert.Equal(ExplorationMenuMode.Closed, menu.Mode);
        menu.Open();
        Assert.Equal(ExplorationMenuMode.MainMenu, menu.Mode);
        Assert.Equal("系统", menu.EntryLabel(1));

        // uigame.c PAL_InGameMenu's system item opens the system-menu layer (PAL_SystemMenu's
        // #227 item rows; its SAVE row enters the slot list).
        menu.Press(ExplorationMenuKey.Down);
        menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationMenuMode.SystemItems, menu.Mode);
        menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationMenuMode.SystemMenu, menu.Mode);
        // #224 replaces #222's provisional "every other key stays inert" leaf contract with the
        // frozen five-slot save flow: browsing only moves the remembered slot cursor (Stories
        // 3/4 — no request, no write, no switch), and Confirm parks exactly one pending save
        // request until the host's receipt (Story 12 — a duplicate confirm never a second).
        Assert.Equal(1, menu.SelectedSaveSlot);
        menu.Press(ExplorationMenuKey.Up);
        Assert.Equal(PlayerSaveContract.LastSlot, menu.SelectedSaveSlot); // PAL_ReadMenu wraps up
        menu.Press(ExplorationMenuKey.Down);
        Assert.Equal(1, menu.SelectedSaveSlot);
        Assert.Null(menu.PendingSave);
        menu.Press(ExplorationMenuKey.Confirm);
        var pending = menu.PendingSave;
        Assert.NotNull(pending);
        Assert.Equal(1, pending!.Slot);
        Assert.Throws<InvalidOperationException>(() => menu.Press(ExplorationMenuKey.Confirm));
        Assert.Throws<InvalidOperationException>(() => menu.Press(ExplorationMenuKey.Up));
        Assert.Throws<InvalidOperationException>(() => menu.Press(ExplorationMenuKey.Cancel));
        Assert.Same(pending, menu.PendingSave); // the parked request is unchanged by the rejected keys
        menu.CompleteSave(pending.Id, succeeded: false); // a failed write returns browsing identically
        Assert.Null(menu.PendingSave);
        Assert.Equal(ExplorationMenuMode.SystemMenu, menu.Mode);

        // kKeyMenu returns to the in-game main menu, and the system entry keeps its place:
        // the remembered cursor survives closes (gpGlobals->iCurMainMenuItem), so a reopen
        // still lands on the system entry and Confirm re-enters the layer without moving.
        menu.Press(ExplorationMenuKey.Cancel);
        Assert.Equal(ExplorationMenuMode.SystemItems, menu.Mode);
        menu.Press(ExplorationMenuKey.Cancel);
        Assert.Equal(ExplorationMenuMode.MainMenu, menu.Mode);
        menu.Press(ExplorationMenuKey.Cancel);
        Assert.Equal(ExplorationMenuMode.Closed, menu.Mode);
        menu.Open();
        Assert.Equal(1, menu.SelectedEntry);
        menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationMenuMode.SystemItems, menu.Mode);
        menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationMenuMode.SystemMenu, menu.Mode);
    }

    /// <summary>Hand-written literal fixture over the real A/B owners: six shared live roles, a
    /// bag with a depleted occupied row, the wallet, a real dynamic world (scene 12 with a live
    /// auto entry), a completed world-mutating trigger call (script slots, battlefield, music)
    /// and the exploration menu with the #222 system entry. Every value is an independent
    /// hand-picked literal; none is derived from the gate under test.</summary>
    private sealed class SaveFixture
    {
        public RoleLiveState Roles { get; }
        public InventoryLiveState Inventory { get; }
        public CashLiveState Cash { get; }
        public DynamicEventWorld World { get; }
        public TriggerScriptSession CompletedTrigger { get; }
        public ExplorationMenuSession Menu { get; }
        public StaticSceneRules Rules { get; }
        public StaticLeader Leader { get; }
        public TriggerEventEntry Entry { get; }

        public SaveFixture()
        {
            Roles = new RoleLiveState(new BattleRoleSnapshot[]
            {
                Role(0, level: 1, hp: 90, maxHp: 90, mp: 12, maxMp: 30, primary: (0, 1, 0),
                    equipment: [0, 0, 0, 0, 0, 0], magics: [], covered: 0, panel: (12, 11, 10, 9, 8)),
                Role(1, level: 2, hp: 111, maxHp: 222, mp: 5, maxMp: 40, primary: (18, 2, 7),
                    equipment: [0, 0, 0, 0, 0, 0], magics: [65, 0], covered: 2, panel: (30, 29, 28, 27, 26)),
                Role(2, level: 4, hp: 333, maxHp: 333, mp: 0, maxMp: 60, primary: (44, 5, 2),
                    equipment: [9, 0, 0, 0, 0, 0], magics: [71, 72], covered: 0, panel: (61, 62, 63, 64, 65)),
                Role(3, level: 2, hp: 0, maxHp: 480, mp: 1, maxMp: 80, primary: (21, 2, 9),
                    equipment: [0, 0, 0, 0, 0, 0], magics: [], covered: 1, panel: (71, 72, 73, 74, 75)),
                Role(4, level: 7, hp: 510, maxHp: 510, mp: 22, maxMp: 90, primary: (5, 7, 3),
                    equipment: [0, 2, 0, 0, 0, 0], magics: [65, 66, 0], covered: 5, panel: (81, 82, 83, 84, 85)),
                Role(5, level: 99, hp: 1, maxHp: 65535, mp: 0, maxMp: 65535, primary: (39, 4, 4),
                    equipment: [1, 0, 0, 0, 0, 0], magics: [65, 0], covered: 4, panel: (210, 205, 200, 195, 190)),
            });
            Inventory = new InventoryLiveState(
            [
                new InventorySlot(7, 3, 2),
                new InventorySlot(65535, 0, 65535), // depleted but still occupied: saved as-is
            ]);
            Cash = new CashLiveState(123456789u);
            Rules = RulesFactory();
            Leader = LeaderFactory();
            World = DynamicEventWorld.ForHost(Rules, Leader, sceneId: 12, onEnter: 4, onTeleport: 9,
                Roles, [2, 0, 5]);

            // A real completed trigger call mutates the world exactly like production: one
            // cross-scene script-slot write, one object script slot, the battlefield word and
            // the last setMusic. It ends Completed — a resting session never blocks the gate.
            var events = TriggerEvents();
            Entry = events.GetEntry("scene", "npc");
            CompletedTrigger = new TriggerScriptSession(events, Language(), World);
            CompletedTrigger.Start("scene", "npc", 5);
            Assert.Equal(TriggerScriptPhase.Completed, CompletedTrigger.Phase);

            Menu = new ExplorationMenuSession(
            [
                new ExplorationMenuEntry(ExplorationMenuPage.Status, ExplorationMenuSession.StatusEntryWord),
                new ExplorationMenuEntry(ExplorationMenuPage.System, ExplorationMenuSession.SystemEntryWord),
            ],
            [
                new ExplorationMenuMember(2, 60102, false, 1, 1, 1, 1, 1),
                new ExplorationMenuMember(0, 60100, false, 1, 1, 1, 1, 1),
                new ExplorationMenuMember(5, 60105, false, 1, 1, 1, 1, 1),
            ],
            Roles, ZeroEquipment(), new Dictionary<int, ClassicBattleGrowthData>
            {
                [0] = Growth([9, 10, 11, 111]),
                [2] = Growth([90, 100, 111, 12, 140]),
                [5] = Growth(Enumerable.Range(1, 101).Select(value => 100 + value).ToArray()),
            },
            Words());
        }

        /// <summary>Every sampled public owner word in one comparable string, for the
        /// capture-changes-nothing assertion.</summary>
        public string DescribeOwners() => DescribeLiveOwners() + ";" + Menu.Mode;

        /// <summary>The same sample without the menu mode, for suites that legitimately move the
        /// menu between samples.</summary>
        public string DescribeLiveOwners() => string.Join(";",
            Roles.Roles[0].Hp, Roles.Roles[0].Mp, Roles.Roles[5].Level, Roles.Roles[5].Hp,
            string.Join(",", Roles.Roles[5].Equipment), string.Join(",", Roles.Roles[5].Magics),
            string.Join(",", Inventory.Slots.Take(2).Select(slot => $"{slot.ItemId}:{slot.Amount}:{slot.AmountInUse}")),
            Cash.Cash,
            World.SceneId, World.Leader.X, World.Leader.Y, World.Leader.Direction, World.Leader.Layer, World.Leader.StepPhase,
            World.Battlefield,
            string.Join(",", World.PartyRoles),
            string.Join(",", World.Objects.Select(obj =>
                $"{obj.Id}:{obj.X}:{obj.Y}:{obj.Direction}:{obj.State}:{obj.VanishTime}:{obj.TriggerMode}:{obj.TriggerEntry}:{obj.AutoEntry}:{obj.AutoIdleCount}")),
            string.Join(",", World.SceneScripts.OrderBy(pair => pair.Key)
                .Select(pair => $"{pair.Key}:{pair.Value.Item1}:{pair.Value.Item2}")),
            string.Join(",", World.ObjectScripts.OrderBy(pair => pair.Key.ObjectId).ThenBy(pair => pair.Key.SlotIndex)
                .Select(pair => $"{pair.Key.ObjectId}:{pair.Key.SlotIndex}:{pair.Value}")),
            CompletedTrigger.Phase, CompletedTrigger.Music is { } music ? $"{music.MusicId}:{music.Mode}" : "none");

        private static StaticSceneRules RulesFactory()
        {
            SceneSprite ObjectSprite() => new(42, Enumerable.Range(0, 12).Select(i => new SceneFrame(i, 16, 24)));
            return new(new SceneMap(10, new uint[16384], [new SceneFrame(0, 32, 15)]),
            [
                new SceneObject(5, 304, 328, 2, 0, 0, 42, 3, 0, 0, ObjectSprite()) { TriggerMode = 1, TriggerEntry = 1 },
                new SceneObject(6, 352, 320, 0, 0, 0, 42, 3, 0, 0, ObjectSprite()) { TriggerMode = 0, AutoEntry = 15 },
            ]);
        }

        private static StaticLeader LeaderFactory() => new(-3192, 1032, 3, 1, 2, 3,
            new SceneSprite(41, Enumerable.Range(0, 16).Select(i => new SceneFrame(i, 20, 30))));

        /// <summary>A trigger event whose one call writes one cross-scene script slot, one object
        /// script slot, the battlefield word and the last setMusic, then ends keep.</summary>
        private static TriggerEventCollection TriggerEvents()
        {
            JsonObject Command(string body) => (JsonObject)JsonNode.Parse(body)!;
            var commands = new JsonArray
            {
                Command("""{"label":"L0000","op":"raw","next":"L0001","source":{"index":0,"opcode":134,"operands":[0,65535,8]}}"""),
                Command("""{"label":"L0001","op":"setSceneScripts","sceneId":13,"onEnter":40,"onTeleport":0,"next":"L0002","source":{"index":1,"opcode":109,"operands":[13,40,0]}}"""),
                Command("""{"label":"L0002","op":"setObjectScript","objectId":5,"value":328,"slotIndex":0,"next":"L0003","source":{"index":2,"opcode":144,"operands":[5,328,0]}}"""),
                Command("""{"label":"L0003","op":"setBattlefield","battlefieldId":7,"next":"L0004","source":{"index":3,"opcode":74,"operands":[7,0,0]}}"""),
                Command("""{"label":"L0004","op":"setMusic","musicId":3,"mode":1,"next":"L0005","source":{"index":4,"opcode":67,"operands":[3,1,0]}}"""),
                Command("""{"label":"L0005","op":"end","mode":"keep","next":null,"source":{"index":5,"opcode":0,"operands":[0,0,0]}}"""),
            };
            var file = new JsonObject { ["formatVersion"] = 1, ["profile"] = "faithful", ["fileId"] = "scene" };
            file["entries"] = new JsonArray((JsonObject)JsonNode.Parse(
                """{"id":"npc","owner":{"type":"eventObject","id":5},"slot":"wTriggerScript","kind":"trigger","target":"L0001","sourceAddress":1}""")!);
            file["commands"] = commands;
            var index = (JsonObject)JsonNode.Parse(
                """{"formatVersion":1,"profile":"faithful","source":{"archive":"SSS.MKF","chunk":4,"byteOrder":"little","instructionCount":6,"byteLength":48,"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},"files":["scene"]}""")!;
            return TriggerEventCollection.Parse(index.ToJsonString(),
                new Dictionary<string, string> { ["scene"] = file.ToJsonString() });
        }

        internal static DialogueLanguagePackage Language() => new("en",
            [new DialogueMessageBlock(61, 61, [DialogueMessagePart.FromText("A")])]);

        private static Dictionary<int, string> Words() => new()
        {
            [ExplorationMenuSession.StatusEntryWord] = "状态",
            [ExplorationMenuSession.SystemEntryWord] = "系统",
            [ExplorationMenuSession.SaveItemWord] = "存档",
            [ExplorationMenuSession.LoadItemWord] = "读取",
            [60100] = "大侠",
            [60102] = "侠三",
            [60105] = "剑客",
        };

        private static BattleEquipmentState ZeroEquipment() => BattleEquipmentState.Initialize(
            Enumerable.Range(0, 6).Select(_ => (IReadOnlyList<int>)[0, 0, 0, 0, 0, 0]).ToArray(),
            new Dictionary<int, BattleEquipmentObject>());

        private static ClassicBattleGrowthData Growth(int[] thresholds) => new(thresholds, []);

        private static BattleRoleSnapshot Role(int roleId, int level, int hp, int maxHp, int mp, int maxMp,
            (int Value, int Level, int Count) primary, int[] equipment, int[] magics, int covered,
            (int Attack, int MagicPower, int Defense, int Dexterity, int FleeRate) panel) => new(
            roleId, level, hp, maxHp, mp, maxMp,
            new BattleExperienceSnapshot(primary.Value, primary.Level, primary.Count),
            Hidden(roleId),
            equipment, magics, new int[9], covered,
            panel.Attack, panel.MagicPower, panel.Defense, panel.Dexterity, panel.FleeRate);

        private static IReadOnlyDictionary<string, BattleExperienceSnapshot> Hidden(int roleId) =>
            new Dictionary<string, BattleExperienceSnapshot>
            {
                ["health"] = new(roleId == 5 ? 71 : 17, 3, roleId == 5 ? 5 : 4),
                ["magic"] = new(0, 3, 0),
                ["attack"] = new(roleId == 5 ? 88 : 87, 3, 1),
                ["magicPower"] = new(5, 2, 9),
                ["defense"] = new(0, 3, 0),
                ["dexterity"] = new(1, 1, 1),
                ["flee"] = new(0, 3, 0),
            };
    }

    /// <summary>A leader whose real search press hits owner 5 at sample 1 (the dynamic-host
    /// fixture layout), for the NPC-interaction blocking case.</summary>
    private static StaticLeader SearchLeader() => new(320, 320, 0, 0, 0, 3,
        new SceneSprite(41, Enumerable.Range(0, 16).Select(i => new SceneFrame(i, 20, 30))));

    private static IReadOnlyDictionary<string, BattleExperienceSnapshot> SevenTracks() =>
        new Dictionary<string, BattleExperienceSnapshot>
        {
            ["health"] = new(7, 3, 55), ["magic"] = new(7, 3, 55), ["attack"] = new(7, 3, 55),
            ["magicPower"] = new(7, 3, 55), ["defense"] = new(7, 3, 55), ["dexterity"] = new(7, 3, 55),
            ["flee"] = new(7, 3, 55),
        };

    private sealed class FirstDrawRandom : IBattleRandom
    {
        public int NextInclusive(int from, int to) => from;
        public float NextInclusive(float from, float to) => from;
    }

    /// <summary>Builds a trigger call over the given world that parks on the first body command
    /// (display/movement/scene-wait/transition), exactly like a production parking.</summary>
    internal static TriggerScriptSession ParkedTrigger(DynamicEventWorld world, params string[] bodies)
    {
        JsonObject Command(int index, string body, bool last)
        {
            var command = (JsonObject)JsonNode.Parse(body)!;
            command["label"] = $"L{index:D4}";
            command["next"] = last ? null : $"L{index + 1:D4}";
            int opcode = command["op"]!.GetValue<string>() switch
            {
                "raw" => 134, "setDialog" => 60, "showDialog" => 65535, "walkTo" => 16,
                "waitUpdate" => 9, "redraw" => 5, "end" => 0,
                _ => throw new InvalidOperationException("test command opcode unmapped"),
            };
            command["source"] = JsonNode.Parse(
                $$"""{"index":{{index}},"opcode":{{opcode}},"operands":[0,0,0]}""");
            return command;
        }
        var commands = new JsonArray { Command(0, """{"op":"raw"}""", last: false) };
        for (int i = 0; i < bodies.Length; i++)
            commands.Add(Command(i + 1, bodies[i], last: false));
        commands.Add(Command(bodies.Length + 1, """{"op":"end","mode":"keep"}""", last: true));
        var file = new JsonObject { ["formatVersion"] = 1, ["profile"] = "faithful", ["fileId"] = "scene" };
        file["entries"] = new JsonArray((JsonObject)JsonNode.Parse(
            """{"id":"npc","owner":{"type":"eventObject","id":5},"slot":"wTriggerScript","kind":"trigger","target":"L0001","sourceAddress":1}""")!);
        file["commands"] = commands;
        var index = (JsonObject)JsonNode.Parse(
            $$"""{"formatVersion":1,"profile":"faithful","source":{"archive":"SSS.MKF","chunk":4,"byteOrder":"little","instructionCount":{{bodies.Length + 2}},"byteLength":{{(bodies.Length + 2) * 8}},"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},"files":["scene"]}""")!;
        var events = TriggerEventCollection.Parse(index.ToJsonString(),
            new Dictionary<string, string> { ["scene"] = file.ToJsonString() });
        var trigger = new TriggerScriptSession(events, SaveFixture.Language(), world);
        trigger.Start("scene", "npc", 5);
        return trigger;
    }
}
