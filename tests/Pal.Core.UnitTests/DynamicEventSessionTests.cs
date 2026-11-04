using System.Text.Json.Nodes;
using Pal.Core;
using Xunit;

namespace Pal.Core.Tests;

public class DynamicEventSessionTests
{
    // Fixture layout (19 words): 0 raw | 1-9 trigger chain (5-8 flexible slots, 9 end keep)
    // | 10-14 raw fillers | 15-18 auto chain (face, counted walkTo, hide, end keep).
    // The auto object 6 starts hidden (state 0); the event's setState activates it.
    [Fact]
    public void Complete_chain_dialogue_wait_auto_counted_movement_hide_and_retained_exploration()
    {
        var fixture = new DynamicFixture();
        var session = NewSession(fixture);
        var obj5 = session.World.Objects.Single(o => o.Id == 5);
        var obj6 = session.World.Objects.Single(o => o.Id == 6);
        Assert.Equal(0, obj6.State);

        // Real search input starts the event (sample 1 hits owner 5).
        session.PressSearch();
        Assert.True(session.Update(100));
        var request = session.PendingTrigger!;
        Assert.NotNull(request);
        Assert.Equal(5, request.Caller);
        Assert.Equal(1, request.SourceAddress);

        // The dialogue parks the call; the world stays frozen until its display completes.
        var trigger = session.StartTrigger(request.Id);
        Assert.Equal(TriggerScriptPhase.WaitingForDisplay, trigger.Phase);
        var display = trigger.PendingDisplay!;
        Assert.NotNull(display);
        Assert.False(session.Update(200));
        display.Display.Update(24, false, true);
        display.Display.Update(100, true, false);
        Assert.True(trigger.AcknowledgeDisplay(display.Id));
        Assert.Equal(TriggerScriptPhase.WaitingForSceneWait, trigger.Phase);
        Assert.Equal(1, obj6.State);
        Assert.Equal(3, trigger.PendingSceneWait!.FramesRemaining);

        // Wait tick 1: the auto script runs one instruction (0x000F face west).
        Assert.True(session.Update(300));
        Assert.Equal(1, obj6.Direction);
        Assert.Equal(0, obj6.CurrentFrame);
        Assert.Equal(16, obj6.AutoEntry);
        Assert.Equal(2, trigger.PendingSceneWait!.FramesRemaining);
        // Wait tick 2: first counted West step (-6,-3) with the walking frame cycle.
        Assert.True(session.Update(400));
        Assert.Equal((346, 317), (obj6.X, obj6.Y));
        Assert.Equal(1, obj6.CurrentFrame);
        Assert.Equal(16, obj6.AutoEntry);
        // Wait tick 3: second step; the wait ends and the event's own walkTo parks as a movement.
        Assert.True(session.Update(500));
        Assert.Equal((340, 314), (obj6.X, obj6.Y));
        Assert.Equal(TriggerScriptPhase.WaitingForMovement, trigger.Phase);
        var movement = trigger.PendingMovement!;
        Assert.NotNull(movement);
        Assert.Equal((288, 336), (movement.TargetX, movement.TargetY));
        // Movement tick 1: South step (-6,+3) toward the tile; the auto object stays frozen.
        Assert.True(session.Update(600));
        Assert.Equal((298, 331), (obj5.X, obj5.Y));
        Assert.Equal((340, 314), (obj6.X, obj6.Y));
        Assert.Equal(TriggerScriptPhase.WaitingForMovement, trigger.Phase);
        // Movement tick 2: near-target snap completes the counted movement, then face + end keep.
        Assert.True(session.Update(700));
        Assert.Equal((288, 336), (obj5.X, obj5.Y));
        Assert.Equal(0, obj5.Direction);
        Assert.Equal(0, obj5.CurrentFrame);
        Assert.Equal(TriggerScriptPhase.Completed, trigger.Phase);
        // Completion tick: the saved return entry and success flag, then exploration resumes.
        Assert.True(session.Update(800));
        Assert.Null(session.Trigger);
        Assert.Equal(1, obj5.TriggerEntry);
        Assert.True(session.LastScriptSuccess);
        // Exploration ticks keep driving the auto walk one step per due tick (steps 3..8).
        Assert.True(session.Update(900));
        Assert.Equal((334, 311), (obj6.X, obj6.Y));
        for (uint tick = 1000; tick <= 1400; tick += 100) Assert.True(session.Update(tick));
        Assert.Equal((304, 296), (obj6.X, obj6.Y));
        Assert.Equal(16, obj6.AutoEntry);
        // Step 9 then the near-target snap; arrival resets the standing frame.
        Assert.True(session.Update(1500));
        Assert.Equal((298, 293), (obj6.X, obj6.Y));
        Assert.True(session.Update(1600));
        Assert.Equal((288, 288), (obj6.X, obj6.Y));
        Assert.Equal(0, obj6.CurrentFrame);
        Assert.Equal(17, obj6.AutoEntry);
        // Hide: state flips negative and the vanish word starts counting down from 800.
        Assert.True(session.Update(1700));
        Assert.Equal(-1, obj6.State);
        Assert.Equal(800, obj6.VanishTime);
        Assert.Equal(18, obj6.AutoEntry);
        // The retained exploration keeps counting the vanish word once per due tick.
        Assert.True(session.Update(1800));
        Assert.Equal(799, obj6.VanishTime);
        // A single late update advances exactly one tick; it never catches up.
        Assert.True(session.Update(9000));
        Assert.Equal(798, obj6.VanishTime);
        // Retention: the hidden object disappears from the plan; the searched object remains.
        Assert.False(session.Current.Plan.Commands.Any(c => c.Source == SceneDrawSource.Object && c.ObjectId == 6));
        Assert.Contains(session.Current.Plan.Commands, c => c.Source == SceneDrawSource.Object && c.ObjectId == 5);
    }

