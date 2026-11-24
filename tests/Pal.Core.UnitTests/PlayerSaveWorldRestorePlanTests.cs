using Pal.Core;
using Xunit;

namespace Pal.Core.Tests;

/// <summary>M6-C leaf #226 (parent #148): the world-domain restore plan (frozen matrix D10–D13
/// and restore steps 7–8 in docs/milestones/m6/m6c-save-matrix.md). Independent oracles are the
/// frozen #221 matrix, the fixed SDLPal reference rules (global.c PAL_SaveGame_Common/
/// PAL_LoadGame_Common load the whole rgEventObject/rgScene/rgObject words back; script.c
/// 0x006D/0x0090 slot writes; play.c PAL_GameUpdate scheduling) and hand-written literal fixture
/// data — never values produced by the plan under test.</summary>
public sealed class PlayerSaveWorldRestorePlanTests
{
    [Fact]
    public void Restore_rebuilds_the_world_domain_from_the_literal_v1_snapshot()
    {
        var fixture = new WorldFixture();
        var roles = PlayerSaveRestorePlan.Build(fixture.Save, fixture.RoleContent);
        var candidate = PlayerSaveWorldRestorePlan.BuildWorld(fixture.Save, fixture.WorldContent, roles);
        Assert.True(candidate is not null,
            "PLAYER_SAVE_WORLD_RESTORE_RED: a decoded v1 snapshot must rebuild the complete world-domain candidate through the A/B world and exploration construction chains");

        // D10: scene identity and the leader — the five saved words ride on the prepared static
        // identity; the walk-frame word and the sprite are content and never come from the file
        // (Story 24). The walking mid-step stays cleared: a restored boundary is non-walking.
        Assert.Equal(77, candidate.World.SceneId);
        Assert.Equal(WorldFixture.LeaderIdentity() with { X = 1200, Y = 800, Direction = 3, Layer = 1, StepPhase = 2 },
            candidate.World.Leader);

        // D5's second view: the restored world party is the role-domain candidate's saved order.
        Assert.Equal(new[] { 2, 4, 0 }, candidate.World.PartyRoles);

        // D11: every object's dynamic words field-by-field against independent literals —
        // including the redirected trigger entry 392 (a completed call's saved return entry),
        // the max WORD trigger counter 65535 on object 5, the mid-walk auto entry 16 with
        // co-existing auto idle counter 3 and trigger idle counter 17 on object 6, the hidden
        // state -2 whose vanish word 800 keeps its countdown progress, and 0 on object 7.
        Assert.Equal((1250, 850, 2, 1, 2, 0, 1, 392, 0, 0, 65535), Words(candidate.World, 5));
        Assert.Equal((1210, 830, 1, 2, -2, 800, 0, 0, 16, 3, 17), Words(candidate.World, 6));
        Assert.Equal((1300, 870, 0, 0, 3, 0, 2, 200, 0, 0, 0), Words(candidate.World, 7));

        // D12: the retained scene script slots — the current scene's saved row plus the
        // cross-scene modification of the unloaded scene 13 (Story 23: saving never truncates
        // to the visible nodes, and loading never executes the retained rows).
        Assert.Equal(2, candidate.World.SceneScripts.Count);
        Assert.Equal((40, 0), (candidate.World.SceneScripts[77].Item1, candidate.World.SceneScripts[77].Item2));
        Assert.Equal((0, 3), (candidate.World.SceneScripts[13].Item1, candidate.World.SceneScripts[13].Item2));
        // D12: the retained object script-slot writes (script.c 0x0090 rgwData[2+slot]).
        Assert.Equal(2, candidate.World.ObjectScripts.Count);
        Assert.Equal(328, candidate.World.ObjectScripts[(5, 2)]);
        Assert.Equal(77, candidate.World.ObjectScripts[(7, 0)]);
        // D13: the battlefield word; the setMusic words return for the host's D13 owner.
        Assert.Equal(3, candidate.World.Battlefield);
        Assert.Equal(new TriggerMusicState(3, 1), candidate.Music);

        // Step 8: the rebuilt exploration view — the saved leader, a fresh plan, non-walking,
        // and the camera anchored at the saved position (continuing never jumps to the start).
        Assert.False(candidate.Exploration.Current.IsWalking);
        Assert.Equal(candidate.World.Leader, candidate.Exploration.Current.Leader);
        Assert.Equal((1040, 688), (candidate.Exploration.Current.Plan.CameraX, candidate.Exploration.Current.Plan.CameraY));
        // The object matrix the session sees: saved dynamic words over prepared static identity
        // (sprite number/frames are the prepared content's words, not file bytes — Story 24).
        var view5 = candidate.Exploration.Objects.Single(o => o.Id == 5);
        Assert.Equal(((ushort)1250, (ushort)850, (ushort)392), (view5.X, view5.Y, view5.TriggerEntry));
        Assert.Equal((42, 3), (view5.SpriteNumber, view5.SpriteFrames));
        Assert.Same(WorldFixture.ObjectSprite, view5.Sprite);
        var view6 = candidate.Exploration.Objects.Single(o => o.Id == 6);
        Assert.Equal(((short)(-2), (short)800, (ushort)16, (ushort)2), (view6.State, view6.VanishTime, view6.AutoEntry, view6.CurrentFrame));
        var view7 = candidate.Exploration.Objects.Single(o => o.Id == 7);
        Assert.Equal((ushort)200, view7.TriggerEntry);
        // The hidden object drops out of the drawn plan while its vanish progress survives in
        // the world words; the visible objects draw from their saved positions.
        var plan = candidate.Exploration.Current.Plan;
        Assert.DoesNotContain(plan.Commands, command => command.Source == SceneDrawSource.Object && command.ObjectId == 6);
        Assert.Contains(plan.Commands, command => command.Source == SceneDrawSource.Object && command.ObjectId == 5);
        Assert.Contains(plan.Commands, command => command.Source == SceneDrawSource.Object && command.ObjectId == 7);
    }

