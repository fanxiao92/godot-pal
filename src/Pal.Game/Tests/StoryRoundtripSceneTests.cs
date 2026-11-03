using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using Pal.Core;
using Pal.Game.Dialogue;
using static GdUnit4.Assertions;

namespace Pal.Game.Tests;

[TestSuite, RequireGodotRuntime]
public class StoryRoundtripSceneTests
{
    [TestCase]
    public void FloatingNumbersUseUnsignedWallAgeRightAlignedBlueDigitsAndWrap()
    {
        foreach (uint start in new[] { 100u, uint.MaxValue - 19 })
        {
            var numbers = new StoryBattleNumbers();
            numbers.EnqueueAction(43, new(1, ClassicBattleSide.Player, 7, 297, true, false), new(100,10), start, true);
            AssertThat(numbers.At(start).Count).IsEqual(0);
            AssertThat(numbers.LabelAt(start) is null).IsTrue();
            numbers.RevealPending();
            foreach (var (age, y) in new[] { (0u,10), (39u,10), (40u,9), (439u,0) })
            {
                var digits = numbers.At(unchecked(start + age));
                AssertThat(digits.Count).IsEqual(3); // overkill297, never clamped43
                AssertThat(digits.Select(d => d.Digit).SequenceEqual(new[] { 7,9,2 })).IsTrue();
                AssertThat(digits.Select(d => d.Position).SequenceEqual(new[] { new Vector2(100,y),new Vector2(94,y),new Vector2(88,y) })).IsTrue();
                AssertThat(digits.All(d => d.Color == StoryNumberColor.Blue && d.BornAt == start)).IsTrue();
                AssertThat(numbers.LabelAt(unchecked(start + age)) == 60003).IsTrue();
            }
            AssertThat(numbers.At(unchecked(start + 440)).Count).IsEqual(0);
            AssertThat(numbers.LabelAt(unchecked(start + 440)) is null).IsTrue();
            var late = new StoryBattleNumbers();
            late.Enqueue(1, new(10,10), StoryNumberColor.Blue, start);
            AssertThat(late.At(unchecked(start + 900)).Count).IsEqual(0);
        }
    }

    [TestCase]
    public void FloatingNumberSlotsUseFirstEmptySixPixelFiveDigitLayoutZeroEvadeAndClear()
    {
        var numbers = new StoryBattleNumbers();
        AssertThat(numbers.Enqueue(0, Vector2.Zero, StoryNumberColor.Blue, 0)).IsFalse();
        numbers.EnqueueAction(20, new(1, ClassicBattleSide.Enemy, 0, 0, false, true), new(240,95), 0, false);
        AssertThat(numbers.At(0).Count).IsEqual(0);
        AssertThat(numbers.LabelAt(0) == 60002).IsTrue();
        numbers.BeginAction(); AssertThat(numbers.LabelAt(0) is null).IsTrue();
        for (int i = 0; i < 16; i++) AssertThat(numbers.Enqueue(65535, new(100,10), StoryNumberColor.Cyan, i == 0 ? 0u : 40u)).IsTrue();
        AssertThat(numbers.Enqueue(1, Vector2.Zero, StoryNumberColor.Blue, 40)).IsFalse();
        var digits = numbers.At(40);
        AssertThat(digits.Count).IsEqual(80);
        AssertThat(digits.Take(5).Select(d => d.Digit).SequenceEqual(new[] { 5,3,5,5,6 })).IsTrue();
        AssertThat(digits.Take(5).Select(d => d.Position.X).SequenceEqual(new[] { 109f,103f,97f,91f,85f })).IsTrue();
        AssertThat(numbers.At(440).Count).IsEqual(75);
        AssertThat(numbers.Enqueue(1, new(100,10), StoryNumberColor.Yellow, 440)).IsTrue();
        AssertThat(numbers.At(440)[0].Slot).IsEqual(0);
        numbers.Clear(); AssertThat(numbers.At(440).Count).IsEqual(0); AssertThat(numbers.LabelAt(440) is null).IsTrue();
    }

    [TestCase]
    public void PlayerPhysicalLiteralFramesEffectsRecoilAndHitOnlyHp()
    {
        var sequence = new StoryPlayerAttackPresentation(new(240,170), new(100,120), 80, 0, 1);
        int[] frames = [7,7,7,7,8,8,8,9,9,9,9,9,9,1,1,1];
        Vector2[] positions = [new(240,170),new(240,170),new(240,170),new(240,170),
            new(164,140),new(164,140),new(154,138),new(154,138),new(154,138),new(156,139),
            new(156,139),new(156,139),new(156,139),new(240,170),new(240,170),new(240,170)];
        Vector2[] enemyPositions = [new(92,116),new(96,118),new(94,117)];
        Vector2[] fxPositions = [new(100,122),new(84,138),new(68,154)];
        for (int tick = 0; tick < 16; tick++)
        {
            var shown = sequence.At(tick, 25);
            AssertThat(shown.PlayerFrame).IsEqual(frames[tick]);
            AssertThat(shown.PlayerPosition == positions[tick]).IsTrue();
            AssertThat(shown.DisplayedEnemyHp).IsEqual(tick < 7 ? 80 : 0);
            AssertThat(shown.ResultVisible == (tick >= 7)).IsTrue();
            AssertThat(shown.EnemyColorShift).IsEqual(tick is 8 or 9 ? 6 : 0);
            AssertThat(shown.EnemyPosition == (tick is >= 10 and <= 12 ? enemyPositions[tick-10] : new Vector2(100,120))).IsTrue();
            AssertThat(shown.EffectFrame == (tick is >= 7 and <= 9 ? tick-7 : (int?)null)).IsTrue();
            if (tick is >= 7 and <= 9) AssertThat(shown.EffectPosition == fxPositions[tick-7]).IsTrue();
        }
    }

    [TestCase]
    public void EnemyIdleWordCounterCyclesPersistsAcrossActionsAndDoesNotCoerceZero()
    {
        var idle = new StoryEnemyIdleState(3, 2);
        // Startup UpdateFighters, immediate first outer; then four TRUE windup ticks.
        int[] expectedFrames = [0,1,1,2,2,0];
        int[] expectedCounters = [1,2,1,2,1,2];
        for (int i = 0; i < 6; i++)
        {
            idle.Advance();
            AssertThat(idle.Frame).IsEqual(expectedFrames[i]);
            AssertThat((int)idle.Counter).IsEqual(expectedCounters[i]);
        }
        // FALSE active gestures assign but do not reset WORD; next TRUE tail clamps active frame.
        idle.SetFrame(5);
        AssertThat((int)idle.Counter).IsEqual(2);
        idle.Advance(); AssertThat(idle.Frame).IsEqual(0); AssertThat((int)idle.Counter).IsEqual(1);
        idle.Advance(); AssertThat(idle.Frame).IsEqual(1); AssertThat((int)idle.Counter).IsEqual(2);
        idle.Advance(); AssertThat(idle.Frame).IsEqual(1); AssertThat((int)idle.Counter).IsEqual(1);
        var zero = new StoryEnemyIdleState(3, 0);
        zero.Advance(); AssertThat((int)zero.Counter).IsEqual(65535); AssertThat(zero.Frame).IsEqual(0);
        zero.SetFrame(2); zero.Advance(); AssertThat((int)zero.Counter).IsEqual(65534); AssertThat(zero.Frame).IsEqual(2);
        var fresh = new StoryEnemyIdleState(3, 2);
        AssertThat(fresh.Frame).IsEqual(0); AssertThat((int)fresh.Counter).IsEqual(2);
    }