    [Fact]
    public void Scene_wait_gates_vanish_countdown_by_runTriggers_and_autos_always_run()
    {
        // The auto object hides itself first (vanish 5), so the second wait tick observes the
        // countdown: runTriggers=1 counts it (play.c PAL_GameUpdate(TRUE)).
        var counting = new DynamicFixture();
        counting.Commands[4] = Command(4, """{"op":"waitUpdate","frames":2,"runTriggers":1,"updateGestures":0}""");
        counting.Commands[5] = Command(5, """{"op":"face","direction":2,"frame":0}""");
        counting.Commands[15] = Command(15, """{"op":"hide","frames":5}""");
        counting.Commands[16] = Command(16, """{"op":"end","mode":"keep"}""");
        var session = NewSession(counting);
        var obj6 = session.World.Objects.Single(o => o.Id == 6);
        StartAndCompleteDialogue(session);
        Assert.True(session.Update(300)); // tick 1: auto hides (vanish 5), wait 2 -> 1
        Assert.Equal(5, obj6.VanishTime);
        Assert.True(session.Update(400)); // tick 2: the trigger-enabled countdown moves the word
        Assert.Equal(4, obj6.VanishTime);
        Assert.Equal(TriggerScriptPhase.Completed, session.Trigger!.Phase);

        // runTriggers=0 keeps PAL_GameUpdate(FALSE): autos still run, the vanish word does not.
        var frozenFixture = new DynamicFixture();
        frozenFixture.Commands[4] = Command(4, """{"op":"waitUpdate","frames":2,"runTriggers":0,"updateGestures":0}""");
        frozenFixture.Commands[5] = Command(5, """{"op":"face","direction":2,"frame":0}""");
        frozenFixture.Commands[15] = Command(15, """{"op":"hide","frames":5}""");
        frozenFixture.Commands[16] = Command(16, """{"op":"end","mode":"keep"}""");
        var frozen = NewSession(frozenFixture);
        var frozenObject = frozen.World.Objects.Single(o => o.Id == 6);
        StartAndCompleteDialogue(frozen);
        Assert.True(frozen.Update(300));
        Assert.Equal(5, frozenObject.VanishTime);
        Assert.True(frozen.Update(400));
        Assert.Equal(5, frozenObject.VanishTime); // frozen countdown, auto already parked
    }