    [Fact]
    public void Continued_exploration_starts_at_the_saved_position_on_a_new_scheduling_basis()
    {
        var fixture = new WorldFixture();
        var candidate = PlayerSaveWorldRestorePlan.BuildWorld(fixture.Save, fixture.WorldContent,
            PlayerSaveRestorePlan.Build(fixture.Save, fixture.RoleContent));

        // Decision 16: the restored session carries no old absolute deadline — the first update
        // at an arbitrary new tick commits exactly once (a huge "downtime" tick never replays
        // shutdown updates), and the same tick twice is not due twice: the new 100ms cadence
        // starts with the new session, not with the saved one's clock.
        Assert.True(candidate.Exploration.Update(5_000_000, 3));
        Assert.True(candidate.Exploration.Current.IsWalking);
        Assert.Equal(WorldFixture.LeaderIdentity() with { X = 1216, Y = 808, Direction = 3, Layer = 1, StepPhase = 3 },
            candidate.Exploration.Current.Leader);
        Assert.Equal((1056, 696), (candidate.Exploration.Current.Plan.CameraX, candidate.Exploration.Current.Plan.CameraY));
        Assert.False(candidate.Exploration.Update(5_000_000, 3));
        Assert.True(candidate.Exploration.Update(5_000_100, 3));
        Assert.Equal(1232, candidate.Exploration.Current.Leader.X);
    }