    [TestCase("effect")]
    [TestCase("idle")]
    [TestCase("number")]
    public void BaseFormatWithoutSelectedPresentationFailsBeforePartialMain(string missing)
    {
        using var fixture = new AuthoredStoryFixture("Win", missing);
        var story = Pal.Game.Profiles.NativeStoryProfile.Load(fixture.StoryUri);
        var visual = Pal.Game.Profiles.NativeBattleVisualProfile.Load(fixture.VisualUri, story);
        AssertThat(visual.Teams.Count).IsEqual(1); // Format remains legal independently of selected capability.
        using var host = new StoryHost(GameSceneConfiguration.FromArguments("--story-profile", fixture.StoryUri,
            "--battle-visual-profile", fixture.VisualUri, "--battle-seed", "1"));
        AssertThat(host.Main.Failure!.Contains(missing == "effect" ? "attack effect" : missing == "number" ? "NumberSpriteUri" : "IdleAnimSpeed")).IsTrue();
        AssertThat(host.Main.GetChildCount()).IsEqual(0);
        AssertThat(host.Main.Dialogue).IsNull(); AssertThat(host.Main.StoryBattle).IsNull();
    }

    [TestCase(Timeout = 20000)]
    public async Task PlayerPhysicalStartsWithReferenceWindupBeforeImpact()
    {
        using var fixture = new AuthoredStoryFixture("Win");
        using var host = new StoryHost(GameSceneConfiguration.FromArguments("--story-profile", fixture.StoryUri,
            "--battle-visual-profile", fixture.VisualUri, "--battle-seed", "1"));
        host.Tap(Key.Enter);
        await host.Wait(() => host.Main.Dialogue!.Session!.PendingDisplay is not null);
        await host.FinishDisplay();
        await host.Wait(() => host.Main.StoryBattleMode == StoryBattlePresentation.AttackMenu);
        await host.ConfirmMenu(StoryBattlePresentation.TargetMenu);
        await host.ConfirmMenu(StoryBattlePresentation.Resolving);
        await host.Wait(() => host.Main.StoryBattleMode == StoryBattlePresentation.Feedback);
        AssertThat(host.Actions[0].Attacker).IsEqual(ClassicBattleSide.Player);
        AssertThat(host.Main.StoryDrawSnapshot!.Value.PlayerFrame).IsEqual(7);
        AssertThat(host.Main.StoryDisplayedEnemyHp == 43).IsTrue();
        AssertThat(host.Main.StoryBattle!.Current.Combat.EnemyHp < 43).IsTrue();
        AssertThat(host.Main.StoryResultVisible).IsFalse();
        await host.Wait(() => host.Main.StoryBattlePose is { Pose: 8 });
        var numbers = host.Main.GetChildren().OfType<PalStoryBattleView>().Single().NumberDigits;
        AssertThat(numbers.Count > 0).IsTrue(); // FX0 enqueues after UI; FX1 is first eligible draw.
    }

    [TestCase(Timeout = 20000)]
    public async Task NonterminalActionsKeepIdleCounterAndConsumePendingOuterOnNextMainCallback()
    {
        // fight.c157–170,279–284,3632–3666: base42 +2, critical3, role0 bonus2,
        // float<=1.125 gives <=297 per single hit. Two hits cannot exhaust1000.
        // Enemy strength36..38 vs defense20, resistance2 gives <=23; player HP100 survives.
        // 1000 also satisfies selected Classic current-HP capability1..32767 (not just WORD format).
        using var fixture = new AuthoredStoryFixture("Win", enemyHp: 1000);
        Main old;
        using (var host = new StoryHost(GameSceneConfiguration.FromArguments("--story-profile", fixture.StoryUri,
            "--battle-visual-profile", fixture.VisualUri, "--story-language", "zh-Hans", "--battle-seed", "1")))
        {
            old = host.Main;
            AssertThat(old.Failure).IsNull();
            host.Tap(Key.Enter);
            await host.Wait(() => old.Dialogue!.Session!.PendingDisplay is not null);
            var script = old.Dialogue!.Session!;
            Guid call = script.CallId;
            Guid origin = old.Interaction!.PendingTrigger!.Id;
            AssertThat(script.PendingDisplay!.Content.Parts[0].Text!).IsEqual("战");
            await host.FinishDisplay();
            await host.Wait(() => old.StoryBattleMode == StoryBattlePresentation.AttackMenu);
            var pending = script.PendingBattle!;
            var battle = old.StoryBattle!;
            AssertThat(ReferenceEquals(pending.Lifecycle, battle)).IsTrue();
            await host.ConfirmMenu(StoryBattlePresentation.TargetMenu);
            await host.ConfirmMenu(StoryBattlePresentation.Resolving);
            await host.Wait(() => host.Actions.Count == 1);
            AssertThat(battle.Current.Combat.PlayerHp > 0 && battle.Current.Combat.EnemyHp > 0).IsTrue();
            AssertThat(battle.Completion).IsNull();
            AssertThat(script.Phase).IsEqual(TriggerScriptPhase.WaitingForBattle);
            await host.Wait(() => host.PendingOuterCallbacks.Count >= 2);
            AssertThat(host.NonterminalReturns.Count).IsEqual(2);
            AssertThat(host.PendingOuterCallbacks.Count).IsEqual(2);
            for (int i = 0; i < 2; i++)
            {
                var ended = host.NonterminalReturns[i];
                var next = host.PendingOuterCallbacks[i];
                var started = host.ActionStarts[i];
                AssertThat(ended.Update + 1).IsEqual(next.Update);
                AssertThat(ended.FinalPose).IsEqual(15); // 16th transition ends each authored action.
                AssertThat(ReferenceEquals(ended.Action, started.Action)).IsTrue();
                // Player: remaining15 TRUE + UpdateFighters =16. Enemy: five TRUE + final =6.
                // Both even, so literal speed2 counter must survive unchanged from first shown pose.
                AssertThat(ended.Counter).IsEqual(started.Counter);
                AssertThat(next.Counter).IsEqual(ended.Counter == 2 ? 1 : 2);
                AssertThat(ended.Frame).IsEqual(0);
                AssertThat(next.Frame).IsEqual(i == 0 ? 1 : 0); // First next is active magic pose; second is configured idle.
                GD.Print($"PAL_CROSS_ACTION:{i+1};endCallback={ended.Update};nextCallback={next.Update};"
                    + $"gapMs={unchecked(next.Now-ended.Now)};startCounter={started.Counter};endCounter={ended.Counter};nextCounter={next.Counter};finalPose={ended.FinalPose};endFrame={ended.Frame};nextFrame={next.Frame}");
            }
            AssertThat(host.NonterminalReturns[0].Mode == StoryBattlePresentation.Resolving).IsTrue();
            AssertThat(host.PendingOuterCallbacks[0].Mode == StoryBattlePresentation.Feedback).IsTrue();
            AssertThat(host.NonterminalReturns[1].Mode == StoryBattlePresentation.AttackMenu).IsTrue();
            AssertThat(host.PendingOuterCallbacks[1].Mode == StoryBattlePresentation.AttackMenu).IsTrue();
            AssertThat(host.NonterminalReturns[0].Counter != host.NonterminalReturns[1].Counter).IsTrue();
            await host.ConfirmMenu(StoryBattlePresentation.TargetMenu);
            await host.ConfirmMenu(StoryBattlePresentation.Resolving);
            await host.Wait(() => host.Actions.Count == 3);
            AssertThat(host.Actions.Count).IsEqual(3);
            AssertThat(host.ActionStarts.Count).IsEqual(3);
            AssertThat(host.NumberBirths).IsEqual(2);
            AssertThat(host.NumberDraws > 0).IsTrue();
            AssertThat(host.Actions[0].Attacker).IsEqual(ClassicBattleSide.Player);
            AssertThat(host.Actions[1].Attacker).IsEqual(ClassicBattleSide.Enemy);
            AssertThat(host.Actions[2].Attacker).IsEqual(ClassicBattleSide.Player);
            AssertThat(host.Actions[0].Round).IsEqual(1);
            AssertThat(host.Actions[1].Round).IsEqual(1);
            AssertThat(host.Actions[2].Round).IsEqual(2);
            AssertThat(ReferenceEquals(host.Actions[0], host.Actions[1]) || ReferenceEquals(host.Actions[1], host.Actions[2])).IsFalse();
            AssertThat(battle.Current.Combat.PlayerHp > 0 && battle.Current.Combat.EnemyHp > 0).IsTrue();
            AssertThat(battle.Completion).IsNull();
            AssertThat(script.CallId).IsEqual(call);
            AssertThat(script.SourceRequestId == origin).IsTrue();
            AssertThat(ReferenceEquals(script.PendingBattle, pending) && ReferenceEquals(old.StoryBattle, battle)).IsTrue();
            AssertThat(script.Phase).IsEqual(TriggerScriptPhase.WaitingForBattle);
            AssertThat(script.PendingDisplay).IsNull();
            var overlay = old.GetChildren().OfType<PalStoryBattleView>().Single();
            AssertThat(old.GetChildren().OfType<Pal.Game.Resources.PalMapScene>().Single().Visible).IsFalse();
            var adapter = old.Dialogue!;
            old.GetParent().RemoveChild(old); // Real exit during third feedback, no fabricated receipt.
            AssertThat(GodotObject.IsInstanceValid(overlay)).IsFalse();
            AssertThat(adapter.Session).IsNull();
            AssertThat(old.StoryBattle).IsNull();
            AssertThat(old.StoryDrawSnapshot is null).IsTrue();
            AssertThat(old.GetChildren().OfType<PalStoryBattleView>().Count()).IsEqual(0);
            AssertThat(ReferenceEquals(script.PendingBattle, pending)).IsTrue();
            AssertThat(script.PendingDisplay).IsNull();
            AssertThat(battle.Completion).IsNull();
            AssertThat(old.Failure).IsNull();
        }
        AssertThat(GodotObject.IsInstanceValid(old)).IsFalse();
        AssertThat(Input.IsKeyPressed(Key.Enter)).IsFalse();
    }