    [Fact]
    public void Scene_wait_updateGestures_cycles_the_party_walk_gesture_once_per_tick()
    {
        var cycling = new DynamicFixture();
        cycling.Commands[4] = Command(4, """{"op":"waitUpdate","frames":1,"runTriggers":0,"updateGestures":1}""");
        cycling.Commands[5] = Command(5, """{"op":"face","direction":2,"frame":0}""");
        var session = NewSession(cycling);
        StartAndCompleteDialogue(session);
        int afterSearch = session.Current.Leader.StepPhase;
        Assert.True(session.Update(300)); // the gesture word advances with the waited frame
        Assert.Equal((afterSearch + 1) % 4, session.Current.Leader.StepPhase);

        var still = new DynamicFixture();
        still.Commands[4] = Command(4, """{"op":"waitUpdate","frames":1,"runTriggers":0,"updateGestures":0}""");
        still.Commands[5] = Command(5, """{"op":"face","direction":2,"frame":0}""");
        var frozen = NewSession(still);
        StartAndCompleteDialogue(frozen);
        int baseline = frozen.Current.Leader.StepPhase;
        Assert.True(frozen.Update(300)); // no gesture word: the party pose does not cycle
        Assert.Equal(baseline, frozen.Current.Leader.StepPhase);
    }

    [Fact]
    public void Party_scene_and_cross_scene_rewrites_are_retained_after_the_event()
    {
        var fixture = new DynamicFixture();
        fixture.Commands[5] = Command(5, """{"op":"setPartyPos","x":11,"y":10,"h":1}""");
        fixture.Commands[6] = Command(6, """{"op":"setPartyFacing","direction":3,"gesture":2,"memberIndex":0}""");
        fixture.Commands[7] = Command(7, """{"op":"setSceneScripts","sceneId":2,"onEnter":7,"onTeleport":0}""");
        fixture.Commands[8] = Command(8, """{"op":"setObjectScript","objectId":777,"value":13,"slotIndex":4}""");
        var session = NewSession(fixture);
        StartAndCompleteDialogue(session);
        for (uint tick = 300; tick <= 900; tick += 100) Assert.True(session.Update(tick));
        Assert.Null(session.Trigger);
        // script.c 0x0046: party world position = x*32+h*16, y*16+h*8.
        Assert.Equal((368, 168), (session.World.Leader.X, session.World.Leader.Y));
        Assert.Equal((368, 168), (session.Current.Leader.X, session.Current.Leader.Y));
        Assert.Equal(3, session.Current.Leader.Direction);
        // Cross-scene rewrites are saved data; scene 2 enter/teleport words and object 777 slot 4.
        Assert.Equal((7, 0), (session.World.SceneScripts[2].Item1, session.World.SceneScripts[2].Item2));
        Assert.Equal(13, session.World.ObjectScripts[(777, 4)]);
    }

    [Fact]
    public void markFailed_clears_the_saved_success_flag_and_trigger_mode_state_is_kept()
    {
        var fixture = new DynamicFixture();
        fixture.Commands[5] = Command(5, """{"op":"setTriggerMode","objectId":6,"mode":2}""");
        fixture.Commands[6] = Command(6, """{"op":"markFailed"}""");
        var session = NewSession(fixture);
        StartAndCompleteDialogue(session);
        for (uint tick = 300; tick <= 900; tick += 100) Assert.True(session.Update(tick));
        Assert.Null(session.Trigger);
        Assert.False(session.LastScriptSuccess);
        Assert.Equal(2, session.World.Objects.Single(o => o.Id == 6).TriggerMode);
    }

    [Fact]
    public void Wait_movement_and_transition_receipts_reject_premature_stale_and_duplicate()
    {
        var fixture = new DynamicFixture();
        var session = NewSession(fixture);
        var trigger = StartAndCompleteDialogue(session);
        // The wait parks; premature/stale scene-wait receipts are inert.
        Assert.False(trigger.AcknowledgeSceneWait(Guid.NewGuid()));
        Assert.Equal(3, trigger.PendingSceneWait!.FramesRemaining);
        Assert.True(session.Update(300));
        Assert.Equal(2, trigger.PendingSceneWait!.FramesRemaining);
        for (uint tick = 400; tick <= 500; tick += 100) Assert.True(session.Update(tick));
        var movement = trigger.PendingMovement!;
        Assert.NotNull(movement);
        Assert.False(trigger.AcknowledgeMovement(movement.Id, false));
        Assert.True(session.Update(600)); // first step only
        // A claimed arrival before the object reaches the target words is rejected.
        Assert.False(trigger.AcknowledgeMovement(movement.Id, true));
        Assert.Equal(TriggerScriptPhase.WaitingForMovement, trigger.Phase);
        Assert.True(session.Update(700)); // snap arrival acknowledges the movement internally
        Assert.Equal(TriggerScriptPhase.Completed, trigger.Phase);
        Assert.False(trigger.AcknowledgeMovement(movement.Id, true)); // stale after completion
    }

