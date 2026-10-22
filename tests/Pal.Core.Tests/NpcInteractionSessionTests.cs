using Pal.Core;
using Xunit;

namespace Pal.Core.Tests;

public class NpcInteractionSessionTests
{
    [Fact]
    public void Search_requests_selected_original_entry_and_global_caller_with_real_standing_draw_plan()
    {
        var obj = Target() with { CurrentFrame = 1, Direction = 1 };
        var raw = SceneSearchTests.Rules([obj]);
        var session = Create(raw, SceneSearchTests.Leader(1232, 1336));
        session.PressSearch();
        Assert.True(session.Update(0));
        var request = Assert.IsType<NpcInteractionRequest>(session.PendingTrigger);
        Assert.Equal(("shared", "npc", 60, 0x0188, 1),
            (request.FileId, request.EntryId, request.Caller, request.SourceAddress, request.SampleIndex));
        Assert.Equal(NpcInteractionPhase.Trigger, session.Phase);
        Assert.True(session.BlocksSceneUpdates);
        Assert.False(session.Current.IsWalking);
        Assert.Equal(0, session.Current.Plan.LeaderFrameIndex);
        Assert.Equal(6, Assert.Single(session.Current.Plan.Commands, c => c.ObjectId == 60).FrameIndex);
        Assert.Equal(obj with { CurrentFrame = 0, Direction = 2 }, Assert.Single(session.Objects));
        Assert.Equal(obj, Assert.Single(raw.Objects));
    }

    [Fact]
    public void Only_real_completed_trigger_receipt_returns_saved_entry_and_begins_blocked_return()
    {
        var events = TriggerScriptSessionTests.RealShape().Load();
        var trigger = new TriggerScriptSession(events, TriggerScriptSessionTests.Language());
        var session = Create();
        session.PressSearch();
        session.Update(0);
        var request = session.PendingTrigger!;
        Assert.False(session.CompleteTrigger(request.Id, trigger, 1));
        trigger.Start(request.FileId, request.EntryId, request.Caller);
        Assert.Equal(0x018B, trigger.CurrentPosition!.SourceAddress);
        Assert.Equal(0x0188, trigger.ReturnEntry);
        Assert.False(session.CompleteTrigger(request.Id, trigger, 1));
        var display = trigger.PendingDisplay!;
        display.Display.Update(0, true, false);
        Assert.False(session.CompleteTrigger(request.Id, trigger, 1));
        display.Display.Update(0, true, false);
        Assert.True(trigger.AcknowledgeDisplay(display.Id));
        Assert.False(session.CompleteTrigger(Guid.NewGuid(), trigger, 1));
        Assert.True(session.CompleteTrigger(request.Id, trigger, 100));
        Assert.Equal(NpcInteractionPhase.Returning, session.Phase);
        Assert.True(session.BlocksSceneUpdates);
        Assert.Null(session.PendingTrigger);
        Assert.Equal(0x0188, session.Objects.Single(o => o.Id == 60).TriggerEntry);
        Assert.False(session.CompleteTrigger(request.Id, trigger, 120));
        Assert.False(session.Update(149));
    }

    [Theory]
    [InlineData(100u, 149u, 150u, 151u, 249u, 250u)]
    [InlineData(4294967275u, 28u, 29u, 30u, 128u, 129u)]
    [InlineData(100u, 149u, 900u, 901u, 999u, 1000u)]
    public void Return_waits_50ms_wraps_and_late_update_moves_only_once_then_reschedules_without_clearing_held_aliases(
        uint completed, uint early, uint due, uint after, uint beforeNext, uint next)
    {
        var session = Create();
        var trigger = new TriggerScriptSession(TriggerScriptSessionTests.RealShape().Load(), TriggerScriptSessionTests.Language());
        session.PressSearch();
        session.Update(unchecked(completed - 100));
        var request = session.PendingTrigger!;
        var standing = session.Current;
        session.Press(ExplorationKey.EastPrimary);
        session.Press(ExplorationKey.EastAlternate);
        session.Press(ExplorationKey.NorthPrimary, repeat: true);
        session.PressSearch(); // Also covers a delivered dialogue-ending search key.
        Assert.False(session.Update(unchecked(completed - 1)));
        Assert.Same(standing, session.Current);
        trigger.Start(request.FileId, request.EntryId, request.Caller);
        Finish(trigger);
        Assert.True(session.CompleteTrigger(request.Id, trigger, completed));
        session.Release(ExplorationKey.EastPrimary); // Alternate still held.
        Assert.False(session.Update(early));
        Assert.Same(standing, session.Current);
        session.PressSearch(); // Inputs polled during UTIL_Delay(50) must also be discarded.
        Assert.True(session.Update(due));
        Assert.False(session.BlocksSceneUpdates);
        Assert.Equal(NpcInteractionPhase.Exploring, session.Phase);
        Assert.Null(session.PendingTrigger);
        // The initial idle exploration step toggled phase 0 -> 2 before PAL_Search.
        Assert.Equal((1248, 1344, 3, 3, 11), ((int)session.Current.Leader.X, (int)session.Current.Leader.Y,
            session.Current.Leader.Direction, session.Current.Leader.StepPhase, session.Current.Plan.LeaderFrameIndex));
        Assert.False(session.Update(after));
        Assert.False(session.Update(beforeNext));
        Assert.True(session.Update(next));
        Assert.Equal((1264, 1352, 0), ((int)session.Current.Leader.X, (int)session.Current.Leader.Y, session.Current.Leader.StepPhase));
        session.Release(ExplorationKey.EastAlternate);
        Assert.True(session.Update(unchecked(next + 100)));
        Assert.False(session.Current.IsWalking);
        Assert.Equal((1264, 1352), ((int)session.Current.Leader.X, (int)session.Current.Leader.Y));
    }