    [TestCase(Timeout = 20000)]
    public async Task EnemyFeedbackKeepsDisplayedHpUntilImpact()
    {
        using var fixture = new AuthoredStoryFixture("Loss");
        using var host = new StoryHost(GameSceneConfiguration.FromArguments("--story-profile", fixture.StoryUri,
            "--battle-visual-profile", fixture.VisualUri, "--battle-seed", "1"));
        host.Tap(Key.Enter);
        await host.Wait(() => host.Main.Dialogue!.Session!.PendingDisplay is not null);
        await host.FinishDisplay();
        await host.Wait(() => host.Main.StoryBattleMode == StoryBattlePresentation.AttackMenu);
        await host.ConfirmMenu(StoryBattlePresentation.TargetMenu);
        await host.ConfirmMenu(StoryBattlePresentation.Resolving);
        await host.Wait(() => host.Main.StoryBattleMode == StoryBattlePresentation.Feedback);
        AssertThat(host.Actions[0].Attacker).IsEqual(ClassicBattleSide.Enemy);
        AssertThat(host.Actions[0].Damage > 0).IsTrue();
        AssertThat(host.Main.StoryDisplayedPlayerHp == 20).IsTrue();
        AssertThat(host.Main.StoryResultVisible).IsFalse();
    }

    [TestCase(Timeout = 90000)]
    public async Task PartyTwoPlayersPickTargetsByCyclingExecuteQueueAndWin()
    {
        // PartyWin: two authored roles (dex100) always act before two id7 slots (dex0, speed 16..19).
        // fight.c157-170 bounds: one player hit deals 43..297, so each 43hp slot dies to its first hit;
        // the second member retargets when both picked the same slot, and the dead enemies' own
        // queue entries pass without producing a new LastAction.
        using var fixture = new AuthoredStoryFixture("PartyWin");
        var selection = GameSceneConfiguration.FromArguments("--story-profile", fixture.StoryUri,
            "--battle-visual-profile", fixture.VisualUri, "--story-language", "zh-Hans", "--battle-seed", "1");
        Main old;
        using (var host = new StoryHost(selection,
            enemySlots: [new(100, 120), new(128, 104)], players: [new(200, 176), new(256, 152)]))
        {
            old = host.Main;
            AssertThat(old.Failure).IsNull();
            host.Tap(Key.Enter);
            await host.Wait(() => old.Dialogue!.Session!.PendingDisplay is not null);
            var script = old.Dialogue!.Session!;
            Guid call = script.CallId;
            await host.FinishDisplay();
            await host.Wait(() => old.StoryBattleMode == StoryBattlePresentation.AttackMenu);
            var battle = old.StoryBattle!;
            var request = script.PendingBattle!;
            AssertThat(ReferenceEquals(old.StoryBattle, request.Lifecycle)).IsTrue();
            AssertThat(battle.Current.Combat.Players.Count).IsEqual(2);
            AssertThat(battle.Current.Combat.Enemies.Count).IsEqual(2);
            AssertThat(battle.Current.Combat.SelectingPlayerIndex).IsEqual(0);
            // Member 0 confirms attack, then target slot 0; one physical Enter each.
            await host.ConfirmMenu(StoryBattlePresentation.TargetMenu);
            await host.ConfirmMenu(StoryBattlePresentation.AttackMenu);
            // The member-switch Enter selected member 0's target only; member 1 starts unselected.
            AssertThat(battle.Current.Combat.Phase).IsEqual(ClassicBattlePhase.Selecting);
            AssertThat(battle.Current.Combat.Players[0].SelectedEnemySlot).IsEqual(0);
            AssertThat(battle.Current.Combat.Players[1].SelectedEnemySlot).IsNull();
            AssertThat(battle.Current.Combat.SelectingPlayerIndex).IsEqual(1);
            await host.ConfirmMenu(StoryBattlePresentation.TargetMenu);
            // Cycle the highlighted target to slot 1, then confirm member 1's attack.
            host.Tap(Key.Right);
            await host.Wait(() => old.StoryBattleMode == StoryBattlePresentation.Resolving);
            AssertThat(battle.Current.Combat.Players[1].SelectedEnemySlot).IsEqual(1);
            AssertThat(battle.Current.Combat.Phase).IsEqual(ClassicBattlePhase.Resolving);
            AssertThat(battle.Current.Combat.ActionQueue.Count).IsEqual(4);
            while (script.PendingBattle is not null)
            {
                await host.Wait(() => old.StoryBattleMode == StoryBattlePresentation.Feedback || script.PendingBattle is null);
                if (script.PendingBattle is null) break;
                await host.Wait(() => old.StoryBattleMode != StoryBattlePresentation.Feedback || script.PendingBattle is null);
            }
            AssertThat(battle.Completion!.Outcome).IsEqual(ClassicBattlePhase.Won);
            AssertThat(script.CallId).IsEqual(call);
            // Exactly two distinct presented actions (the two member kills); dead-enemy queue
            // entries passed without re-presenting the previous action.
            AssertThat(host.Actions.Count).IsEqual(2);
            AssertThat(host.Actions.All(a => a.Attacker == ClassicBattleSide.Player)).IsTrue();
            AssertThat(ReferenceEquals(host.Actions[0], host.Actions[1])).IsFalse();
            AssertThat(host.Actions[0].Damage > 42).IsTrue();
            AssertThat(host.Actions[1].Damage > 42).IsTrue();
            AssertThat(battle.Current.Combat.Players.Select(p => p.Hp).SequenceEqual(new[] { 100, 95 })).IsTrue();
            AssertThat(battle.Current.Combat.Enemies.Select(e => e.Hp).SequenceEqual(new[] { 0, 0 })).IsTrue();
            AssertThat(old.StoryBattleMode).IsNull();
            AssertThat(old.GetChildren().OfType<PalStoryBattleView>().Count()).IsEqual(0);
            AssertThat(old.GetChildren().OfType<Pal.Game.Resources.PalMapScene>().Single().Visible).IsTrue();
            AssertThat(script.PendingDisplay!.Content.Parts[0].Text!).IsEqual("胜");
            await host.FinishDisplay();
            await host.Wait(() => old.Interaction!.Phase == NpcInteractionPhase.Exploring);
            AssertThat(script.Phase).IsEqual(TriggerScriptPhase.Completed);
            AssertThat(old.Failure).IsNull();
        }
        AssertThat(GodotObject.IsInstanceValid(old)).IsFalse();
        AssertThat(Input.IsKeyPressed(Key.Enter)).IsFalse();
        AssertThat(Input.IsKeyPressed(Key.Right)).IsFalse();
        // Same-URI fresh Main: prepared resources are reused read-only, no session state leaks.
        using (var fresh = new StoryHost(selection,
            enemySlots: [new(100, 120), new(128, 104)], players: [new(200, 176), new(256, 152)]))
        {
            AssertThat(fresh.Main.Failure).IsNull();
            AssertThat(fresh.Main.Dialogue!.Session!.CallId).IsEqual(Guid.Empty);
            AssertThat(fresh.Main.StoryBattle).IsNull();
            fresh.Tap(Key.Enter);
            await fresh.Wait(() => fresh.Main.Dialogue!.Session!.PendingDisplay is not null);
            AssertThat(fresh.Main.Dialogue!.Session!.PendingDisplay!.Content.Parts[0].Text!).IsEqual("战");
        }
    }