    [Fact]
    public void Repeated_restore_builds_isolated_candidates_and_copies_preserve_the_saved_words()
    {
        var fixture = new WorldFixture();
        var roles = PlayerSaveRestorePlan.Build(fixture.Save, fixture.RoleContent);
        var first = PlayerSaveWorldRestorePlan.BuildWorld(fixture.Save, fixture.WorldContent, roles);
        var second = PlayerSaveWorldRestorePlan.BuildWorld(fixture.Save, fixture.WorldContent, roles);

        // Every restored word of the two candidates is identical...
        Assert.Equal(first.World.SceneId, second.World.SceneId);
        Assert.Equal(first.World.Leader, second.World.Leader);
        Assert.Equal(first.World.SceneScripts.Count, second.World.SceneScripts.Count);
        Assert.Equal(first.World.SceneScripts[13], second.World.SceneScripts[13]);
        Assert.Equal(first.World.ObjectScripts[(5, 2)], second.World.ObjectScripts[(5, 2)]);
        Assert.Equal(first.World.Battlefield, second.World.Battlefield);
        Assert.Equal(first.Music, second.Music);
        Assert.Equal(Words(first.World, 6), Words(second.World, 6));

        // ...but the two candidates own independent storage: the host's #224 leader publication
        // on one world never dirties the other world or its exploration view.
        first.World.PublishLeader(WorldFixture.LeaderIdentity() with { X = 9, Y = 9 });
        Assert.Equal(1200, second.World.Leader.X);
        Assert.Equal(1200, second.Exploration.Current.Leader.X);

        // The world's own isolated copy (the preview-session path) preserves the restored
        // retained words — restore results survive the same copy semantics as a live world.
        var copy = first.World.CreateIsolatedCopy();
        Assert.Equal(first.World.SceneScripts[13], copy.SceneScripts[13]);
        Assert.Equal(first.World.ObjectScripts[(7, 0)], copy.ObjectScripts[(7, 0)]);
        Assert.Equal(Words(first.World, 6), Words(copy, 6));
    }

    [Fact]
    public void Unsupported_scene_or_object_closure_rejects_fail_closed_without_a_default_scene()
    {
        var fixture = new WorldFixture();
        var roles = PlayerSaveRestorePlan.Build(fixture.Save, fixture.RoleContent);

        // Decision 24: a scene the prepared closure cannot rebuild rejects with a locatable
        // diagnostic — never a silent downgrade to a default scene, never a partial candidate.
        var wrongScene = fixture.WorldContent with { SceneId = 78 };
        Assert.StartsWith("$.world.sceneId", Assert.Throws<InvalidDataException>(() =>
            PlayerSaveWorldRestorePlan.BuildWorld(fixture.Save, wrongScene, roles)).Message);

        // A saved object id outside the prepared scene is a dangling reference: fail closed.
        var unknownObject = new WorldFixture(WorldFixture.WorldWithObjects(
            WorldFixture.SavedObject(5), WorldFixture.SavedObject(6),
            new PlayerSaveObject(99, 10, 10, 0, 0, 1, 0, 0, 0, 0, 0, 0)));
        Assert.StartsWith("$.world.objects.objectId", Assert.Throws<InvalidDataException>(() =>
            PlayerSaveWorldRestorePlan.BuildWorld(unknownObject.Save, unknownObject.WorldContent,
                PlayerSaveRestorePlan.Build(unknownObject.Save, unknownObject.RoleContent))).Message);

        // A prepared object the saved world does not carry would silently reset to its initial
        // words — a content mismatch the restore rejects instead of repairing.
        var missingCoverage = fixture.WorldContent with { SceneRules = WorldFixture.RulesWithoutObject7() };
        Assert.StartsWith("$.world.objects", Assert.Throws<InvalidDataException>(() =>
            PlayerSaveWorldRestorePlan.BuildWorld(fixture.Save, missingCoverage, roles)).Message);

        // A real capture always carries the current scene's retained script row; its absence can
        // never correspond to a real capture and rejects the same fail-closed way.
        var noCurrentRow = new WorldFixture(WorldFixture.WorldWithoutCurrentSceneRow());
        Assert.StartsWith("$.world.sceneScripts", Assert.Throws<InvalidDataException>(() =>
            PlayerSaveWorldRestorePlan.BuildWorld(noCurrentRow.Save, noCurrentRow.WorldContent,
                PlayerSaveRestorePlan.Build(noCurrentRow.Save, noCurrentRow.RoleContent))).Message);
    }