    [Theory]
    [InlineData(0, 304, 248, 2, 6, 0)]
    [InlineData(1, 304, 232, 3, 9, 3)]
    [InlineData(2, 336, 232, 0, 0, 6)]
    [InlineData(3, 336, 248, 1, 3, 9)]
    public void Search_follows_actual_exploration_step_and_faces_both_actors_with_reference_party_stride_three(
        int direction, ushort x, ushort y, int objectDirection, int objectFrame, int partyFrame)
    {
        var obj = Target() with { X = x, Y = y, State = 1, CurrentFrame = 1 };
        var rules = SceneSearchTests.Rules([obj]);
        var session = Create(rules, SceneSearchTests.Leader() with { WalkFramesWord = 4 });
        session.Press((ExplorationKey)(direction * 2));
        session.PressSearch();
        Assert.True(session.Update(0));
        Assert.Equal(0, session.PendingTrigger!.SampleIndex);
        Assert.Equal((x, y, direction, 1), ((ushort)session.Current.Leader.X, (ushort)session.Current.Leader.Y,
            session.Current.Leader.Direction, session.Current.Leader.StepPhase));
        Assert.Equal(partyFrame, session.Current.Plan.LeaderFrameIndex);
        Assert.False(session.Current.IsWalking);
        Assert.Equal(objectDirection, session.Objects.Single().Direction);
        Assert.Equal(objectFrame, Assert.Single(session.Current.Plan.Commands, c => c.ObjectId == 60).FrameIndex);
        Assert.Equal(obj, rules.Objects.Single());
    }