    [TestCase(Timeout = 120000)]
    public async Task PartyLossCoversEnemyActionsMemberDeathPassAndLostClosure()
    {
        // PartyLoss: enemy slots (dex500) act before both members; every landed hit kills a
        // 20hp member in one action, and member attacks (atk1 vs defense 1023) can never clear
        // a 43hp slot, so the battle continues through rounds with a dead member passing until Lost.
        using var fixture = new AuthoredStoryFixture("PartyLoss");
        var selection = GameSceneConfiguration.FromArguments("--story-profile", fixture.StoryUri,
            "--battle-visual-profile", fixture.VisualUri, "--story-language", "zh-Hans", "--battle-seed", "1");
        Main old;
        using (var host = new StoryHost(selection,
            enemySlots: [new(100, 120), new(128, 104)], players: [new(200, 176), new(256, 152)]))
        {
            old = host.Main;
            AssertThat(old.Failure).IsNull();
            host.Tap(Key.Enter);
            await host.Wait(() => old.Dialogue!.Session!.PendingDisplay is not null);
            var script = old.Dialogue!.Session!;
            Guid call = script.CallId;
            await host.FinishDisplay();
            await host.Wait(() => old.StoryBattleMode == StoryBattlePresentation.AttackMenu);
            var battle = old.StoryBattle!;
            var deadMember = -1;
            while (script.PendingBattle is not null)
            {
                if (old.StoryBattleMode == StoryBattlePresentation.AttackMenu)
                {
                    var selecting = battle.Current.Combat.SelectingPlayerIndex;
                    AssertThat(selecting).IsNotNull();
                    // Once a member died, only the living member selects and no dead member acts again.
                    if (deadMember >= 0)
                    {
                        AssertThat(selecting).IsNotEqual(deadMember);
                        AssertThat(battle.Current.Combat.Players[deadMember].Hp).IsEqual(0);
                    }
                    await host.ConfirmMenu(StoryBattlePresentation.TargetMenu);
                    await host.ConfirmMenu(StoryBattlePresentation.Resolving);
                }
                await host.Wait(() => old.StoryBattleMode == StoryBattlePresentation.Feedback
                    || old.StoryBattleMode == StoryBattlePresentation.AttackMenu || script.PendingBattle is null);
                for (int i = 0; i < 2; i++)
                    if (battle.Current.Combat.Players[i].Hp == 0 && deadMember < 0) deadMember = i;
                if (script.PendingBattle is null) break;
                await host.Wait(() => old.StoryBattleMode != StoryBattlePresentation.Feedback || script.PendingBattle is null);
            }
            AssertThat(battle.Completion!.Outcome).IsEqual(ClassicBattlePhase.Lost);
            AssertThat(deadMember >= 0).IsTrue();
            AssertThat(battle.Current.Combat.Players.All(p => p.Hp == 0)).IsTrue();
            // Enemy actions really targeted members, and the dead member never presented
            // an action after the killing enemy action (round-1 deaths may precede any member action).
            AssertThat(host.Actions.Any(a => a.Attacker == ClassicBattleSide.Enemy && a.TargetIndex is 0 or 1 && !a.Evaded)).IsTrue();
            var killIndex = -1;
            for (int i = host.Actions.Count - 1; i >= 0; i--)
                if (host.Actions[i].Attacker == ClassicBattleSide.Enemy && !host.Actions[i].Evaded && host.Actions[i].TargetIndex == deadMember) { killIndex = i; break; }
            AssertThat(killIndex >= 0).IsTrue();
            AssertThat(host.Actions.Select((a, i) => (a, i))
                .Where(x => x.a.Attacker == ClassicBattleSide.Player && x.a.ActorIndex == deadMember)
                .All(x => x.i < killIndex)).IsTrue();
            AssertThat(script.CallId).IsEqual(call);
            AssertThat(script.PendingDisplay!.Content.Parts[0].Text!).IsEqual("败");
            await host.FinishDisplay();
            await host.Wait(() => old.Interaction!.Phase == NpcInteractionPhase.Exploring);
            AssertThat(script.Phase).IsEqual(TriggerScriptPhase.Completed);
            AssertThat(old.StoryBattleMode).IsNull();
            AssertThat(old.GetChildren().OfType<PalStoryBattleView>().Count()).IsEqual(0);
            AssertThat(old.Failure).IsNull();
        }
        AssertThat(GodotObject.IsInstanceValid(old)).IsFalse();
        AssertThat(Input.IsKeyPressed(Key.Enter)).IsFalse();
        // A key pressed after the result reconnection must not leak into the next interaction.
        using (var fresh = new StoryHost(selection,
            enemySlots: [new(100, 120), new(128, 104)], players: [new(200, 176), new(256, 152)]))
        {
            AssertThat(fresh.Main.Failure).IsNull();
            AssertThat(fresh.Main.Interaction!.Phase).IsEqual(NpcInteractionPhase.Exploring);
            fresh.Tap(Key.Enter);
            await fresh.Wait(() => fresh.Main.Dialogue!.Session!.PendingDisplay is not null);
            AssertThat(fresh.Main.Dialogue!.Session!.PendingDisplay!.Content.Parts[0].Text!).IsEqual("战");
        }
    }