    [Fact]
    public void A_consumed_auto_entry_resumes_from_the_saved_address_not_the_authored_one()
    {
        // A save whose object 6 sits mid-walk: the authored auto entry 15 (face west) was
        // already consumed and the word advanced to 16 (face south→2), idle counter 2, with the
        // object activated and facing east (3). Story 22: the restored dispatch resumes at 16 —
        // the already-consumed prefix never runs again from its original entry.
        var world = new PlayerSaveWorld(77,
            new PlayerSaveLeader(1200, 800, 3, 1, 2),
            new PlayerSaveObject[]
            {
                new(5, 1250, 850, 2, 1, 2, 0, 1, 1, 0, 0, 0),
                new(6, 1210, 830, 3, 1, 1, 0, 0, 0, 16, 2, 0),
                new(7, 1300, 870, 0, 0, 3, 0, 2, 200, 0, 0, 0),
            },
            new PlayerSaveSceneScript[] { new(77, 40, 0) },
            Array.Empty<PlayerSaveObjectScript>(), 3, null);
        var fixture = new WorldFixture(world);
        var candidate = PlayerSaveWorldRestorePlan.BuildWorld(fixture.Save, fixture.WorldContent,
            PlayerSaveRestorePlan.Build(fixture.Save, fixture.RoleContent));

        // The real dynamic-scene owner consumes the restored view: rules projected from the
        // saved words, the leader at the saved position, the scene's retained enter row.
        var events = Events();
        var session = new DynamicEventSession(
            WorldFixture.Rules().WithObjects(candidate.Exploration.Objects),
            candidate.World.Leader, events, Language(), events.GetEntry("scene", "npc"), 77, 40, 0);
        var restored6 = candidate.World.Objects.Single(o => o.Id == 6);
        // The plan's world owner carries the restored idle counter (D11 world state); the
        // SceneObject rules view the session consumes intentionally carries no idle word, so
        // the session's own idle counter starts at zero — the dispatch words below are exactly
        // what that view legitimately carries.
        Assert.Equal((16, 2, 3, 1), (restored6.AutoEntry, restored6.AutoIdleCount, restored6.Direction, restored6.State));
        var obj6 = session.World.Objects.Single(o => o.Id == 6);
        Assert.Equal((16, 3), (obj6.AutoEntry, obj6.Direction)); // restored, not reset to 15/0

        Assert.True(session.Update(100));
        Assert.Equal(2, obj6.Direction);   // address 16's face ran — not the consumed 15 (face 1)
        Assert.Equal(0, obj6.CurrentFrame);
        Assert.Equal(17, obj6.AutoEntry);  // advanced onto the end-keep at 17
    }

    [Fact]
    public void A_restored_trigger_idle_counter_resumes_the_owners_gate_instead_of_restarting_at_zero()
    {
        // EVENTOBJECT.nScriptIdleFrame is stored with the whole object (global.c:869): a saved
        // trigger counter of 1 must come back on the live owner so the next counted gate call
        // continues from it. With limit 2 the restored 1 pre-increments to 2, fails the
        // below-the-limit test, resets and falls through to L0002; restarting from zero would
        // instead take the branch, land on L0003 and leave the counter at 1.
        var world = new PlayerSaveWorld(77,
            new PlayerSaveLeader(1200, 800, 3, 1, 2),
            new PlayerSaveObject[]
            {
                new(5, 1250, 850, 2, 1, 2, 0, 1, 392, 0, 4, 1),
                new(6, 1210, 830, 1, 2, -2, 800, 0, 0, 16, 3, 65535),
                new(7, 1300, 870, 0, 0, 3, 0, 2, 200, 0, 0, 0),
            },
            new PlayerSaveSceneScript[] { new(77, 40, 0) },
            Array.Empty<PlayerSaveObjectScript>(), 3, null);
        var fixture = new WorldFixture(world);
        var candidate = PlayerSaveWorldRestorePlan.BuildWorld(fixture.Save, fixture.WorldContent,
            PlayerSaveRestorePlan.Build(fixture.Save, fixture.RoleContent));

        Assert.Equal(1, candidate.World.Objects.Single(o => o.Id == 5).TriggerIdleCount);
        Assert.Equal(4, candidate.World.Objects.Single(o => o.Id == 5).AutoIdleCount);
        Assert.Equal(65535, candidate.World.Objects.Single(o => o.Id == 6).TriggerIdleCount);
        Assert.Equal(3, candidate.World.Objects.Single(o => o.Id == 6).AutoIdleCount);

        var script = new TriggerScriptSession(CountedTriggerEvents(), Language(), candidate.World);
        script.Start("shared", "npc", 5);
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
        Assert.Equal(new TriggerPosition("shared", "L0002", 2), script.CurrentPosition);
        Assert.Equal(0, candidate.World.Objects.Single(o => o.Id == 5).TriggerIdleCount);
        Assert.Equal(4, candidate.World.Objects.Single(o => o.Id == 5).AutoIdleCount);
        Assert.Equal(65535, candidate.World.Objects.Single(o => o.Id == 6).TriggerIdleCount);
        Assert.Equal(3, candidate.World.Objects.Single(o => o.Id == 6).AutoIdleCount);
    }