    [Fact]
    public void Auto_idle_counters_gate_goto_redirect_and_wait_fallthrough_per_reference()
    {
        // The trigger chain activates the auto object and ends after a one-frame wait, so the
        // wait tick itself is the first auto tick. goto limit 0 executes the jump and continues
        // in the same tick: the West walk step (-4,-2 at speed 2) runs immediately.
        var jump = new DynamicFixture();
        jump.Commands[4] = Command(4, """{"op":"waitUpdate","frames":1,"runTriggers":0,"updateGestures":0}""");
        jump.Commands[5] = Command(5, """{"op":"face","direction":2,"frame":0}""");
        jump.Commands[15] = Command(15, """{"op":"goto","to":"L0016","limit":0}""");
        jump.Commands[16] = Command(16, """{"op":"walkStep","direction":1}""");
        jump.Commands[17] = Command(17, """{"op":"end","mode":"keep"}""");
        var session = NewSession(jump);
        var obj6 = session.World.Objects.Single(o => o.Id == 6);
        StartAndCompleteDialogue(session);
        Assert.True(session.Update(300)); // wait tick: the goto jump + walkStep in one tick
        Assert.Equal((348, 318), (obj6.X, obj6.Y));
        Assert.Equal(17, obj6.AutoEntry);
        Assert.Equal(TriggerScriptPhase.Completed, session.Trigger!.Phase);
        Assert.True(session.Update(400)); // the next due tick completes the trigger call
        Assert.Null(session.Trigger);

        // end redirect limit 2 sends the entry to the target on the first auto tick.
        var redirect = new DynamicFixture();
        redirect.Commands[4] = Command(4, """{"op":"waitUpdate","frames":1,"runTriggers":0,"updateGestures":0}""");
        redirect.Commands[5] = Command(5, """{"op":"face","direction":2,"frame":0}""");
        redirect.Commands[15] = Command(15, """{"op":"end","mode":"redirect","to":"L0017","limit":2}""");
        redirect.Commands[16] = Command(16, """{"op":"walkStep","direction":1}""");
        redirect.Commands[17] = Command(17, """{"op":"end","mode":"keep"}""");
        var second = NewSession(redirect);
        var target = second.World.Objects.Single(o => o.Id == 6);
        StartAndCompleteDialogue(second);
        Assert.True(second.Update(300));
        Assert.Equal(17, target.AutoEntry);
        Assert.Equal(1, target.AutoIdleCount);
        Assert.Equal((352, 320), (target.X, target.Y)); // the redirect itself moves nothing

        // The auto frame wait holds its entry for frames-1 ticks, then falls through and resets.
        var waiting = new DynamicFixture();
        waiting.Commands[4] = Command(4, """{"op":"waitUpdate","frames":1,"runTriggers":0,"updateGestures":0}""");
        waiting.Commands[5] = Command(5, """{"op":"face","direction":2,"frame":0}""");
        waiting.Commands[15] = Command(15, """{"op":"waitUpdate","frames":3,"runTriggers":0,"updateGestures":0}""");
        waiting.Commands[16] = Command(16, """{"op":"walkStep","direction":1}""");
        waiting.Commands[17] = Command(17, """{"op":"end","mode":"keep"}""");
        var third = NewSession(waiting);
        var waiter = third.World.Objects.Single(o => o.Id == 6);
        StartAndCompleteDialogue(third);
        Assert.True(third.Update(300)); // wait tick: auto holds its entry, counts to 1
        Assert.Equal(15, waiter.AutoEntry);
        Assert.True(third.Update(400)); // the completion tick ends the trigger call
        Assert.True(third.Update(500)); // exploring: second count still holds the entry
        Assert.Equal(15, waiter.AutoEntry);
        Assert.True(third.Update(600)); // third count reaches the frames word: fall through
        Assert.Equal(16, waiter.AutoEntry);
        Assert.Equal(0, waiter.AutoIdleCount);
    }