    [TestCase]
    public void EnemyPhysicalReferenceTablesCoverZeroWaitInclusiveFramesAndReturn()
    {
        var player = new Vector2(240, 170); var enemy = new Vector2(100, 120);
        // Independently counted from fight.c4987–5135; columns magic, attack, wait, impact tick.
        int[,] table = { {0,0,0,6}, {0,0,2,6}, {0,2,0,4}, {0,2,2,10},
            {1,0,0,7}, {1,0,2,7}, {1,2,0,5}, {1,2,2,11},
            {3,0,0,9}, {3,0,2,9}, {3,2,0,7}, {3,2,2,13} };
        for (int row = 0; row < table.GetLength(0); row++)
        foreach (bool evaded in new[] { false, true })
        {
            int magic = table[row,0], attack = table[row,1], wait = table[row,2], hit = table[row,3];
            var sequence = new StoryEnemyAttackPresentation(2, magic, attack, wait, player, enemy,
                80, evaded ? 80 : 0, 150, new(1, ClassicBattleSide.Enemy, 0, evaded ? 0 : 80, false, evaded));
            AssertThat(sequence.Duration == hit + 11).IsTrue();
            AssertThat(sequence.ImpactPose == hit).IsTrue();
            AssertThat(sequence.At(hit - 1).DisplayedPlayerHp).IsEqual(80);
            AssertThat(sequence.At(hit - 1).ResultVisible).IsFalse();
            var impact = sequence.At(hit);
            AssertThat(impact.DisplayedPlayerHp).IsEqual(evaded ? 80 : 0);
            AssertThat(impact.ResultVisible).IsTrue();
            AssertThat(impact.PlayerFrame).IsEqual(evaded ? 3 : 4);
            AssertThat(impact.PlayerColorShift).IsEqual(evaded ? 0 : 6);
            AssertThat(impact.EnemyPosition == new Vector2(196,154)).IsTrue();
            AssertThat(sequence.At(hit+1).PlayerPosition == new Vector2(248,174)).IsTrue();
            AssertThat(sequence.At(hit+2).PlayerPosition == new Vector2(250,175)).IsTrue();
            AssertThat(sequence.At(hit+5).EnemyPosition == enemy).IsTrue();
            AssertThat(sequence.At(hit+6).PlayerFrame).IsEqual(evaded ? 0 : 2);
            AssertThat(sequence.At(hit+11).PlayerPosition == player).IsTrue();
            if (attack == 2 && wait == 2)
            {
                AssertThat(sequence.At(hit-6).EnemyFrame).IsEqual(1 + magic);
                AssertThat(sequence.At(hit-4).EnemyFrame).IsEqual(2 + magic);
                AssertThat(sequence.At(hit-2).EnemyFrame).IsEqual(3 + magic);
            }
            if (attack == 0) AssertThat(sequence.At(hit-1).EnemyFrame).IsEqual(1);
            if (magic > 0)
            {
                AssertThat(sequence.At(0).EnemyFrame).IsEqual(2);
                AssertThat(sequence.At(1).EnemyFrame).IsEqual(2);
            }
            AssertThat(sequence.At(magic*2).EnemyPosition == enemy - new Vector2(magic < 3 ? 2 : 0, magic < 3 ? 1 : 0)).IsTrue();
        }
        var incomingIdle = new StoryEnemyAttackPresentation(3, 0, 0, 0, player, enemy, 80, 80, 150,
            new(1, ClassicBattleSide.Enemy, 0, 0, false, true), 2);
        AssertThat(incomingIdle.At(0).EnemyFrame).IsEqual(2);
        AssertThat(incomingIdle.At(3).EnemyFrame).IsEqual(2);
        AssertThat(StoryEnemyAttackPresentation.RestFrame(29,150)).IsEqual(1);
        AssertThat(StoryEnemyAttackPresentation.RestFrame(30,150)).IsEqual(0);
        AssertThat(StoryEnemyAttackPresentation.RestFrame(99,1000)).IsEqual(1);
        AssertThat(StoryEnemyAttackPresentation.RestFrame(100,1000)).IsEqual(0);
        AssertThat((int)StoryEnemyAttackPresentation.ShiftIndex(0xa3,6)).IsEqual(0xa9);
        AssertThat((int)StoryEnemyAttackPresentation.ShiftIndex(0xad,6)).IsEqual(0xaf);
        AssertThat((int)StoryEnemyAttackPresentation.ShiftIndex(0x21,-6)).IsEqual(0x20);
    }

    [TestCase]
    public void NormalStoryArgumentsAcceptExplicitProfiles()
    {
        var selection = GameSceneConfiguration.FromArguments("--story-profile", "res://Assets/Story.tres",
            "--battle-visual-profile", "res://Assets/Visual.tres", "--story-language", "en", "--battle-seed", "0");
        AssertThat(selection).IsNotNull();
    }

    [TestCase("Win", "zh-Hans", Timeout = 20000)]
    [TestCase("Win", "en", Timeout = 20000)]
    [TestCase("Loss", "zh-Hans", Timeout = 20000)]
    [TestCase("Loss", "en", Timeout = 20000)]
    public async Task NormalMainSearchBattleAndResultReturnThroughOwnedLifecycle(string scenario, string language)
    {
        using var fixture = new AuthoredStoryFixture(scenario);
        string hash = Godot.FileAccess.GetSha256(fixture.StoryUri);
        var selection = GameSceneConfiguration.FromArguments("--story-profile", fixture.StoryUri,
            "--battle-visual-profile", fixture.VisualUri, "--story-language", language, "--battle-seed", "1");
        Main old;
        using (var host = new StoryHost(selection))
        {
            old = host.Main;
            AssertThat(old.Failure).IsNull();
            AssertThat(old.Exploration!.Leader.X).IsEqual((short)320);
            AssertThat(old.StoryBattleMode).IsNull();
            bool unassociatedRejected = false;
            var selected = old.Dialogue!.Session!.Entries.Single();
            try { old.Dialogue.Start(selected.FileId, selected.Id, 1); }
            catch (InvalidOperationException) { unassociatedRejected = true; }
            AssertThat(unassociatedRejected).IsTrue();
            AssertThat(old.Dialogue.Session!.CallId).IsEqual(Guid.Empty);
            host.Tap(Key.Enter);
            await host.Wait(() => old.Dialogue!.Session!.PendingDisplay is not null);
            var script = old.Dialogue!.Session!;
            Guid call = script.CallId;
            Guid origin = old.Interaction!.PendingTrigger!.Id;
            AssertThat(script.SourceRequestId).IsEqual((Guid?)origin);
            AssertThat(script.Caller).IsEqual(1);
            AssertThat(script.PendingDisplay!.Content.Parts[0].Text!).IsEqual(language == "en" ? "FIGHT" : "战");
            await host.FinishDisplay();
            await host.Wait(() => old.StoryBattleMode == StoryBattlePresentation.AttackMenu);
            var request = script.PendingBattle!;
            AssertThat(ReferenceEquals(old.StoryBattle, request.Lifecycle)).IsTrue();
            await host.ConfirmMenu(StoryBattlePresentation.TargetMenu);
            AssertThat(request.Lifecycle.Current.Combat.Phase).IsEqual(ClassicBattlePhase.Selecting);
            AssertThat(request.Lifecycle.Current.Combat.LastAction).IsNull();
            await host.ConfirmMenu(StoryBattlePresentation.Resolving);
            await host.Wait(() => old.StoryBattleMode == StoryBattlePresentation.Feedback);
            AssertThat(request.Lifecycle.Current.Combat.LastAction).IsNotNull();
            // Only menu input selects attacks. No per-frame automatic attack policy exists.
            while (script.PendingBattle is not null)
            {
                await host.Wait(() => old.StoryBattleMode == StoryBattlePresentation.AttackMenu || script.PendingBattle is null);
                if (script.PendingBattle is null) break;
                await host.ConfirmMenu(StoryBattlePresentation.TargetMenu);
                await host.ConfirmMenu(StoryBattlePresentation.Resolving);
                await host.Wait(() => old.StoryBattleMode == StoryBattlePresentation.Feedback);
            }
            AssertThat(script.CallId).IsEqual(call);
            AssertThat(request.Lifecycle.Completion!.Outcome).IsEqual(scenario == "Win" ? ClassicBattlePhase.Won : ClassicBattlePhase.Lost);
            AssertThat(old.StoryBattleMode).IsNull();
            AssertThat(old.GetChildren().OfType<PalStoryBattleView>().Count()).IsEqual(0);
            AssertThat(old.GetChildren().OfType<Pal.Game.Resources.PalMapScene>().Single().Visible).IsTrue();
            AssertThat(script.PendingDisplay!.Content.Parts[0].Text!).IsEqual(language == "en" ? scenario == "Win" ? "WIN" : "LOSE" : scenario == "Win" ? "胜" : "败");
            await host.FinishDisplay();
            await host.Wait(() => old.Interaction!.Phase == NpcInteractionPhase.Exploring);
            AssertThat(script.Phase).IsEqual(TriggerScriptPhase.Completed);
            AssertThat(script.CurrentPosition!.SourceAddress).IsEqual(scenario == "Win" ? 6 : 9);
            AssertThat(script.ReturnEntry).IsEqual(1);
            AssertThat(script.AcknowledgeBattle(request.Id, request.Lifecycle.Completion)).IsFalse();
            AssertThat(host.Actions.Count > 0).IsTrue();
            AssertThat(host.PoseChanges > 0).IsTrue();
            host.Tap(Key.Enter);
            await host.Wait(() => script.PendingDisplay is not null);
            AssertThat(script.CallId == call).IsFalse();
            AssertThat(script.SourceRequestId == origin).IsFalse();
            AssertThat(script.SourceRequestId).IsEqual((Guid?)old.Interaction!.PendingTrigger!.Id);
            AssertThat(old.Failure).IsNull();
        }
        AssertThat(GodotObject.IsInstanceValid(old)).IsFalse();
        AssertThat(Input.IsKeyPressed(Key.Enter)).IsFalse();
        using (var fresh = new StoryHost(selection))
        {
            AssertThat(fresh.Main.Failure).IsNull();
            AssertThat(fresh.Main.Dialogue!.Session!.CallId).IsEqual(Guid.Empty);
            AssertThat(fresh.Main.StoryBattle).IsNull();
            fresh.Tap(Key.Enter);
            await fresh.Wait(() => fresh.Main.Dialogue!.Session!.PendingDisplay is not null);
            AssertThat(fresh.Main.Dialogue!.Session!.Caller).IsEqual(1);
        }
        AssertThat(Godot.FileAccess.GetSha256(fixture.StoryUri)).IsEqual(hash);
    }