    [Fact]
    public void A_world_saved_before_the_first_setMusic_restores_a_null_music_state()
    {
        // D13: the music words are null before the first setMusic; the restore returns the same
        // null owner state instead of inventing a default track.
        var fixture = new WorldFixture(WorldFixture.DefaultWorld(null));
        var candidate = PlayerSaveWorldRestorePlan.BuildWorld(fixture.Save, fixture.WorldContent,
            PlayerSaveRestorePlan.Build(fixture.Save, fixture.RoleContent));
        Assert.Null(candidate.Music);
    }

    /// <summary>One object's eleven dynamic words as a comparable row.</summary>
    private static (int X, int Y, int Direction, int CurrentFrame, int State, int VanishTime,
        int TriggerMode, int TriggerEntry, int AutoEntry, int AutoIdleCount, int TriggerIdleCount) Words(DynamicEventWorld world, int objectId)
    {
        var obj = world.Objects.Single(o => o.Id == objectId);
        return (obj.X, obj.Y, obj.Direction, obj.CurrentFrame, obj.State, obj.VanishTime,
            obj.TriggerMode, obj.TriggerEntry, obj.AutoEntry, obj.AutoIdleCount, obj.TriggerIdleCount);
    }

    private static DialogueLanguagePackage Language() =>
        new("en", [new DialogueMessageBlock(61, 61, [DialogueMessagePart.FromText("A")])]);

    /// <summary>The auto chain: 15 face west (the authored, already-consumed entry), 16 face 2
    /// (the saved resume point), 17 end keep. The trigger entry at 1 parks immediately; raw
    /// fillers use unregistered opcodes exactly like the dynamic-session fixtures.</summary>
    private static TriggerEventCollection Events() => TriggerEventCollection.Parse("""
        {"formatVersion":1,"profile":"faithful","source":{"archive":"SSS.MKF","chunk":4,"byteOrder":"little","instructionCount":18,"byteLength":144,"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},"files":["scene"]}
        """, new Dictionary<string, string>
        {
            ["scene"] = """
            {"formatVersion":1,"profile":"faithful","fileId":"scene","entries":[
              {"id":"npc","owner":{"type":"eventObject","id":5},"slot":"wTriggerScript","kind":"trigger","target":"L0001","sourceAddress":1}],
            "commands":[
              {"label":"L0000","op":"raw","next":"L0001","source":{"index":0,"opcode":134,"operands":[0,0,0]}},
              {"label":"L0001","op":"end","mode":"keep","next":"L0002","source":{"index":1,"opcode":0,"operands":[0,0,0]}},
              {"label":"L0002","op":"raw","next":"L0003","source":{"index":2,"opcode":139,"operands":[0,0,0]}},
              {"label":"L0003","op":"raw","next":"L0004","source":{"index":3,"opcode":140,"operands":[0,0,0]}},
              {"label":"L0004","op":"raw","next":"L0005","source":{"index":4,"opcode":141,"operands":[0,0,0]}},
              {"label":"L0005","op":"raw","next":"L0006","source":{"index":5,"opcode":142,"operands":[0,0,0]}},
              {"label":"L0006","op":"raw","next":"L0007","source":{"index":6,"opcode":143,"operands":[0,0,0]}},
              {"label":"L0007","op":"raw","next":"L0008","source":{"index":7,"opcode":134,"operands":[0,0,0]}},
              {"label":"L0008","op":"raw","next":"L0009","source":{"index":8,"opcode":139,"operands":[0,0,0]}},
              {"label":"L0009","op":"raw","next":"L0010","source":{"index":9,"opcode":140,"operands":[0,0,0]}},
              {"label":"L0010","op":"raw","next":"L0011","source":{"index":10,"opcode":141,"operands":[0,0,0]}},
              {"label":"L0011","op":"raw","next":"L0012","source":{"index":11,"opcode":142,"operands":[0,0,0]}},
              {"label":"L0012","op":"raw","next":"L0013","source":{"index":12,"opcode":143,"operands":[0,0,0]}},
              {"label":"L0013","op":"raw","next":"L0014","source":{"index":13,"opcode":134,"operands":[0,0,0]}},
              {"label":"L0014","op":"raw","next":"L0015","source":{"index":14,"opcode":139,"operands":[0,0,0]}},
              {"label":"L0015","op":"face","direction":1,"frame":0,"next":"L0016","source":{"index":15,"opcode":15,"operands":[1,0,0]}},
              {"label":"L0016","op":"face","direction":2,"frame":0,"next":"L0017","source":{"index":16,"opcode":15,"operands":[2,0,0]}},
              {"label":"L0017","op":"end","mode":"keep","next":null,"source":{"index":17,"opcode":0,"operands":[0,0,0]}}]}
            """,
        });