    [Fact]
    public void Touch_objects_and_unsupported_auto_commands_reject_before_any_side_effect()
    {
        var fixture = new DynamicFixture();
        fixture.Commands[15] = Command(15, """{"op":"setDialog","box":"upper","portraitId":0,"color":0,"rngWord":0}""");
        Assert.Throws<InvalidDataException>(() => NewSession(fixture));

        var touching = new DynamicFixture();
        var rules = touching.Rules(touchingObject: true);
        Assert.Throws<InvalidDataException>(() => new DynamicEventSession(rules, DynamicFixture.Leader(),
            touching.Load(), Language(), touching.Load().GetEntry("scene", "npc"), 1, 0, 0));
    }

    private static DynamicEventSession NewSession(DynamicFixture fixture) =>
        new(fixture.Rules(), DynamicFixture.Leader(), fixture.Load(), Language(), fixture.Load().GetEntry("scene", "npc"), 1, 0, 0);

    private static TriggerScriptSession StartAndCompleteDialogue(DynamicEventSession session)
    {
        session.PressSearch();
        Assert.True(session.Update(100));
        var pending = session.PendingTrigger!;
        Assert.NotNull(pending);
        var trigger = session.StartTrigger(pending.Id);
        var display = trigger.PendingDisplay!;
        Assert.NotNull(display);
        display.Display.Update(24, false, true);
        display.Display.Update(100, true, false);
        Assert.True(trigger.AcknowledgeDisplay(display.Id));
        return trigger;
    }

    private static DialogueLanguagePackage Language() =>
        new("en", [new DialogueMessageBlock(61, 61, [DialogueMessagePart.FromText("A")])]);

    private static JsonObject Command(int index, string body)
    {
        var command = JsonNode.Parse(body)!.AsObject();
        command["label"] = $"L{index:D4}";
        // The faithful format pins every next to its WORD-order successor.
        command["next"] = index >= 18 ? null : $"L{index + 1:D4}";
        int opcode = command["op"]!.GetValue<string>() switch
        {
            "end" => command["mode"]!.GetValue<string>() == "keep" ? 0 : 2,
            "goto" => 3, "setDialog" => 60, "waitUpdate" => 9,
            "walkStep" => 11 + (int)command["direction"]!,
            "face" => 15, "walkTo" => 16, "setPartyFacing" => 21, "setPartyPos" => 70,
            "setTriggerMode" => 64, "setState" => 73, "hide" => 82, "setSceneScripts" => 109,
            "setObjectScript" => 144, "markFailed" => 65,
            _ => throw new InvalidOperationException("test command opcode unmapped"),
        };
        command["source"] = JsonNode.Parse($$"""{"index":{{index}},"opcode":{{opcode}},"operands":[0,0,0]}""");
        return command;
    }