    [TestCase]
    public void FailedStoryPreloadPublishesNoPartialScene()
    {
        var selection = GameSceneConfiguration.FromArguments("--story-profile", "res://Assets/Missing-Story.tres",
            "--battle-visual-profile", "res://Assets/Missing-Visual.tres");
        using var host = new StoryHost(selection);
        AssertThat(host.Main.Failure).IsNotNull();
        AssertThat(host.Main.GetChildCount()).IsEqual(0);
        AssertThat(host.Main.Exploration).IsNull();
    }

    [TestCase]
    public void StoryParserRejectsIncompleteMixedDuplicateUnknownAndInvalidValues()
    {
        string[] valid = ["--story-profile", "res://Assets/Story.tres", "--battle-visual-profile", "res://Assets/Visual.tres"];
        foreach (string[] extra in new string[][] {
            ["--story-profile", "res://Assets/Other.tres"], ["--story-language", "fr"],
            ["--battle-seed", "18446744073709551616"], ["--battle-seed", "+1"],
            ["--story-unknown", "x"], ["--m3-dialogue-profile", "res://Assets/Other.tres"],
            ["--battle-seed"] })
        {
            bool rejected = false;
            try { GameSceneConfiguration.FromArguments(valid.Concat(extra).ToArray()); }
            catch (System.IO.InvalidDataException) { rejected = true; }
            AssertThat(rejected).IsTrue();
        }
    }

    [TestCase]
    public void InclusiveGodotRandomDegenerateRangesDoNotConsumeEntropy()
    {
        using var subject = new GodotBattleRandom(91); using var control = new GodotBattleRandom(91);
        AssertThat(subject.NextInclusive(2, 2)).IsEqual(2);
        AssertThat(subject.NextInclusive(3, -1)).IsEqual(3);
        AssertThat(subject.NextInclusive(2f, 2f)).IsEqual(2f);
        AssertThat(subject.NextInclusive(3f, -1f)).IsEqual(3f);
        for (int i = 0; i < 12; i++)
        {
            AssertThat(subject.NextInclusive(-5, 8)).IsEqual(control.NextInclusive(-5, 8));
            AssertThat(subject.NextInclusive(.9f, 1.1f)).IsEqual(control.NextInclusive(.9f, 1.1f));
        }
    }

    [TestCase]
    public void NativeWordsRemainOptionalAndCopyWordZeroBlankDataWithoutSharingMutableStorage()
    {
        using var language = new Pal.Game.Resources.PalDialogueLanguage();
        AssertThat(language.ToCorePackage().Language).IsEqual("zh-Hans");
        AssertThat(language.CopyWords().Count).IsEqual(0);
        language.Words[0] = "";
        language.Words[65535] = "unused";
        var copy = language.CopyWords();
        language.Words[65535] = "changed";
        AssertThat(copy[0]).IsEqual("");
        AssertThat(copy[65535]).IsEqual("unused");
        language.Words[65536] = "bad";
        bool rejected = false;
        try { language.ToCorePackage(); } catch (System.IO.InvalidDataException) { rejected = true; }
        AssertThat(rejected).IsTrue();
    }

    [TestCase]
    public void StoryBattlePoseClockEnforcesFortyMillisecondsLateDeliveryWrapAndRestart()
    {
        foreach (uint start in new[] { 100u, uint.MaxValue - 19 })
        {
            var clock = StoryBattlePoseClock.Start(start);
            AssertThat(clock.Pose).IsEqual(0);
            AssertThat(clock.Advance(unchecked(start + 39)) == clock).IsTrue();
            var due = clock.Advance(unchecked(start + 40));
            AssertThat(due.Pose).IsEqual(1);
            AssertThat(due.PoseStartedAt).IsEqual(unchecked(start + 40));
            var late = clock.Advance(unchecked(start + 900));
            AssertThat(late.Pose).IsEqual(1);
            AssertThat(late.PoseStartedAt).IsEqual(unchecked(start + 900));
            AssertThat(late.Advance(unchecked(start + 939)) == late).IsTrue();
            AssertThat(late.Advance(unchecked(start + 940)).Pose).IsEqual(2);
            var restarted = StoryBattlePoseClock.Start(unchecked(start + 910));
            AssertThat(restarted.Pose).IsEqual(0);
            AssertThat(restarted.Advance(unchecked(start + 949)) == restarted).IsTrue();
            AssertThat(restarted.Advance(unchecked(start + 950)).Pose).IsEqual(1);
        }
    }

    [TestCase(Timeout = 20000)]
    public async Task ActiveBattleExitReleasesOverlayAndBindingsBeforeOwnedNodesAreFreed()
    {
        using var fixture = new AuthoredStoryFixture("Win");
        var selection = GameSceneConfiguration.FromArguments("--story-profile", fixture.StoryUri,
            "--battle-visual-profile", fixture.VisualUri, "--battle-seed", "1");
        using (var host = new StoryHost(selection))
        {
            host.Tap(Key.Enter);
            await host.Wait(() => host.Main.Dialogue!.Session!.PendingDisplay is not null);
            await host.FinishDisplay();
            await host.Wait(() => host.Main.StoryBattleMode == StoryBattlePresentation.AttackMenu);
            await host.ConfirmMenu(StoryBattlePresentation.TargetMenu);
            await host.ConfirmMenu(StoryBattlePresentation.Resolving);
            await host.Wait(() => host.Main.StoryBattleMode == StoryBattlePresentation.Feedback);
            var overlay = host.Main.GetChildren().OfType<PalStoryBattleView>().Single();
            var adapter = host.Main.Dialogue!; var display = host.Main.Display!;
            var script = adapter.Session!;
            var battle = host.Main.StoryBattle!;
            var pending = script.PendingBattle!;
            var completion = battle.Completion;
            AssertThat(pending).IsNotNull();
            AssertThat(script.Phase).IsEqual(TriggerScriptPhase.WaitingForBattle);
            AssertThat(GodotObject.IsInstanceValid(overlay)).IsTrue();
            host.Main.GetParent().RemoveChild(host.Main); // Actual active exit, not a completed roundtrip Dispose.
            AssertThat(GodotObject.IsInstanceValid(host.Main)).IsTrue();
            AssertThat(GodotObject.IsInstanceValid(overlay)).IsFalse();
            AssertThat(GodotObject.IsInstanceValid(display)).IsTrue(); // Still owned by detached Main.
            AssertThat(adapter.Session).IsNull();
            AssertThat(host.Main.StoryBattle).IsNull();
            AssertThat(host.Main.StoryBattleMode).IsNull();
            AssertThat(host.Main.StoryBattlePose).IsNull();
            AssertThat(host.Main.StoryEnemyPresentation is null).IsTrue();
            AssertThat(host.Main.StoryDisplayedPlayerHp is null).IsTrue();
            AssertThat(host.Main.StoryDisplayedEnemyHp is null && host.Main.StoryDrawSnapshot is null).IsTrue();
            AssertThat(host.Main.StoryResultVisible).IsFalse();
            AssertThat(host.Main.Interaction).IsNull();
            AssertThat(host.Main.Dialogue).IsNull();
            AssertThat(host.Main.Display).IsNull();
            AssertThat(host.Main.Exploration).IsNull();
            AssertThat(host.Main.SelectedLanguage).IsNull();
            AssertThat(host.Main.GetChildren().OfType<PalStoryBattleView>().Count()).IsEqual(0);
            AssertThat(ReferenceEquals(script.PendingBattle, pending)).IsTrue();
            AssertThat(ReferenceEquals(script.PendingBattle!.Lifecycle, battle)).IsTrue();
            AssertThat(ReferenceEquals(battle.Completion, completion)).IsTrue();
            AssertThat(script.Phase).IsEqual(TriggerScriptPhase.WaitingForBattle);
            AssertThat(script.PendingDisplay).IsNull(); // Exit did not fabricate or acknowledge a receipt.
            using var fresh = new StoryHost(selection);
            fresh.Tap(Key.Enter);
            await fresh.Wait(() => fresh.Main.Dialogue!.Session!.PendingDisplay is not null);
            AssertThat(fresh.Main.Failure).IsNull();
            AssertThat(adapter.Session).IsNull();
            AssertThat(host.Main.Failure).IsNull();
        }
        AssertThat(Input.IsKeyPressed(Key.Enter)).IsFalse();
    }