    /// <summary>A counted goto entry (script.c 0x0003, limit 2) owned by event object 5: the
    /// first call pre-increments the owner's trigger counter to 1 and takes the branch.</summary>
    private static TriggerEventCollection CountedTriggerEvents()
    {
        var fixture = new EventFixture();
        fixture.Index["source"]!["instructionCount"] = 4;
        fixture.Index["source"]!["byteLength"] = 32;
        fixture.Commands.Clear();
        fixture.Commands.Add(TriggerScriptControlFlowTests.Command(0, 134, """{"op":"raw"}""", 3));
        fixture.Commands.Add(TriggerScriptControlFlowTests.Command(1, 3, """{"op":"goto","to":"L0003","limit":2}""", 3));
        fixture.Commands.Add(TriggerScriptControlFlowTests.Command(2, 0, """{"op":"end","mode":"keep"}""", 3));
        fixture.Commands.Add(TriggerScriptControlFlowTests.Command(3, 0, """{"op":"end","mode":"keep"}""", 3));
        fixture.Entry["owner"]!["id"] = 5;
        fixture.Entry["target"] = "L0001";
        fixture.Entry["sourceAddress"] = 1;
        return fixture.Load();
    }

    /// <summary>Hand-written literal snapshot over the frozen v1 contract: six minimal role
    /// records with an empty equipment/magic domain (the role plan's zero closure), the party
    /// [2, 4, 0], one occupied bag row, and a fully non-default world domain — a moved leader,
    /// a redirected trigger entry, a mid-walk auto entry with idle progress, a hidden object
    /// with a live vanish countdown, a modified current-scene row plus an unloaded scene's
    /// retained row, object script-slot writes, the battlefield word and a setMusic pair. Every
    /// value is an independently hand-picked literal; none is derived from the plan under test.</summary>
    internal sealed class WorldFixture
    {
        internal static readonly SceneSprite LeaderSprite =
            new(41, Enumerable.Range(0, 16).Select(i => new SceneFrame(i, 20, 30)));
        internal static readonly SceneSprite ObjectSprite =
            new(42, Enumerable.Range(0, 12).Select(i => new SceneFrame(i, 16, 24)));

        public PlayerSaveV1 Save { get; }
        public PlayerSaveRestoreContent RoleContent { get; }
        public PlayerSaveWorldRestoreContent WorldContent { get; }