    [Theory]
    [InlineData(11, true, 3, 9)]
    [InlineData(12, false, 12, 12)]
    public void Facing_and_party_standing_only_happen_below_sprite_frames_times_four(
        ushort currentFrame, bool faces, int drawObjectFrame, int partyFrame)
    {
        var obj = Target() with { X = 1232, Y = 1336, Direction = 0, CurrentFrame = currentFrame };
        var session = Create(SceneSearchTests.Rules([obj]), SceneSearchTests.Leader(1232, 1336, 3) with { WalkFramesWord = 4 });
        session.PressSearch();
        session.Update(0);
        Assert.NotNull(session.PendingTrigger);
        Assert.Equal(faces ? 1 : 0, session.Objects.Single().Direction);
        Assert.Equal(faces ? 0 : 12, session.Objects.Single().CurrentFrame);
        Assert.Equal(drawObjectFrame, Assert.Single(session.Current.Plan.Commands, c => c.ObjectId == 60).FrameIndex);
        Assert.Equal(partyFrame, session.Current.Plan.LeaderFrameIndex);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void Real_trigger_message61_span1_repeats_from0188_regardless_of_translation_line_count(int lines)
    {
        var events = TriggerScriptSessionTests.RealShape().Load();
        var language = new DialogueLanguagePackage(lines == 1 ? "zh-Hans" : "en", [new DialogueMessageBlock(61, 62,
            Enumerable.Range(0, lines).Select(_ => DialogueMessagePart.FromText("AB")).ToArray())]);
        var trigger = new TriggerScriptSession(events, language);
        var session = Create();
        Guid previous = Guid.Empty;
        for (uint start = 0; start <= 250; start += 250)
        {
            session.PressSearch();
            Assert.True(session.Update(start));
            var request = session.PendingTrigger!;
            Assert.NotEqual(previous, request.Id);
            previous = request.Id;
            trigger.Start(request.FileId, request.EntryId, request.Caller);
            Assert.Equal(60, trigger.Caller);
            Assert.Equal(0x0188, trigger.ReturnEntry);
            Assert.Equal(0x018B, trigger.CurrentPosition!.SourceAddress);
            var display = trigger.PendingDisplay!;
            Assert.Equal((61, 1), (display.MessageReferences.Single().StartMessageId, display.MessageReferences.Single().Span));
            Assert.Equal(lines, display.Content.Parts.Count);
            Assert.Empty(display.Display.Current.VisibleText);
            Assert.Equal((59, 0x4F, 24), (display.Context.PortraitId, display.Context.Color, display.Context.DelayMilliseconds));
            Finish(trigger);
            Assert.Equal((0, 0x4F, false), (trigger.Context.PortraitId, trigger.Context.Color, trigger.Context.PlayingRng));
            session.PressSearch(); // End confirmation must not leak into a new call.
            Assert.True(session.CompleteTrigger(request.Id, trigger, start + 100));
            Assert.False(session.Update(start + 149));
            Assert.True(session.Update(start + 150));
            Assert.Null(session.PendingTrigger);
            Assert.Equal(0x0188, session.Objects.Single().TriggerEntry);
        }
    }

    [Fact]
    public void Unsupported_first_search_hit_is_rejected_not_skipped_while_all32_objects_and_collision_remain()
    {
        var objects = Enumerable.Range(45, 32).Select(id => SceneSearchTests.Object((ushort)id, 1000, 1000) with { TriggerMode = 0 }).ToArray();
        objects[60 - 45] = Target();
        objects[45 - 45] = SceneSearchTests.Object(45, 1232, 1336);
        var rules = SceneSearchTests.Rules(objects);
        var session = Create(rules);
        session.PressSearch();
        var error = Assert.Throws<InvalidDataException>(() => session.Update(0));
        Assert.Contains("owner 45 at sample 0", error.Message);
        Assert.Null(session.PendingTrigger);
        Assert.Equal(NpcInteractionPhase.Exploring, session.Phase);
        Assert.Equal(objects, session.Objects);
        Assert.Equal(32, session.Objects.Count);
        Assert.True(rules.IsBlocked(1216, 1344));
    }

    [Fact]
    public void Search_press_is_retained_until_due_and_consumed_once_on_a_miss()
    {
        var session = Create(leader: SceneSearchTests.Leader()); // Owner60 is far away.
        session.Update(0);
        session.PressSearch();
        Assert.False(session.Update(99));
        Assert.True(session.Update(100));
        Assert.Null(session.PendingTrigger);
        Assert.True(session.Update(200));
        Assert.Null(session.PendingTrigger);
        Assert.False(session.BlocksSceneUpdates);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("address")]
    [InlineData("kind")]
    [InlineData("slot")]
    [InlineData("runtime")]
    public void Independent_binding_rejects_other_owners_addresses_kinds_slots_or_runtime_entry(string fault)
    {
        var entry = TriggerScriptSessionTests.RealShape().Load().GetEntry("shared", "npc");
        entry = fault switch
        {
            "owner" => entry with { OwnerId = 61 }, "address" => entry with { SourceAddress = 0x0189 },
            "kind" => entry with { Kind = "auto" }, "slot" => entry with { Slot = "wAutoScript" }, _ => entry
        };
        var obj = fault == "runtime" ? Target() with { TriggerEntry = 0x0189 } : Target();
        Assert.Throws<InvalidDataException>(() => new NpcInteractionSession(SceneSearchTests.Rules([obj]),
            SceneSearchTests.Leader(1232, 1336), entry));
    }

    [Fact]
    public void Completed_wrong_caller_or_wrong_entry_cannot_release_pending_search()
    {
        var session = Create();
        session.PressSearch(); session.Update(0);
        var pending = session.PendingTrigger!;
        var trigger = new TriggerScriptSession(TriggerScriptSessionTests.RealShape().Load(), TriggerScriptSessionTests.Language());
        trigger.Start("shared", "npc", 61); Finish(trigger);
        Assert.False(session.CompleteTrigger(pending.Id, trigger, 100));
        trigger = new TriggerScriptSession(new EventFixture().Load(), TriggerScriptSessionTests.Language());
        trigger.Start("shared", "npc", 60); Finish(trigger);
        Assert.False(session.CompleteTrigger(pending.Id, trigger, 100));
        Assert.Same(pending, session.PendingTrigger);
        Assert.True(session.BlocksSceneUpdates);
    }

    [Fact]
    public void All32_original_objects_remain_in_runtime_draw_and_collision_after_interaction()
    {
        var objects = Enumerable.Range(45, 32).Select(id => SceneSearchTests.Object((ushort)id, 1000, 1000) with { TriggerMode = 0 }).ToArray();
        objects[60 - 45] = Target() with { CurrentFrame = 1 };
        objects[57 - 45] = Target() with { Id = 57, X = 1264, Y = 1352, TriggerMode = 6, CurrentFrame = 1, Direction = 3 };
        var rules = SceneSearchTests.Rules(objects);
        var session = Create(rules);
        var original57 = Assert.Single(session.Current.Plan.Commands, c => c.ObjectId == 57);
        session.PressSearch(); session.Update(0);
        Assert.Equal(32, session.Objects.Count);
        Assert.Equal(original57, Assert.Single(session.Current.Plan.Commands, c => c.ObjectId == 57));
        Assert.Equal(objects.Where(o => o.Id != 60), session.Objects.Where(o => o.Id != 60));
        Assert.Equal(objects, rules.Objects);
        var trigger = new TriggerScriptSession(TriggerScriptSessionTests.RealShape().Load(), TriggerScriptSessionTests.Language());
        var request = session.PendingTrigger!;
        trigger.Start(request.FileId, request.EntryId, request.Caller); Finish(trigger);
        session.CompleteTrigger(request.Id, trigger, 100);
        session.Press(ExplorationKey.EastPrimary);
        Assert.True(session.Update(150));
        Assert.Equal((1248, 1344), ((int)session.Current.Leader.X, (int)session.Current.Leader.Y));
        Assert.True(session.Update(250));
        Assert.Equal((1248, 1344), ((int)session.Current.Leader.X, (int)session.Current.Leader.Y));
        Assert.False(session.Current.IsWalking); // Non-selected owner57 still blocks the next step.
        Assert.Null(session.PendingTrigger); // Its touch script is not scheduled in this independent context.
    }

    [Fact]
    public void Sprite_frames_zero_does_not_force_a_party_standing_redraw()
    {
        var obj = SceneSearchTests.Object(60, 336, 248) with { State = 1 };
        var session = Create(SceneSearchTests.Rules([obj]), SceneSearchTests.Leader(direction: 3));
        session.Press(ExplorationKey.EastPrimary);
        session.PressSearch(); session.Update(0);
        Assert.NotNull(session.PendingTrigger);
        Assert.True(session.Current.IsWalking);
        Assert.Equal(10, session.Current.Plan.LeaderFrameIndex);
        Assert.Equal(obj, session.Objects.Single());
    }

    [Fact]
    public void Search_queued_before_deadline_waits_for_actual_next_exploration_step()
    {
        var session = Create();
        session.Update(0);
        session.PressSearch();
        Assert.False(session.Update(99));
        Assert.Null(session.PendingTrigger);
        Assert.True(session.Update(100));
        Assert.Equal((60, 1), (session.PendingTrigger!.Caller, session.PendingTrigger.SampleIndex));
    }

    [Fact]
    public void Host_selects_entry_metadata_from_the_same_initialized_trigger_without_loading_or_running_another_executor()
    {
        var trigger = new TriggerScriptSession(TriggerScriptSessionTests.RealShape().Load(), TriggerScriptSessionTests.Language());
        var selected = Assert.Single(trigger.Entries, e => e.OwnerType == "eventObject" && e.OwnerId == 60 && e.Slot == "wTriggerScript");
        var session = new NpcInteractionSession(SceneSearchTests.Rules([Target()]), SceneSearchTests.Leader(1232, 1336), selected);
        Assert.Equal(TriggerScriptPhase.Ready, trigger.Phase);
        session.PressSearch(); session.Update(0);
        var request = session.PendingTrigger!;
        Assert.Equal(("shared", "npc", 0x0188), (request.FileId, request.EntryId, request.SourceAddress));
        trigger.Start(request.FileId, request.EntryId, request.Caller);
        Assert.Equal(TriggerScriptPhase.WaitingForDisplay, trigger.Phase);
        Assert.Equal(60, trigger.Caller);
    }

    internal static void Finish(TriggerScriptSession trigger)
    {
        for (int i = 0; i < 10 && trigger.PendingDisplay is { } request; i++)
        {
            request.Display.Update(0, true, false);
            if (request.Display.Current.Phase == DialogueDisplayPhase.Completed)
                Assert.True(trigger.AcknowledgeDisplay(request.Id));
        }
        Assert.Equal(TriggerScriptPhase.Completed, trigger.Phase);
    }

    internal static SceneObject Target() => SceneSearchTests.Object(60, 1216, 1344) with
    {
        SpriteNumber = 207, SpriteFrames = 3,
        Sprite = new SceneSprite(207, Enumerable.Range(0, 16).Select(i => new SceneFrame(i, 20, 30)))
    };
    internal static NpcInteractionSession Create(StaticSceneRules? rules = null, StaticLeader? leader = null) =>
        new(rules ?? SceneSearchTests.Rules([Target()]), leader ?? SceneSearchTests.Leader(1232, 1336),
            TriggerScriptSessionTests.RealShape().Load().GetEntry("shared", "npc"));
}