    private sealed class StoryHost : IDisposable
    {
        internal Main Main { get; } = new();
        private readonly ISceneRunner runner;
        private readonly Control? focus = ((SceneTree)Engine.GetMainLoop()).Root.GuiGetFocusOwner();
        internal List<ClassicBattleAction> Actions { get; } = [];
        internal int PoseChanges { get; private set; }
        internal sealed record Callback(int Update, uint Now, int Counter, int Frame,
            StoryBattlePresentation? Mode, ClassicBattleAction? Action, int? FinalPose);
        internal List<Callback> NonterminalReturns { get; } = [];
        internal List<Callback> PendingOuterCallbacks { get; } = [];
        internal List<Callback> ActionStarts { get; } = [];
        private Callback Capture(StoryBattleDrawSnapshot draw, int? finalPose = null) => new(updates,
            Main.StoryPresentationTicks, draw.EnemyIdleCounter, draw.EnemyFrame, Main.StoryBattleMode,
            Main.StoryBattle?.Current.Combat.LastAction, finalPose);
        private StoryBattlePoseSnapshot? previousPose;
        private StoryBattleDrawSnapshot? previousDraw;
        private int beforeEnemyHp;
        private uint outerStartedAt;
        private bool outerPending;
        private ClassicBattleAction? last;
        private uint? numberBornAt;
        internal int NumberBirths { get; private set; }
        internal int NumberDraws { get; private set; }
        private int updates;
        private Exception? observationFailure;
        private StoryBattlePresentation? previousMode;
        private ClassicBattleView? previousCombat;
        private ulong? resultBoundFrame;
        private PalDialogueDisplay? display;
        private readonly bool party;
        private readonly Vector2[] partyPlayers = [], partyEnemies = [];
        internal StoryHost(GameSceneConfiguration selection, Vector2[]? enemySlots = null, Vector2[]? players = null)
        {
            // Party hosts reuse the same production scene; only the observer switches to party checks.
            party = enemySlots is not null;
            if (party)
            {
                if (enemySlots!.Length != 2 || players is not { Length: 2 })
                    throw new ArgumentException("Party hosts require exactly two enemy slots and two player origins.");
                partyEnemies = enemySlots; partyPlayers = players!;
            }
            Main.Configure(selection); Main.UpdateCompleted += Updated;
            // Runner owns the container, so an actual Main removal can be inspected before final Free.
            var container = new Node();
            container.AddChild(Main);
            runner = ISceneRunner.Load(container, autoFree: true);
            display = Main.Display;
            if (display is not null) display.UpdateCompleted += DisplayUpdated;
        }
        private void Updated()
        {
            updates++;
            try
            {
                var combat = Main.StoryBattle?.Current.Combat;
                var pose = Main.StoryBattlePose;
                var draw = Main.StoryDrawSnapshot;
                if (!party && pose is { Pose: 0 } && previousPose is null) numberBornAt = null;
                if (!party && pose is { } feedbackPose && draw is { } feedbackDraw && combat?.LastAction is { } feedbackAction)
                {
                    // Author B: idle1/magic1/attack1/wait0. fight.c5078 enqueues after the
                    // last prehit draw (4); Delay(FALSE) waits40 before its first impact UIUpdate (5).
                    int birthPose = feedbackAction.Attacker == ClassicBattleSide.Player ? 7 : 4;
                    if (feedbackPose.Pose == birthPose && numberBornAt is null)
                    { numberBornAt = Main.StoryPresentationTicks; NumberBirths++; }
                    if (numberBornAt is { } birth)
                    {
                        uint elapsed = unchecked(Main.StoryPresentationTicks - birth);
                        bool visible = elapsed < 440 && feedbackPose.Pose >= (feedbackAction.Attacker == ClassicBattleSide.Player ? 8 : 5);
                        if (feedbackAction.Attacker == ClassicBattleSide.Enemy)
                        {
                            var enemyDigits = feedbackDraw.NumberDigits.Where(d => d.Position.Y > 50).ToArray();
                            AssertThat(enemyDigits.All(d => d.BornAt == birth))
                                .OverrideFailureMessage($"Enemy number BornAt must be real prehit callback {birth}, not impact {Main.StoryPresentationTicks}; actual={string.Join(',', enemyDigits.Select(d => d.BornAt))}")
                                .IsTrue();
                            AssertThat(enemyDigits.Length > 0 == (visible && !feedbackAction.Evaded && feedbackAction.Damage > 0)).IsTrue();
                            if (feedbackPose.Pose == 5 && previousPose?.Pose == 4)
                                GD.Print($"PAL_ENEMY_NUMBER_BIRTH:prehit={birth};hit={Main.StoryPresentationTicks};age={elapsed/40};digits={enemyDigits.Length}");
                        }
                        var currentDigits = feedbackDraw.NumberDigits.Where(d => d.BornAt == birth).ToArray();
                        bool numeric = visible && !feedbackAction.Evaded && feedbackAction.Damage > 0;
                        AssertThat(currentDigits.Length > 0 == numeric).IsTrue();
                        if (numeric)
                        {
                            NumberDraws++;
                            string text = string.Concat(currentDigits.Reverse().Select(d => d.Digit.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                            AssertThat(text).IsEqual(feedbackAction.Damage.ToString(System.Globalization.CultureInfo.InvariantCulture));
                            AssertThat(currentDigits.All(d => d.Color == StoryNumberColor.Blue)).IsTrue();
                            int x = feedbackAction.Attacker == ClassicBattleSide.Player ? 100 : 240;
                            int y = feedbackAction.Attacker == ClassicBattleSide.Player ? 10 : 95;
                            AssertThat(currentDigits[0].Position == new Vector2(x,y-(int)(elapsed/40))).IsTrue();
                        }
                        int? word = visible && (feedbackAction.Critical || feedbackAction.Evaded) ? feedbackAction.Evaded ? 60002 : 60003 : null;
                        AssertThat(feedbackDraw.FeedbackWord == word).IsTrue();
                    }
                    else AssertThat(feedbackDraw.FeedbackWord is null).IsTrue();
                }
                if (!party && pose is { Pose: 0 } && previousPose is null) beforeEnemyHp = previousCombat!.EnemyHp;
                if (!party && pose is { } playerPose && combat?.LastAction?.Attacker == ClassicBattleSide.Player && draw is { } playerDraw)
                {
                    int tick = playerPose.Pose;
                    int[] frames = [7,7,7,7,8,8,8,9,9,9,9,9,9,0,0,0];
                    // Rest can be dying/dead; active frames are independent literals.
                    if (tick < 13) AssertThat(playerDraw.PlayerFrame).IsEqual(frames[tick]);
                    AssertThat(playerDraw.ResultVisible == (tick >= 7)).IsTrue();
                    AssertThat(playerDraw.DisplayedEnemyHp).IsEqual(tick < 7 ? beforeEnemyHp : combat.EnemyHp);
                    AssertThat(Main.StoryDisplayedEnemyHp == playerDraw.DisplayedEnemyHp).IsTrue();
                    AssertThat(playerDraw.EffectFrame == (tick is >= 7 and <= 9 ? tick - 7 : (int?)null)).IsTrue();
                    AssertThat(playerDraw.EnemyColorShift).IsEqual(tick is 8 or 9 ? 6 : 0);
                    if (tick == 7) AssertThat(playerDraw.EffectPosition == new Vector2(100,122)).IsTrue();
                    if (tick == 8) AssertThat(playerDraw.EffectPosition == new Vector2(84,138)).IsTrue();
                    if (tick == 9) AssertThat(playerDraw.EffectPosition == new Vector2(68,154)).IsTrue();
                    if (previousPose is { } lastPose && previousDraw is { } lastDraw)
                    {
                        int expected = lastDraw.EnemyIdleCounter;
                        if (lastPose.Pose != tick && tick != 13) expected = expected == 2 ? 1 : 2;
                        AssertThat(playerDraw.EnemyIdleCounter).IsEqual(expected);
                    }
                }
                if (!party && pose is { } enemyPose && combat?.LastAction?.Attacker == ClassicBattleSide.Enemy && draw is { } enemyDraw
                    && previousPose is { } prevEnemyPose && previousDraw is { } prevEnemyDraw)
                {
                    int expected = prevEnemyDraw.EnemyIdleCounter;
                    if (enemyPose.Pose != prevEnemyPose.Pose && enemyPose.Pose >= 11) expected = expected == 2 ? 1 : 2;
                    AssertThat(enemyDraw.EnemyIdleCounter).IsEqual(expected);
                }
                if (!party && draw is { } firstDraw && previousDraw is null)
                {
                    AssertThat(firstDraw.EnemyIdleCounter).IsEqual(2);
                    outerStartedAt = Main.StoryPresentationTicks;
                }
                else if (!party && draw is { } outerDraw && previousDraw is { } priorDraw && previousPose is null)
                {
                    int expected = priorDraw.EnemyIdleCounter;
                    if (outerPending) PendingOuterCallbacks.Add(Capture(outerDraw));
                    if (outerPending || unchecked(Main.StoryPresentationTicks - outerStartedAt) >= 40)
                    {
                        expected = expected == 2 ? 1 : 2;
                        outerStartedAt = Main.StoryPresentationTicks; outerPending = false;
                    }
                    // A player's first TRUE windup happens in the resolving callback, after the outer caller.
                    if (pose is { Pose: 0 } && combat!.LastAction!.Attacker == ClassicBattleSide.Player)
                        expected = expected == 2 ? 1 : 2;
                    AssertThat(outerDraw.EnemyIdleCounter).IsEqual(expected);
                }
                if (!party && pose is null && previousPose is { } finalPose && draw is { } returnedDraw && previousDraw is { } finalDraw)
                {
                    int expected = previousCombat!.LastAction!.Attacker == ClassicBattleSide.Enemy
                        ? finalDraw.EnemyIdleCounter == 2 ? 1 : 2 : finalDraw.EnemyIdleCounter;
                    AssertThat(returnedDraw.EnemyIdleCounter).IsEqual(expected);
                    NonterminalReturns.Add(Capture(returnedDraw, finalPose.Pose));
                    outerPending = true;
                }
                if (previousPose is { } beforePose)
                {
                    if (pose is { } currentPose && currentPose.Pose != beforePose.Pose)
                    {
                        AssertThat(currentPose.Pose).IsEqual(beforePose.Pose + 1);
                        AssertThat(unchecked(currentPose.Now - beforePose.PoseStartedAt) >= 40).IsTrue();
                        AssertThat(currentPose.PoseStartedAt).IsEqual(currentPose.Now);
                        PoseChanges++;
                    }
                    if (pose is null)
                    {
                        // Even the final pose must finish before feedback releases its action/overlay.
                        AssertThat(unchecked(Main.StoryPresentationTicks - beforePose.PoseStartedAt) >= 40).IsTrue();
                        if (previousCombat?.LastAction?.Attacker == ClassicBattleSide.Enemy)
                            AssertThat(beforePose.Pose).IsEqual(15); // hit tick5 + eleven timed recovery ticks.
                    }
                }
                if (pose is { } firstPose && previousPose is null)
                {
                    AssertThat(firstPose.Pose).IsEqual(0);
                    AssertThat(firstPose.PoseStartedAt).IsEqual(firstPose.Now);
                }
                if (!party && Main.StoryEnemyPresentation is { } shown && combat is not null)
                {
                    AssertThat(Main.StoryDisplayedPlayerHp == shown.DisplayedPlayerHp).IsTrue();
                    AssertThat(Main.StoryResultVisible == shown.ResultVisible).IsTrue();
                    // Authored package: magic1*2 + preMove2 + hold1; attack wait0.
                    bool hit = pose!.Value.Pose >= 5;
                    AssertThat(shown.ResultVisible == hit).IsTrue();
                    AssertThat(shown.DisplayedPlayerHp).IsEqual(hit ? combat.PlayerHp :
                        previousCombat!.PlayerHp + (previousMode == StoryBattlePresentation.Feedback ? combat.LastAction!.Damage : 0));
                    if (hit && pose.Value.Pose == 5)
                        AssertThat(shown.PlayerFrame).IsEqual(combat.LastAction!.Evaded ? 3 : 4);
                }
                previousPose = pose; previousDraw = draw;
                if (Main.StoryBattleMode == StoryBattlePresentation.Feedback && previousMode == StoryBattlePresentation.Feedback)
                    AssertThat(ReferenceEquals(combat, previousCombat)).IsTrue();
                if (combat?.LastAction is { } action && !ReferenceEquals(action, last))
                {
                    AssertThat(previousMode).IsEqual(StoryBattlePresentation.Resolving);
                    AssertThat(Main.StoryBattleMode).IsEqual(StoryBattlePresentation.Feedback);
                    Actions.Add(action); last = action;
                    ActionStarts.Add(Capture(draw!.Value));
                }
                if (previousMode is not null && Main.StoryBattleMode is null && Main.Dialogue?.Session?.PendingDisplay is not null)
                    resultBoundFrame = Engine.GetProcessFrames();
                previousMode = Main.StoryBattleMode; previousCombat = combat;
            }
            catch (Exception error) { observationFailure ??= error; }
        }
        private void DisplayUpdated()
        {
            if (resultBoundFrame is not { } bound) return;
            try { AssertThat(Engine.GetProcessFrames() > bound).IsTrue(); }
            catch (Exception error) { observationFailure ??= error; }
        }
        internal async Task ConfirmMenu(StoryBattlePresentation expected)
        {
            bool observed = false;
            var before = Main.StoryBattle!.Current.Combat;
            void Capture()
            {
                Main.UpdateCompleted -= Capture;
                try
                {
                    AssertThat(Main.StoryBattleMode).IsEqual(expected);
                    AssertThat(ReferenceEquals(before.LastAction, Main.StoryBattle!.Current.Combat.LastAction)).IsTrue();
                }
                catch (Exception error) { observationFailure ??= error; }
                observed = true;
            }
            Main.UpdateCompleted += Capture;
            try { Tap(Key.Enter); await Wait(() => observed); }
            finally { Main.UpdateCompleted -= Capture; }
        }
        internal void Tap(Key key) { Send(key, true); Send(key, false); }
        private static void Send(Key key, bool pressed)
        {
            using var input = new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed };
            Input.ParseInputEvent(input); Input.FlushBufferedEvents();
        }
        internal async Task Wait(Func<bool> predicate)
        {
            ulong start = Time.GetTicksMsec();
            int before = updates;
            while (!predicate() || updates == before)
            {
                if (observationFailure is not null) throw observationFailure;
                if (Main.Failure is not null) throw new InvalidOperationException(Main.Failure);
                if (Time.GetTicksMsec() - start > 5000) throw new TimeoutException("Story production boundary not observed; mode=" + Main.StoryBattleMode);
                await runner.AwaitIdleFrame();
            }
            if (observationFailure is not null) throw observationFailure;
        }
        internal async Task FinishDisplay()
        {
            var display = Main.Dialogue!.Session!.PendingDisplay!;
            await Wait(() => display.Display.Current.Phase == DialogueDisplayPhase.WaitingForAdvance);
            Tap(Key.Enter);
            await Wait(() => Main.Dialogue!.Session!.PendingDisplay?.Id != display.Id);
        }
        public void Dispose()
        {
            try
            {
                Send(Key.Enter, false); Main.UpdateCompleted -= Updated;
                if (display is not null) display.UpdateCompleted -= DisplayUpdated;
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