    internal sealed class DynamicFixture
    {
        public JsonObject Index { get; } = JsonNode.Parse("""
            {"formatVersion":1,"profile":"faithful","source":{"archive":"SSS.MKF","chunk":4,"byteOrder":"little","instructionCount":19,"byteLength":152,"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},"files":["scene"]}
            """)!.AsObject();
        public Dictionary<string, JsonObject> Files { get; } = new()
        {
            ["scene"] = JsonNode.Parse("""
            {"formatVersion":1,"profile":"faithful","fileId":"scene","entries":[
              {"id":"npc","owner":{"type":"eventObject","id":5},"slot":"wTriggerScript","kind":"trigger","target":"L0001","sourceAddress":1},
              {"id":"walker","owner":{"type":"eventObject","id":6},"slot":"wAutoScript","kind":"auto","target":"L0015","sourceAddress":15}],
            "commands":[
              {"label":"L0000","op":"raw","next":"L0001","source":{"index":0,"opcode":134,"operands":[0,65535,8]}},
              {"label":"L0001","op":"setDialog","box":"upper","portraitId":0,"color":0,"rngWord":0,"next":"L0002","source":{"index":1,"opcode":60,"operands":[0,0,0]}},
              {"label":"L0002","op":"showDialog","messageId":61,"descriptionLineWord":0,"next":"L0003","source":{"index":2,"opcode":65535,"operands":[61,0,0]}},
              {"label":"L0003","op":"setState","objectId":6,"state":1,"next":"L0004","source":{"index":3,"opcode":73,"operands":[6,1,0]}},
              {"label":"L0004","op":"waitUpdate","frames":3,"runTriggers":0,"updateGestures":0,"next":"L0005","source":{"index":4,"opcode":9,"operands":[3,0,0]}},
              {"label":"L0005","op":"walkTo","x":9,"y":21,"h":0,"next":"L0006","source":{"index":5,"opcode":16,"operands":[9,21,0]}},
              {"label":"L0006","op":"face","direction":0,"frame":0,"next":"L0007","source":{"index":6,"opcode":15,"operands":[0,0,0]}},
              {"label":"L0007","op":"face","direction":0,"frame":0,"next":"L0008","source":{"index":7,"opcode":15,"operands":[0,0,0]}},
              {"label":"L0008","op":"face","direction":0,"frame":0,"next":"L0009","source":{"index":8,"opcode":15,"operands":[0,0,0]}},
              {"label":"L0009","op":"end","mode":"keep","next":"L0010","source":{"index":9,"opcode":0,"operands":[0,0,0]}},
              {"label":"L0010","op":"raw","next":"L0011","source":{"index":10,"opcode":139,"operands":[0,0,0]}},
              {"label":"L0011","op":"raw","next":"L0012","source":{"index":11,"opcode":140,"operands":[0,0,0]}},
              {"label":"L0012","op":"raw","next":"L0013","source":{"index":12,"opcode":141,"operands":[0,0,0]}},
              {"label":"L0013","op":"raw","next":"L0014","source":{"index":13,"opcode":142,"operands":[0,0,0]}},
              {"label":"L0014","op":"raw","next":"L0015","source":{"index":14,"opcode":143,"operands":[0,0,0]}},
              {"label":"L0015","op":"face","direction":1,"frame":0,"next":"L0016","source":{"index":15,"opcode":15,"operands":[1,0,0]}},
              {"label":"L0016","op":"walkTo","x":9,"y":18,"h":0,"next":"L0017","source":{"index":16,"opcode":16,"operands":[9,18,0]}},
              {"label":"L0017","op":"hide","frames":0,"next":"L0018","source":{"index":17,"opcode":82,"operands":[0,0,0]}},
              {"label":"L0018","op":"end","mode":"keep","next":null,"source":{"index":18,"opcode":0,"operands":[0,0,0]}}]}
            """)!.AsObject()
        };
        public JsonArray Commands => Files["scene"]["commands"]!.AsArray();
        public TriggerEventCollection Load() => TriggerEventCollection.Parse(Index.ToJsonString(), Files.ToDictionary(p => p.Key, p => p.Value.ToJsonString()));

        private static readonly SceneSprite ObjectSprite = new(42, Enumerable.Range(0, 12).Select(i => new SceneFrame(i, 16, 24)));

        public StaticSceneRules Rules(bool touchingObject = false) => new(
            new SceneMap(1, new uint[16384], [new SceneFrame(0, 32, 15)]),
        [
            new SceneObject(5, 304, 328, 2, 0, 0, 42, 3, 0, 0, ObjectSprite) { TriggerMode = touchingObject ? (ushort)6 : (ushort)1, TriggerEntry = 1 },
            new SceneObject(6, 352, 320, 0, 0, 0, 42, 3, 0, 0, ObjectSprite) { TriggerMode = 0, AutoEntry = 15 },
        ]);

        internal static StaticLeader Leader() => new(320, 320, 0, 0, 0, 3,
            new SceneSprite(41, Enumerable.Range(0, 16).Select(i => new SceneFrame(i, 20, 30))));
    }
}