        public WorldFixture(PlayerSaveWorld? world = null)
        {
            Save = new PlayerSaveV1(PlayerSaveContract.CurrentVersion, saveSlot: 2, savedTimes: 5,
                "story/scene77@sha256:33cc44dd",
                new PlayerSaveRole[] { Role(0), Role(1), Role(2), Role(3), Role(4), Role(5) },
                [2, 4, 0],
                new PlayerSaveInventorySlot[] { new(7, 3, 2) },
                12345u,
                world ?? DefaultWorld(new PlayerSaveMusic(3, 1)));
            RoleContent = new(new Dictionary<int, BattleEquipmentObject>(), new HashSet<int>(),
                new HashSet<int> { 7 }, new Dictionary<int, int>());
            WorldContent = new(77, Rules(), LeaderIdentity());
        }

        public static PlayerSaveWorld DefaultWorld(PlayerSaveMusic? music) => new(77,
            new PlayerSaveLeader(1200, 800, 3, 1, 2),
            new PlayerSaveObject[]
            {
                SavedObject(5), SavedObject(6), SavedObject(7),
            },
            new PlayerSaveSceneScript[] { new(77, 40, 0), new(13, 0, 3) },
            new PlayerSaveObjectScript[] { new(5, 2, 328), new(7, 0, 77) },
            3, music);

        internal static PlayerSaveObject SavedObject(int objectId) => objectId switch
        {
            5 => new(5, 1250, 850, 2, 1, 2, 0, 1, 392, 0, 0, 65535),
            6 => new(6, 1210, 830, 1, 2, -2, 800, 0, 0, 16, 3, 17),
            _ => new(7, 1300, 870, 0, 0, 3, 0, 2, 200, 0, 0, 0),
        };

        public static PlayerSaveWorld WorldWithObjects(params PlayerSaveObject[] objects) => new(77,
            DefaultWorld(new PlayerSaveMusic(3, 1)).Leader, objects,
            DefaultWorld(new PlayerSaveMusic(3, 1)).SceneScripts,
            DefaultWorld(new PlayerSaveMusic(3, 1)).ObjectScripts, 3, new PlayerSaveMusic(3, 1));

        public static PlayerSaveWorld WorldWithoutCurrentSceneRow() => new(77,
            DefaultWorld(new PlayerSaveMusic(3, 1)).Leader, DefaultWorld(new PlayerSaveMusic(3, 1)).Objects,
            new PlayerSaveSceneScript[] { new(13, 0, 3) },
            DefaultWorld(new PlayerSaveMusic(3, 1)).ObjectScripts, 3, new PlayerSaveMusic(3, 1));

        /// <summary>The prepared scene static content: an open map, the trigger object 5 (entry
        /// 1), the auto object 6 (authored entry 15, initially hidden) and object 7.</summary>
        public static StaticSceneRules Rules() => new(
            new SceneMap(1, new uint[16384], [new SceneFrame(0, 32, 15)]),
        [
            new SceneObject(5, 304, 328, 2, 0, 0, 42, 3, 0, 0, ObjectSprite) { TriggerMode = 1, TriggerEntry = 1 },
            new SceneObject(6, 352, 320, 0, 0, 0, 42, 3, 0, 0, ObjectSprite) { TriggerMode = 0, AutoEntry = 15 },
            new SceneObject(7, 400, 340, 1, 0, 0, 42, 3, 3, 2, ObjectSprite) { TriggerMode = 2, TriggerEntry = 200 },
        ]);

        public static StaticSceneRules RulesWithoutObject7() => new(
            new SceneMap(1, new uint[16384], [new SceneFrame(0, 32, 15)]),
            Rules().Objects.Where(o => o.Id != 7));

        /// <summary>The prepared static leader identity: the authored start position and the
        /// content-owned sprite/walk-frame word the save never carries.</summary>
        public static StaticLeader LeaderIdentity() => new(320, 320, 0, 0, 0, 3, LeaderSprite);

        private static PlayerSaveRole Role(int roleId) => new(roleId, 3, 44, 44, 7, 20,
            new PlayerSaveExperience(30, 3, 1), new Dictionary<string, PlayerSaveExperience>(),
            new int[PlayerSaveContract.EquipmentSlotCount], Array.Empty<int>(), 0, 20, 19, 18, 17, 16);
    }
}
