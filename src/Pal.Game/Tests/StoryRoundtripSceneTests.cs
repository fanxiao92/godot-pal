using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
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
    [TestCase(Timeout = 20000)]
    public async Task SinglePlayerTwoEnemiesDrawBothDuplicateSlotsOnFirstBattleUpdateAndFreshMain()
    {
        using var fixture = new AuthoredStoryFixture("PartyWin", singlePlayerTwoEnemies: true);
        var selection = GameSceneConfiguration.FromArguments("--story-profile", fixture.StoryUri,
            "--battle-visual-profile", fixture.VisualUri, "--battle-seed", "1");
        for (int run = 0; run < 2; run++)
        {
            bool observedFirstBattle = false, observedLethalFeedback = false, observedDisappearance = false;
            void Observe(Main main)
            {
                if (main.StoryBattle is null) return;
                var draw = main.StoryDrawSnapshot!.Value;
                if (main.StoryBattle.Current.Combat.Enemies[0].Hp == 0 && !observedDisappearance)
                {
                    AssertThat(draw.PartyEnemies.Select(enemy => enemy.Hp).SequenceEqual(new[]
                        { main.StoryBattleMode == StoryBattlePresentation.Feedback && !draw.ResultVisible ? 43 : 0, 43 })).IsTrue();
                    if (main.StoryBattleMode == StoryBattlePresentation.Feedback)
                    {
                        observedLethalFeedback = true;
                        AssertThat(draw.PartyEnemies.All(enemy => enemy.Visible))
                            .OverrideFailureMessage("Lethal feedback must keep the struck slot visible until the action finishes.").IsTrue();
                    }
                    else
                    {
                        observedDisappearance = true;
                        AssertThat(observedLethalFeedback).IsTrue();
                        AssertThat(draw.PartyEnemies.Select(enemy => enemy.Visible).SequenceEqual(new[] { false, true }))
                            .OverrideFailureMessage("Dead enemy slot must stop drawing after lethal feedback without hiding its duplicate-ID neighbour.").IsTrue();
                    }
                }
                // Capture the first battle completion unconditionally, not the first correct draw.
                if (observedFirstBattle) return;
                observedFirstBattle = true;
                AssertThat(draw.PartyEnemies.Count)
                    .OverrideFailureMessage("One-player/two-enemy Main must draw both enemy slots on its first battle update.")
                    .IsEqual(2);
                AssertThat(draw.EnemyIdleCounters.SequenceEqual(new[] { 2, 2 })).IsTrue();
                AssertThat(draw.EnemyIdleFrames.SequenceEqual(new[] { 0, 0 })).IsTrue();
                AssertThat(draw.PartyPlayers.Count).IsEqual(1);
                AssertThat(draw.PartyPlayers[0].Position == new Vector2(240, 170)).IsTrue();
                AssertThat(draw.PartyEnemies[0].Position == new Vector2(100, 120)).IsTrue();
                AssertThat(draw.PartyEnemies[1].Position == new Vector2(128, 104)).IsTrue();
                AssertThat(draw.PartyEnemies.Select(enemy => enemy.Hp).SequenceEqual(new[] { 43, 43 })).IsTrue();
                AssertThat(draw.PartyEnemies.All(enemy => enemy.Visible)).IsTrue();
                AssertThat(main.StoryBattle.Current.Combat.Enemies.Select(enemy => enemy.Id)
                    .SequenceEqual(new[] { 7, 7 })).IsTrue();
            }
            using var host = new StoryHost(selection, [new(100, 120), new(128, 104)], [new(240, 170)], Observe);
            AssertThat(host.Main.Failure).IsNull();
            host.Tap(Key.Enter);
            await host.Wait(() => host.Main.Dialogue!.Session!.PendingDisplay is not null);
            await host.FinishDisplay();
            await host.Wait(() => observedFirstBattle);
            AssertThat(host.Main.StoryBattleMode).IsEqual(StoryBattlePresentation.AttackMenu);
            await host.ConfirmMenu(StoryBattlePresentation.TargetMenu);
            await host.ConfirmMenu(StoryBattlePresentation.Resolving);
            await host.Wait(() => observedDisappearance);
            await host.Wait(() => host.Main.StoryBattleMode == StoryBattlePresentation.AttackMenu);
            await host.ConfirmMenu(StoryBattlePresentation.TargetMenu);
            AssertThat(host.Main.StoryDrawSnapshot!.Value.PartyEnemies.Select(enemy => enemy.Visible)
                .SequenceEqual(new[] { false, true })).IsTrue();
        }
    }

    [TestCase(Timeout = 20000)]
    public async Task PreparedHeterogeneousSlotsUseOwnActionsTargetsAndFreshMain()
    {
        using var fixture = new AuthoredStoryFixture("PartyWin", heterogeneousEnemies: true);
        string[] before = fixture.Snapshot();
        var story = Pal.Game.Profiles.NativeStoryProfile.Load(fixture.StoryUri);
        var visual = Pal.Game.Profiles.NativeBattleVisualProfile.Load(fixture.VisualUri, story);
        var slots = visual.Teams[18].EnemySlots;
        AssertThat(slots.Select(s => s.TargetId).SequenceEqual(new[] { 7, 7 })).IsTrue();
        AssertThat(slots.Select(s => s.EnemySprite!.SpriteId).SequenceEqual(new[] { 97, 98 })).IsTrue();
        AssertThat(slots.Select(s => s.EnemySprite!.FrameCount).SequenceEqual(new[] { 3, 4 })).IsTrue();
        AssertThat(slots.Select(s => s.EnemySprite!.Frames[0].Width).SequenceEqual(new[] { 2, 3 })).IsTrue();
        AssertThat(slots[0].EnemySprite!.Frames.Select(f => f.Height).SequenceEqual(new[] { 1, 1, 1 })).IsTrue();
        AssertThat(slots[1].EnemySprite!.Frames.Select(f => f.Height).SequenceEqual(new[] { 3, 6, 9, 12 })).IsTrue();
        AssertThat((slots[0].IdleFrames, slots[0].IdleAnimSpeed, slots[0].MagicFrames, slots[0].AttackFrames, slots[0].ActWaitFrames))
            .IsEqual((1, 0, 1, 1, 0));
        AssertThat((slots[1].IdleFrames, slots[1].IdleAnimSpeed, slots[1].MagicFrames, slots[1].AttackFrames, slots[1].ActWaitFrames))
            .IsEqual((2, 3, 0, 2, 2));
        var selection = GameSceneConfiguration.FromArguments("--story-profile", fixture.StoryUri,
            "--battle-visual-profile", fixture.VisualUri, "--battle-seed", "1");
        for (int run = 0; run < 2; run++)
        {
            bool first = false, wrappedZero = false, removed = false;
            int[] targetHeights = [0, 0];
            var hitTargets = new HashSet<int>();
            var enemyPoses = new HashSet<int>();
            void Observe(Main main)
            {
                if (main.StoryBattle is not { } battle) return;
                var combat = battle.Current.Combat;
                var draw = main.StoryDrawSnapshot!.Value;
                if (!first)
                {
                    first = true; // First actual completion, not the first matching draw.
                    // battle.c1782 + first StartFrame: two WORD pre-decrements before first draw.
                    AssertThat(draw.EnemyIdleCounters.SequenceEqual(new[] { 65534, 1 })).IsTrue();
                    wrappedZero = true;
                    AssertThat(draw.EnemyIdleFrames.SequenceEqual(new[] { 0, 0 })).IsTrue();
                    AssertThat(combat.Enemies.Select(e => e.Id).SequenceEqual(new[] { 7, 7 })).IsTrue();
                    AssertThat(draw.PartyEnemies.Select(e => e.Position).SequenceEqual(new[] { new Vector2(100, 120), new Vector2(128, 104) })).IsTrue();
                    AssertThat(draw.PartyEnemies.Select(e => e.Hp).SequenceEqual(new[] { 43, 43 })).IsTrue();
                    AssertThat(draw.PartyEnemies.All(e => e.Visible)).IsTrue();
                }
                var action = combat.LastAction;
                bool lethalFeedback = main.StoryBattleMode == StoryBattlePresentation.Feedback
                    && action is { Attacker: ClassicBattleSide.Player, TargetIndex: 0 };
                if (combat.Enemies[0].Hp == 0)
                {
                    AssertThat(draw.PartyEnemies[0].Visible).IsEqual(lethalFeedback);
                    AssertThat(draw.PartyEnemies[1].Visible).IsTrue();
                    if (!lethalFeedback) removed = true;
                }
                if (main.StoryBattleMode != StoryBattlePresentation.Feedback || main.StoryBattlePose is not { } pose) return;
                if (action!.Attacker == ClassicBattleSide.Player)
                {
                    int target = action.TargetIndex;
                    // fight.c ShowPlayerAttackAnim samples current target height before approach.
                    // Pose3 is the last completed windup; literal per-frame art heights above.
                    if (pose.Pose == 3)
                        targetHeights[target] = target == 0 ? 1 : new[] { 3, 6 }[draw.EnemyIdleFrames[target]];
                    if (pose.Pose is >= 7 and <= 9)
                    {
                        AssertThat(targetHeights[target] > 0).IsTrue();
                        int fx = pose.Pose - 7;
                        Vector2 origin = target == 0 ? new(100, 120) : new(128, 104);
                        AssertThat(draw.EffectFrame == fx).IsTrue();
                        AssertThat(draw.EffectPosition == origin + new Vector2(-16 * fx, 10 - targetHeights[target] / 3 + 16 * fx)).IsTrue();
                        AssertThat(draw.PartyEnemies[1 - target].Position == (target == 0 ? new Vector2(128, 104) : new Vector2(100, 120))).IsTrue();
                        hitTargets.Add(target);
                    }
                }
                else
                {
                    AssertThat(action.ActorIndex).IsEqual(1); // The surviving duplicate id7, not slot0 metadata.
                    AssertThat(draw.PartyEnemies[0].Visible).IsFalse();
                    if (pose.Pose is 0 or 1 or 2 or 3)
                        AssertThat(draw.PartyEnemies[1].Position == new Vector2(128, 104)
                            - new Vector2(pose.Pose < 3 ? (pose.Pose + 1) * 2 : 6, pose.Pose < 3 ? pose.Pose + 1 : 3)).IsTrue();
                    if (pose.Pose is >= 4 and <= 9)
                    {
                        // fight.c4987–5055: magic0, three pre-moves + hold, inclusive attack2/wait2.
                        AssertThat(draw.PartyEnemies[1].Frame).IsEqual(1 + (pose.Pose - 4) / 2);
                        Vector2 player = action.TargetIndex == 0 ? new(200, 176) : new(256, 152);
                        AssertThat(draw.PartyEnemies[1].Position == player + new Vector2(-44, -16)).IsTrue();
                    }
                    AssertThat(action.CovererPartyIndex).IsNull();
                    Vector2 targetOrigin = action.TargetIndex == 0 ? new(200, 176) : new(256, 152);
                    if (pose.Pose == 10)
                    {
                        AssertThat(draw.PartyPlayers[action.TargetIndex].Frame).IsEqual(action.Evaded ? 3 : 4);
                        AssertThat(draw.PartyPlayers[action.TargetIndex].ColorShift).IsEqual(action.Evaded ? 0 : 6);
                        AssertThat(draw.PartyPlayers[action.TargetIndex].Position == targetOrigin).IsTrue();
                    }
                    if (pose.Pose is 11 or 12)
                    {
                        AssertThat(draw.PartyPlayers[action.TargetIndex].Position == targetOrigin
                            + (pose.Pose == 11 ? new Vector2(8, 4) : new Vector2(10, 5))).IsTrue();
                        AssertThat(draw.PartyPlayers[action.TargetIndex].ColorShift).IsEqual(0);
                    }
                    AssertThat(draw.PartyPlayers[1 - action.TargetIndex].Position
                        == (action.TargetIndex == 0 ? new Vector2(256, 152) : new Vector2(200, 176))).IsTrue();
                    AssertThat(draw.ResultVisible).IsEqual(pose.Pose >= 10);
                    AssertThat(draw.PartyPlayers[action.TargetIndex].Hp)
                        .IsEqual(combat.Players[action.TargetIndex].Hp + (pose.Pose < 10 ? action.Damage : 0));
                    enemyPoses.Add(pose.Pose);
                }
            }
            Main old;
            using (var host = new StoryHost(selection, [new(100, 120), new(128, 104)],
                [new(200, 176), new(256, 152)], Observe, enemyImpactPose: 10))
            {
                old = host.Main;
                AssertThat(old.Failure).IsNull();
                host.Tap(Key.Enter);
                await host.Wait(() => old.Dialogue!.Session!.PendingDisplay is not null);
                await host.FinishDisplay();
                await host.Wait(() => first && old.StoryBattleMode == StoryBattlePresentation.AttackMenu);
                await host.ConfirmMenu(StoryBattlePresentation.TargetMenu);
                await host.ConfirmMenu(StoryBattlePresentation.AttackMenu);
                await host.ConfirmMenu(StoryBattlePresentation.TargetMenu);
                host.Tap(Key.Right);
                await host.ConfirmMenu(StoryBattlePresentation.Resolving);
                await host.Wait(() => host.Actions.Count == 3 && old.StoryBattleMode == StoryBattlePresentation.AttackMenu);
                AssertThat(host.Actions.Select(a => (a.Attacker, a.ActorIndex, a.TargetIndex)).Take(2).SequenceEqual(new[]
                    { (ClassicBattleSide.Player, 1, 1), (ClassicBattleSide.Player, 0, 0) })).IsTrue();
                AssertThat(host.Actions[0].Damage is >= 33 and <= 38).IsTrue();
                AssertThat(host.Actions[1].Damage >= 43).IsTrue();
                AssertThat(wrappedZero && removed).IsTrue();
                AssertThat(hitTargets.SetEquals(new[] { 0, 1 })).IsTrue();
                AssertThat(enemyPoses.SetEquals(Enumerable.Range(0, 21))).IsTrue();
                AssertThat(old.StoryBattle!.Current.Combat.Enemies.Select(e => e.Hp).SequenceEqual(new[] { 0, 43 - host.Actions[0].Damage })).IsTrue();
            }
            AssertThat(GodotObject.IsInstanceValid(old)).IsFalse();
            AssertThat(Input.IsKeyPressed(Key.Enter) || Input.IsKeyPressed(Key.Right)).IsFalse();
            AssertThat(fixture.Snapshot()).IsEqual(before);
        }

        string output = ProjectSettings.GlobalizePath(fixture.Root);
        string rejectedOutput = Path.Combine(output, "RejectedStory");
        string authorPath = Path.Combine(fixture.InputDirectory, "story-package.json");
        byte[] originalAuthor = File.ReadAllBytes(authorPath);
        var positiveBytes = Directory.GetFiles(output, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes);
        var author = JsonNode.Parse(originalAuthor)!;
        // Only slot1's URI changes: its unchanged idle2 + magic0 + attack2 requires
        // indices 0..3, but this same fixture's sprite97 has only indices 0..2.
        author["visual"]!["teams"]![0]!["enemies"]![1]!["enemySpriteUri"] =
            author["visual"]!["teams"]![0]!["enemies"]![0]!["enemySpriteUri"]!.GetValue<string>();
        Dictionary<string, byte[]> SourceBytes() => Directory.GetFiles(fixture.InputDirectory, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes);
        try
        {
            File.WriteAllText(authorPath, author.ToJsonString());
            var sourceBefore = SourceBytes();
            bool outputAbsentBefore = !Directory.Exists(rejectedOutput) && !File.Exists(rejectedOutput);
            AssertThat(outputAbsentBefore).IsTrue();
            InvalidDataException? rejection = null;
            try { Pal.Game.Editor.PrepareStoryPackage.Prepare(fixture.InputDirectory, rejectedOutput); }
            catch (InvalidDataException error) { rejection = error; }
            var sourceAfter = SourceBytes();
            bool sourceUnchanged = sourceBefore.Keys.ToHashSet().SetEquals(sourceAfter.Keys)
                && sourceBefore.All(pair => pair.Value.SequenceEqual(sourceAfter[pair.Key]));
            bool outputAbsentAfter = !Directory.Exists(rejectedOutput) && !File.Exists(rejectedOutput);
            GD.Print("PAL_HETEROGENEOUS_NEGATIVE_CONTROL:" + JsonSerializer.Serialize(new
            {
                sourceBefore, sourceAfter, rejectedOutput, outputAbsentBefore, outputAbsentAfter,
                sourceUnchanged, exception = rejection?.ToString()
            }));
            const string missingFrame = "visual Teams[TeamId=18].EnemySlots[1]: enemy frame metadata references unaddressable frames";
            string expected = "^" + Regex.Escape(fixture.Root + "/RejectedStory/.story-package-")
                + "[0-9a-f]{32}" + Regex.Escape("/Visual.tres: " + missingFrame) + "$";
            AssertThat(rejection is not null && Regex.IsMatch(rejection.Message, expected)
                && rejection.InnerException?.Message == missingFrame).IsTrue();
            AssertThat(sourceUnchanged).IsTrue();
            AssertThat(outputAbsentAfter).IsTrue();
        }
        finally
        {
            File.WriteAllBytes(authorPath, originalAuthor);
            bool positiveBytesUnchanged = Directory.GetFiles(output, "*", SearchOption.AllDirectories)
                .ToHashSet().SetEquals(positiveBytes.Keys)
                && positiveBytes.All(pair => pair.Value.SequenceEqual(File.ReadAllBytes(pair.Key)));
            string[] restored = fixture.Snapshot();
            GD.Print("PAL_HETEROGENEOUS_NEGATIVE_RESTORED:" + JsonSerializer.Serialize(new
            {
                positiveBytesUnchanged, originalAuthor, restoredAuthor = File.ReadAllBytes(authorPath), before, restored
            }));
            AssertThat(positiveBytesUnchanged).IsTrue();
            AssertThat(File.ReadAllBytes(authorPath).SequenceEqual(originalAuthor)).IsTrue();
            AssertThat(restored).IsEqual(before);
        }
        string ownedInput = fixture.InputDirectory;
        fixture.Dispose();
        bool ownedCleaned = !Directory.Exists(ownedInput) && !Directory.Exists(output);
        GD.Print("PAL_HETEROGENEOUS_NEGATIVE_CLEANUP:" + JsonSerializer.Serialize(new { ownedInput, output, ownedCleaned }));
        AssertThat(ownedCleaned).IsTrue();
    }

    [TestCase(Timeout = 90000)]
    public async Task PreparedMixedEmptySlotsUseRuntimeIndicesTargetLoadedOnlyAndSettleOnceThroughFreshMain()
    {
        // #141 (T13c): a three-player party meets the five-slot team [empty, 7, empty, 7, empty]
        // through the formal Prepare and normal Main. Empty runtime slots keep identity, order and
        // position but never draw, target or act; the default target starts at the first alive
        // slot, cycling crosses empties, and the last real enemy's death settles exactly once
        // with only the loaded slots' rewards (3+3 exp / 5+5 cash, never the empty slots').
        using var fixture = new AuthoredStoryFixture("PartyWin", threePlayer: true, mixedEmptySlots: true);
        string[] before = fixture.Snapshot();
        var selection = GameSceneConfiguration.FromArguments("--story-profile", fixture.StoryUri,
            "--battle-visual-profile", fixture.VisualUri, "--story-language", "zh-Hans", "--battle-seed", "1");
        for (int run = 0; run < 2; run++)
        {
            bool first = false;
            void Observe(Main main)
            {
                if (main.StoryBattle is null) return;
                var draw = main.StoryDrawSnapshot!.Value;
                if (!first)
                {
                    first = true; // First actual completion, not the first matching draw.
                    AssertThat(main.StoryBattle!.Current.Combat.Enemies.Select(enemy => (enemy.Id, enemy.IsEmpty)).ToArray())
                        .IsEqual(new[] { (0, true), (7, false), (0, true), (7, false), (0, true) });
                    AssertThat(draw.PartyEnemies.Select(enemy => enemy.Hp).ToArray()).IsEqual(new[] { 0, 43, 0, 43, 0 });
                    AssertThat(draw.PartyEnemies.Select(enemy => enemy.Visible).ToArray())
                        .IsEqual(new[] { false, true, false, true, false });
                    AssertThat(draw.PartyEnemies.Select(enemy => enemy.Position).ToArray()).IsEqual(new[] {
                        new Vector2(72, 136), new(100, 120), new(128, 104), new(156, 88), new(184, 72) });
                }
                // Empty runtime slots never draw at any later callback, including while a
                // loaded neighbour is struck or removed.
                if (draw.PartyEnemies.Count == 5)
                    for (int slot = 0; slot < 5; slot += 2)
                        AssertThat(draw.PartyEnemies[slot].Visible).IsFalse();
            }
            Main old;
            using (var host = new StoryHost(selection,
                enemySlots: [new(72, 136), new(100, 120), new(128, 104), new(156, 88), new(184, 72)],
                players: [new(180, 180), new(234, 170), new(270, 146)], updateObserver: Observe))
            {
                old = host.Main;
                AssertThat(old.Failure).IsNull();
                host.Tap(Key.Enter);
                await host.Wait(() => old.Dialogue!.Session!.PendingDisplay is not null);
                await host.FinishDisplay();
                var script = old.Dialogue!.Session!;
                await host.Wait(() => old.StoryBattleMode == StoryBattlePresentation.AttackMenu);
                var battle = old.StoryBattle!;
                // Member 0 confirms attack; the target menu starts at the first alive slot 1.
                await host.ConfirmMenu(StoryBattlePresentation.TargetMenu);
                await host.ConfirmMenu(StoryBattlePresentation.AttackMenu);
                AssertThat(battle.Current.Combat.Players[0].SelectedEnemySlot).IsEqual(1);
                // Member 1 cycles right across empty slot 2 to the last loaded slot 3.
                await host.ConfirmMenu(StoryBattlePresentation.TargetMenu);
                host.Tap(Key.Right);
                await host.ConfirmMenu(StoryBattlePresentation.AttackMenu);
                AssertThat(battle.Current.Combat.Players[1].SelectedEnemySlot).IsEqual(3);
                // Member 2 keeps the default first-alive target; its confirm builds the queue.
                await host.ConfirmMenu(StoryBattlePresentation.TargetMenu);
                await host.ConfirmMenu(StoryBattlePresentation.Resolving);
                AssertThat(battle.Current.Combat.Players.Select(player => player.SelectedEnemySlot).ToArray())
                    .IsEqual(new int?[] { 1, 3, 1 });
                AssertThat(battle.Current.Combat.ActionQueue.Count).IsEqual(5);
                while (script.PendingBattle is not null && old.StoryBattleMode != StoryBattlePresentation.Settlement)
                {
                    await host.Wait(() => old.StoryBattleMode is StoryBattlePresentation.Feedback or StoryBattlePresentation.Settlement
                        || script.PendingBattle is null);
                    if (script.PendingBattle is null || old.StoryBattleMode == StoryBattlePresentation.Settlement) break;
                    await host.Wait(() => old.StoryBattleMode != StoryBattlePresentation.Feedback
                        || script.PendingBattle is null);
                }
                // Every presented action targets a loaded slot; empty slots never act or answer.
                AssertThat(host.Actions.All(action => action.Attacker == ClassicBattleSide.Player)).IsTrue();
                AssertThat(host.Actions.All(action => action.TargetIndex is 1 or 3)).IsTrue();
                AssertThat(battle.Completion!.Outcome).IsEqual(ClassicBattlePhase.Won);
                AssertThat(battle.Current.Combat.Enemies.Select(enemy => enemy.Hp).ToArray()).IsEqual(new[] { 0, 0, 0, 0, 0 });
                AssertThat(battle.Current.Combat.Enemies.Select(enemy => enemy.IsEmpty).ToArray())
                    .IsEqual(new[] { true, false, true, false, true });
                // Both loaded slots' rewards settle exactly once; the empty slots contribute nothing.
                await host.Wait(() => old.StoryBattleMode == StoryBattlePresentation.Settlement);
                AssertThat(old.StorySettlementLine!.Contains(":6", StringComparison.Ordinal)
                    && old.StorySettlementLine.Contains("10", StringComparison.Ordinal)).IsTrue();
                host.Tap(Key.Enter);
                await host.Wait(() => old.StoryBattleMode is null);
                await host.FinishDisplay();
                await host.Wait(() => old.Interaction!.Phase == NpcInteractionPhase.Exploring);
                AssertThat(old.StoryExperienceGranted).IsEqual(6);
                AssertThat(old.StoryCashGranted).IsEqual(10);
                AssertThat(old.StoryBattleMode).IsNull();
                AssertThat(old.GetChildren().OfType<PalStoryBattleView>().Count()).IsEqual(0);
                AssertThat(script.Phase).IsEqual(TriggerScriptPhase.Completed);
                AssertThat(old.Failure).IsNull();
            }
            AssertThat(GodotObject.IsInstanceValid(old)).IsFalse();
            AssertThat(Input.IsKeyPressed(Key.Enter) || Input.IsKeyPressed(Key.Right)).IsFalse();
            AssertThat(fixture.Snapshot()).IsEqual(before);
        }
    }

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
        // Each ordinal owns a state, even with shared prepared metadata. FALSE gestures
        // assign only the actor; the spectator keeps its frame and WORD counter.
        var slots = new[] { new StoryEnemyIdleState(3, 2), new StoryEnemyIdleState(2, 3) };
        int[][] frames = [[0,0], [1,0], [1,1], [2,1], [2,1], [0,0]];
        int[][] counters = [[1,2], [2,1], [1,3], [2,2], [1,1], [2,3]];
        for (int tick = 0; tick < frames.Length; tick++)
        {
            foreach (var slot in slots) slot.Advance();
            AssertThat(slots.Select(slot => slot.Frame).SequenceEqual(frames[tick])).IsTrue();
            AssertThat(slots.Select(slot => (int)slot.Counter).SequenceEqual(counters[tick])).IsTrue();
        }
        slots[1].SetFrame(5);
        AssertThat(slots[0].Frame).IsEqual(0);
        AssertThat((int)slots[0].Counter).IsEqual(2);
        AssertThat((int)slots[1].Counter).IsEqual(3);
        var zero = new StoryEnemyIdleState(3, 0);
        zero.Advance(); AssertThat((int)zero.Counter).IsEqual(65535); AssertThat(zero.Frame).IsEqual(0);
        zero.SetFrame(2); zero.Advance(); AssertThat((int)zero.Counter).IsEqual(65534); AssertThat(zero.Frame).IsEqual(2);
        var fresh = new StoryEnemyIdleState(3, 2);
        AssertThat(fresh.Frame).IsEqual(0); AssertThat((int)fresh.Counter).IsEqual(2);
    }

    [TestCase(Timeout = 90000)]
    public async Task PreparedThreePlayersCancelTargetsRevertEarlierSelectionsAndSettleOnce()
    {
        // #142 (T14): the classic kKeyMenu cancel works through normal Main production input —
        // cancelling the target returns to the action menu with no commit, cancelling the
        // action menu reopens the newest committed member (the first member is a boundary
        // no-op), and the finally committed selections settle exactly once through the formal
        // mixed-empty chain with only loaded slots targeted.
        using var fixture = new AuthoredStoryFixture("PartyWin", threePlayer: true, mixedEmptySlots: true);
        string[] before = fixture.Snapshot();
        var selection = GameSceneConfiguration.FromArguments("--story-profile", fixture.StoryUri,
            "--battle-visual-profile", fixture.VisualUri, "--story-language", "zh-Hans", "--battle-seed", "1");
        Main old;
        using (var host = new StoryHost(selection,
            enemySlots: [new(72, 136), new(100, 120), new(128, 104), new(156, 88), new(184, 72)],
            players: [new(180, 180), new(234, 170), new(270, 146)]))
        {
            old = host.Main;
            AssertThat(old.Failure).IsNull();
            host.Tap(Key.Enter);
            await host.Wait(() => old.Dialogue!.Session!.PendingDisplay is not null);
            await host.FinishDisplay();
            var script = old.Dialogue!.Session!;
            await host.Wait(() => old.StoryBattleMode == StoryBattlePresentation.AttackMenu);
            var battle = old.StoryBattle!;
            // The first member's cancel is the boundary no-op: same menu, no commit, no queue.
            host.Tap(Key.Escape);
            await host.Wait(() => old.StoryBattleMode == StoryBattlePresentation.AttackMenu);
            AssertThat(battle.Current.Combat.SelectingPlayerIndex).IsEqual(0);
            AssertThat(battle.Current.Combat.Players.All(player => player.SelectedEnemySlot is null)).IsTrue();
            AssertThat(battle.Current.Combat.ActionQueue.Count).IsEqual(0);
            // Cancelling the target returns to the action menu without committing member 0.
            await host.ConfirmMenu(StoryBattlePresentation.TargetMenu);
            host.Tap(Key.Escape);
            await host.Wait(() => old.StoryBattleMode == StoryBattlePresentation.AttackMenu);
            AssertThat(battle.Current.Combat.Players[0].SelectedEnemySlot).IsNull();
            // Member 0 commits slot 3 (cycling across the empty slot 2).
            await host.ConfirmMenu(StoryBattlePresentation.TargetMenu);
            host.Tap(Key.Right);
            await host.ConfirmMenu(StoryBattlePresentation.AttackMenu);
            AssertThat(battle.Current.Combat.Players[0].SelectedEnemySlot).IsEqual(3);
            // Member 1's action-menu cancel reopens member 0's still-editable selection.
            host.Tap(Key.Escape);
            await host.Wait(() => battle.Current.Combat.SelectingPlayerIndex == 0);
            AssertThat(old.StoryBattleMode).IsEqual(StoryBattlePresentation.AttackMenu);
            AssertThat(battle.Current.Combat.Players[0].SelectedEnemySlot).IsNull();
            // Re-entering the target menu restarts from the first alive slot; member 0 re-edits to 1.
            await host.ConfirmMenu(StoryBattlePresentation.TargetMenu);
            await host.ConfirmMenu(StoryBattlePresentation.AttackMenu);
            AssertThat(battle.Current.Combat.Players[0].SelectedEnemySlot).IsEqual(1);
            // Member 1: a target cancel then re-confirm keeps the default first-alive target.
            await host.ConfirmMenu(StoryBattlePresentation.TargetMenu);
            host.Tap(Key.Escape);
            await host.Wait(() => old.StoryBattleMode == StoryBattlePresentation.AttackMenu);
            await host.ConfirmMenu(StoryBattlePresentation.TargetMenu);
            await host.ConfirmMenu(StoryBattlePresentation.AttackMenu);
            AssertThat(battle.Current.Combat.Players[1].SelectedEnemySlot).IsEqual(1);
            // Member 2's cancel reopens member 1; member 1 re-edits onto slot 3.
            host.Tap(Key.Escape);
            await host.Wait(() => battle.Current.Combat.SelectingPlayerIndex == 1);
            AssertThat(battle.Current.Combat.Players[0].SelectedEnemySlot).IsEqual(1);
            AssertThat(battle.Current.Combat.Players[1].SelectedEnemySlot).IsNull();
            await host.ConfirmMenu(StoryBattlePresentation.TargetMenu);
            host.Tap(Key.Right);
            await host.ConfirmMenu(StoryBattlePresentation.AttackMenu);
            AssertThat(battle.Current.Combat.Players[1].SelectedEnemySlot).IsEqual(3);
            // The final member's commit closes editing and builds the queue.
            await host.ConfirmMenu(StoryBattlePresentation.TargetMenu);
            await host.ConfirmMenu(StoryBattlePresentation.Resolving);
            AssertThat(battle.Current.Combat.Players.Select(player => player.SelectedEnemySlot).ToArray())
                .IsEqual(new int?[] { 1, 3, 1 });
            AssertThat(script.Phase).IsEqual(TriggerScriptPhase.WaitingForBattle);
            while (script.PendingBattle is not null && old.StoryBattleMode != StoryBattlePresentation.Settlement)
            {
                await host.Wait(() => old.StoryBattleMode is StoryBattlePresentation.Feedback or StoryBattlePresentation.Settlement
                    || script.PendingBattle is null);
                if (script.PendingBattle is null || old.StoryBattleMode == StoryBattlePresentation.Settlement) break;
                await host.Wait(() => old.StoryBattleMode != StoryBattlePresentation.Feedback
                    || script.PendingBattle is null);
            }
            // Every presented action targets a loaded slot; the party wins and settles exactly once.
            AssertThat(host.Actions.All(action => action.Attacker == ClassicBattleSide.Player)).IsTrue();
            AssertThat(host.Actions.All(action => action.TargetIndex is 1 or 3)).IsTrue();
            AssertThat(battle.Completion!.Outcome).IsEqual(ClassicBattlePhase.Won);
            AssertThat(battle.Current.Combat.Enemies.Select(enemy => enemy.Hp).ToArray()).IsEqual(new[] { 0, 0, 0, 0, 0 });
            await host.Wait(() => old.StoryBattleMode == StoryBattlePresentation.Settlement);
            AssertThat(old.StorySettlementLine!.Contains(":6", StringComparison.Ordinal)
                && old.StorySettlementLine.Contains("10", StringComparison.Ordinal)).IsTrue();
            host.Tap(Key.Enter);
            await host.Wait(() => old.StoryBattleMode is null);
            await host.FinishDisplay();
            await host.Wait(() => old.Interaction!.Phase == NpcInteractionPhase.Exploring);
            AssertThat(old.StoryExperienceGranted).IsEqual(6);
            AssertThat(old.StoryCashGranted).IsEqual(10);
            AssertThat(old.StoryBattleMode).IsNull();
            AssertThat(old.GetChildren().OfType<PalStoryBattleView>().Count()).IsEqual(0);
            AssertThat(script.Phase).IsEqual(TriggerScriptPhase.Completed);
            AssertThat(old.Failure).IsNull();
        }
        AssertThat(GodotObject.IsInstanceValid(old)).IsFalse();
        AssertThat(Input.IsKeyPressed(Key.Enter) || Input.IsKeyPressed(Key.Right) || Input.IsKeyPressed(Key.Escape)).IsFalse();
        AssertThat(fixture.Snapshot()).IsEqual(before);
    }

    [TestCase(Timeout = 90000)]
    public async Task AttackAllMemberCommitsWithoutTargetMenuStrikesBothLoadedSlotsAndSettlesOnce()
    {
        // #143 (T15): the authored attack-all member's Attack confirm commits the all-target
        // selection directly (uibattle.c 1094-1099/1611-1617 — the classic target menu is never
        // entered for that member), one strike covers both loaded slots of the mixed five-slot
        // team in reference order {2,1,0,4,3} with per-target records, and the battle settles
        // exactly once through the formal Prepare chain.
        using var fixture = new AuthoredStoryFixture("PartyWin", mixedEmptySlots: true, attackAllMember0: true);
        string[] before = fixture.Snapshot();
        var selection = GameSceneConfiguration.FromArguments("--story-profile", fixture.StoryUri,
            "--battle-visual-profile", fixture.VisualUri, "--story-language", "zh-Hans", "--battle-seed", "1");
        bool capableMemberSawTargetMenu = false;
        List<int[]> strikeTargets = [];
        List<int[]> strikeDamages = [];
        List<bool> strikeSingleCritical = [];
        List<int> numberSlots = [];
        IReadOnlyList<ClassicBattleAction>? capturedStrike = null;
        void Observe(Main observed)
        {
            var combat = observed.StoryBattle?.Current.Combat;
            if (observed.StoryBattleMode == StoryBattlePresentation.TargetMenu && combat?.SelectingPlayerIndex == 0)
                capableMemberSawTargetMenu = true;
            if (combat?.LastAction is { Attacker: ClassicBattleSide.Player, AttackAll: true }
                && combat.LastActionTargets is { Count: > 1 } targets)
            {
                if (!ReferenceEquals(capturedStrike, targets))
                {
                    capturedStrike = targets;
                    strikeTargets.Add(targets.Select(record => record.TargetIndex).ToArray());
                    strikeDamages.Add(targets.Select(record => record.Damage).ToArray());
                    strikeSingleCritical.Add(targets.Select(record => record.Critical).Distinct().Count() == 1);
                }
            }
            if (observed.StoryBattleMode == StoryBattlePresentation.Feedback
                && combat?.LastAction is { AttackAll: true })
            {
                var digits = observed.GetChildren().OfType<PalStoryBattleView>().Single().NumberDigits;
                if (digits.Count >= 2)
                    numberSlots = digits.Select(digit => digit.Slot).Distinct().ToList();
            }
        }
        Main old;
        using (var host = new StoryHost(selection,
            enemySlots: [new(72, 136), new(100, 120), new(128, 104), new(156, 88), new(184, 72)],
            players: [new(200, 176), new(256, 152)], updateObserver: Observe))
        {
            old = host.Main;
            AssertThat(old.Failure).IsNull();
            host.Tap(Key.Enter);
            await host.Wait(() => old.Dialogue!.Session!.PendingDisplay is not null);
            await host.FinishDisplay();
            var script = old.Dialogue!.Session!;
            await host.Wait(() => old.StoryBattleMode == StoryBattlePresentation.AttackMenu);
            var battle = old.StoryBattle!;
            AssertThat(battle.Current.Combat.Players[0].CanAttackAll).IsTrue();
            AssertThat(battle.Current.Combat.Players[1].CanAttackAll).IsFalse();
            // Member 0's Attack confirm commits the all-target selection directly: member 1 is
            // already selecting and the classic target menu never opened for the capable member.
            host.Tap(Key.Enter);
            await host.Wait(() => old.StoryBattleMode == StoryBattlePresentation.AttackMenu
                && battle.Current.Combat.SelectingPlayerIndex == 1);
            AssertThat(battle.Current.Combat.Players[0].SelectedAttackAll).IsTrue();
            AssertThat(battle.Current.Combat.Players[0].SelectedEnemySlot).IsNull();
            // Member 1 keeps the classic single-target menu; the final commit builds the queue.
            await host.ConfirmMenu(StoryBattlePresentation.TargetMenu);
            await host.ConfirmMenu(StoryBattlePresentation.Resolving);
            while (script.PendingBattle is not null && old.StoryBattleMode != StoryBattlePresentation.Settlement)
            {
                await host.Wait(() => old.StoryBattleMode is StoryBattlePresentation.Feedback or StoryBattlePresentation.Settlement
                    || script.PendingBattle is null);
                if (script.PendingBattle is null || old.StoryBattleMode == StoryBattlePresentation.Settlement) break;
                await host.Wait(() => old.StoryBattleMode != StoryBattlePresentation.Feedback
                    || script.PendingBattle is null);
            }
            // Every attack-all strike hit exactly the loaded slots in reference order {2,1,0,4,3}.
            AssertThat(strikeTargets.Count > 0).IsTrue();
            AssertThat(strikeTargets.All(targets => targets.SequenceEqual(new[] { 1, 3 }))).IsTrue();
            AssertThat(strikeSingleCritical.All(single => single)).IsTrue(); // one roll covers the whole strike
            AssertThat(battle.Completion!.Outcome).IsEqual(ClassicBattlePhase.Won);
            AssertThat(battle.Current.Combat.Enemies.Select(enemy => enemy.Hp).ToArray()).IsEqual(new[] { 0, 0, 0, 0, 0 });
            // Both struck enemies displayed their own damage number at once (fight.c 2198-2212).
            AssertThat(numberSlots.Count >= 2).IsTrue();
            await host.Wait(() => old.StoryBattleMode == StoryBattlePresentation.Settlement);
            host.Tap(Key.Enter);
            await host.Wait(() => old.StoryBattleMode is null);
            await host.FinishDisplay();
            await host.Wait(() => old.Interaction!.Phase == NpcInteractionPhase.Exploring);
            AssertThat(old.StoryExperienceGranted).IsEqual(6);
            AssertThat(old.StoryCashGranted).IsEqual(10);
            AssertThat(old.StoryBattleMode).IsNull();
            AssertThat(old.GetChildren().OfType<PalStoryBattleView>().Count()).IsEqual(0);
            AssertThat(script.Phase).IsEqual(TriggerScriptPhase.Completed);
            AssertThat(old.Failure).IsNull();
        }
        AssertThat(capableMemberSawTargetMenu).IsFalse();
        AssertThat(GodotObject.IsInstanceValid(old)).IsFalse();
        AssertThat(Input.IsKeyPressed(Key.Enter)).IsFalse();
        AssertThat(fixture.Snapshot()).IsEqual(before);
    }

    [TestCase]
    public void AttackAllPresentationCoversEveryStruckEnemyWithOneAnim()
    {
        // fight.c 2036-2256 with sTarget -1: the actor walks the single-target offsets toward the
        // fixed (150,100) center; the three FX frames blit at BOTH struck enemies without the
        // per-frame x offset; both struck enemies shift color at poses 8-9 and shake -8/-4/-6
        // horizontally at poses 10-12; the HP result reveals at pose 7.
        var presentation = new Pal.Game.Dialogue.StoryPlayerAttackAllPresentation(new Vector2(200, 176),
            [new Vector2(100, 120), new Vector2(156, 88)], [1, 3], [80, 60], [40, 55], 2, dual: false, secondStrike: false);
        Vector2[] poses =
        [
            new(200, 176), new(200, 176), new(200, 176), new(200, 176),
            new(214, 120), new(214, 120), new(204, 118), new(204, 118), new(204, 118),
            new(206, 119), new(206, 119), new(206, 119), new(206, 119),
            new(200, 176), new(200, 176), new(200, 176)
        ];
        for (int pose = 0; pose < 16; pose++)
        {
            var shown = presentation.At(pose, 25);
            AssertThat(shown.PlayerFrame).IsEqual(pose < 4 ? 7 : pose < 7 ? 8 : pose < 13 ? 9 : 2);
            AssertThat(shown.PlayerPosition == poses[pose]).IsTrue();
            var struck = shown.StruckEnemies!;
            AssertThat(struck.Count).IsEqual(2);
            // Slot is the struck enemy's combat slot index (EnemyDraws keys per-slot draws on it),
            // not the hit-list ordinal — slots 1 and 3 are the loaded mixed-team slots.
            AssertThat(struck[0].Slot).IsEqual(1);
            AssertThat(struck[1].Slot).IsEqual(3);
            AssertThat(shown.EffectFrame == (pose is >= 7 and <= 9 ? pose - 7 : (int?)null)).IsTrue();
            for (int hit = 0; hit < 2; hit++)
            {
                Vector2 basePosition = hit == 0 ? new Vector2(100, 120) : new Vector2(156, 88);
                Vector2 shake = pose == 10 ? new Vector2(-8, 0) : pose == 11 ? new Vector2(-4, 0)
                    : pose == 12 ? new Vector2(-6, 0) : Vector2.Zero;
                AssertThat(struck[hit].Position == basePosition + shake).IsTrue();
                AssertThat(struck[hit].ColorShift).IsEqual(pose is 8 or 9 ? 6 : 0);
                AssertThat(struck[hit].DisplayedHp).IsEqual(pose >= 7 ? (hit == 0 ? 40 : 55) : (hit == 0 ? 80 : 60));
                AssertThat(struck[hit].ResultVisible == (pose >= 7)).IsTrue();
                AssertThat(struck[hit].EffectPosition == basePosition).IsTrue(); // no per-frame offset
            }
        }
        // fight.c 2080-2101: a dual attack-all actor stays at its home column instead (first
        // strike −8/−4, second strike −20/−12).
        var first = new Pal.Game.Dialogue.StoryPlayerAttackAllPresentation(new Vector2(200, 176),
            [new Vector2(100, 120)], [1], [80], [70], 2, dual: true, secondStrike: false);
        var second = new Pal.Game.Dialogue.StoryPlayerAttackAllPresentation(new Vector2(200, 176),
            [new Vector2(100, 120)], [1], [70], [60], 2, dual: true, secondStrike: true);
        for (int pose = 0; pose < 16; pose++)
        {
            AssertThat(first.At(pose, 25).PlayerPosition == new Vector2(192, 172)).IsTrue();
            AssertThat(second.At(pose, 25).PlayerPosition == new Vector2(180, 164)).IsTrue();
        }
    }

    [TestCase("effect")]
    [TestCase("idle")]
    [TestCase("number")]
    public void BaseFormatWithoutSelectedPresentationFailsBeforePartialMain(string missing)
    {
        if (missing == "idle")
        {
            // Per-slot idle speed is required author data, not an optional reference capability.
            // Formal Prepare rejects it during construction; the fixture cleans its failed output.
            InvalidDataException? rejected = null;
            try { using var unexpected = new AuthoredStoryFixture("Win", missing); }
            catch (InvalidDataException error) { rejected = error; }
            AssertThat(rejected is not null).IsTrue();
            string diagnostic = rejected!.Message;
            AssertThat(diagnostic.StartsWith(Path.Combine(Path.GetTempPath(), "pal-story-c-"), StringComparison.Ordinal)).IsTrue();
            AssertThat(diagnostic.Contains("story-package.json: visual.teams[0]: enemies[0]: metadata has missing, duplicate or unknown fields; missing: idleAnimSpeed", StringComparison.Ordinal)).IsTrue();
            return;
        }
        using var fixture = new AuthoredStoryFixture("Win", missing);
        var story = Pal.Game.Profiles.NativeStoryProfile.Load(fixture.StoryUri);
        var visual = Pal.Game.Profiles.NativeBattleVisualProfile.Load(fixture.VisualUri, story);
        AssertThat(visual.Teams.Count).IsEqual(1); // Format remains legal independently of selected capability.
        using var host = new StoryHost(GameSceneConfiguration.FromArguments("--story-profile", fixture.StoryUri,
            "--battle-visual-profile", fixture.VisualUri, "--battle-seed", "1"));
        AssertThat(host.Main.Failure!.Contains(missing == "effect" ? "attack effect" : "NumberSpriteUri")).IsTrue();
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
        using var fixture = new AuthoredStoryFixture("PartyWin", partyHpMpPrefixAmount: -3);
        var selection = GameSceneConfiguration.FromArguments("--story-profile", fixture.StoryUri,
            "--battle-visual-profile", fixture.VisualUri, "--story-language", "zh-Hans", "--battle-seed", "1");
        DynamicEventWorld? observedWorld = null;
        RoleLiveState? observedOwner = null;
        TriggerScriptSession? observedSession = null;
        IReadOnlyDictionary<int, BattleRoleSnapshot>? liveProjection = null;
        BattleRoleSnapshot[] initialRoles = [], prefixRoles = [], settledRoles = [];
        ClassicBattleLifecycleSession? observedBattle = null;
        ClassicBattleAction? enemyResponse = null;
        Guid observedCall = Guid.Empty, observedOrigin = Guid.Empty;
        bool searchDelivered = false, observedPrefix = false;
        Guid beforeCall = Guid.Empty;
        Guid? beforeSourceRequest = null, beforeInteractionRequest = null;
        TriggerScriptPhase beforePhase = TriggerScriptPhase.Ready;
        bool observedInitial = false, observedEntry = false, observedCommit = false, observedSuffix = false, observedReturn = false;
        void AssertInitialRole(BattleRoleSnapshot role, int roleId)
        {
            AssertThat(role.RoleId).IsEqual(roleId);
            AssertThat(role.Hp).IsEqual(roleId == 0 ? 100 : 95);
            AssertThat(role.Mp).IsEqual(20);
            AssertThat(role.MaxHp).IsEqual(150); AssertThat(role.MaxMp).IsEqual(50);
            AssertThat(role.Level).IsEqual(2); AssertThat(role.PrimaryExperience.Value).IsEqual(0);
            AssertThat(role.Equipment.SequenceEqual(new int[6])).IsTrue();
            AssertThat(role.Magics.SequenceEqual(new int[32])).IsTrue();
            AssertThat(role.Statuses.SequenceEqual(new int[9])).IsTrue();
            AssertThat(role.HiddenExperience.Values.All(track => track.Value == 0 && track.Level == 1 && track.Count == 0)).IsTrue();
        }
        void AssertPrefixRole(BattleRoleSnapshot role, int roleId)
        {
            AssertThat(role.Hp).IsEqual(roleId == 0 ? 97 : 92);
            AssertThat(role.Mp).IsEqual(17);
            AssertInitialRole(role with { Hp = roleId == 0 ? 100 : 95, Mp = 20 }, roleId);
        }
        void ObserveSharedOwner(Main main)
        {
            var world = main.Dialogue?.FirstBattleWorld
                ?? throw new InvalidOperationException("Story completion is missing its shared world.");
            var owner = world.RoleLiveState
                ?? throw new InvalidOperationException("Story completion is missing its live role owner.");
            var session = main.Dialogue!.Session!;
            if (!observedInitial)
            {
                observedInitial = true;
                observedWorld = world; observedOwner = owner; observedSession = session;
                liveProjection = session.BattleRoles;
                initialRoles = [owner.Roles[0], owner.Roles[1]];
                AssertThat(session.CallId).IsEqual(Guid.Empty);
                AssertThat(main.StoryBattle).IsNull();
                AssertInitialRole(initialRoles[0], 0); AssertInitialRole(initialRoles[1], 1);
            }
            // Search is queued before StartAfterSceneDraw actually starts the call. The first
            // completion with its new CallId is applicable regardless of phase or HP/MP shape.
            if (searchDelivered && !observedPrefix && session.CallId != beforeCall)
            {
                observedPrefix = true;
                observedCall = session.CallId;
                observedOrigin = main.Interaction!.PendingTrigger!.Id;
                prefixRoles = [owner.Roles[0], owner.Roles[1]];
                GD.Print($"PAL_STORY_PREFIX:beforeCall={beforeCall};beforeSource={beforeSourceRequest};"
                    + $"beforeRequest={beforeInteractionRequest};beforePhase={beforePhase};newCall={observedCall};"
                    + $"source={session.SourceRequestId};request={observedOrigin};phase={session.Phase};"
                    + $"hp={prefixRoles[0].Hp}/{prefixRoles[1].Hp};mp={prefixRoles[0].Mp}/{prefixRoles[1].Mp}");
                AssertPrefixRole(prefixRoles[0], 0); AssertPrefixRole(prefixRoles[1], 1);
                AssertThat(observedCall != Guid.Empty && observedOrigin != Guid.Empty).IsTrue();
                AssertThat(session.SourceRequestId).IsEqual(observedOrigin);
                AssertThat(session.Phase).IsEqual(TriggerScriptPhase.WaitingForDisplay);
                AssertThat(session.PendingDisplay!.Content.Parts[0].Text!).IsEqual("战");
                for (int roleId = 0; roleId < 2; roleId++)
                    AssertThat(ReferenceEquals(prefixRoles[roleId], initialRoles[roleId])).IsFalse();
            }
            // Numeric consumption precedes pending/lifecycle identity checks so a startup-value
            // consumer cannot be intercepted by an incidental request-reference mismatch.
            if (main.StoryBattle is { } entering && !observedEntry)
            {
                observedEntry = true;
                observedBattle = entering;
                AssertThat(entering.Current.Combat.Players.Select(p => p.Hp).SequenceEqual(new[] { 97, 92 }))
                    .OverrideFailureMessage("Story battle must consume prefix-current HP [97,92], not startup [100,95].").IsTrue();
                AssertPrefixRole(entering.Current.Roles[0], 0); AssertPrefixRole(entering.Current.Roles[1], 1);
                AssertThat(observedPrefix).IsTrue();
                AssertThat(ReferenceEquals(session.PendingBattle!.Lifecycle, entering)).IsTrue();
                AssertThat(entering.Current.Combat.Players.Select(p => p.RoleId).SequenceEqual(new[] { 0, 1 })).IsTrue();
                AssertThat(entering.Current.Combat.Players.Select(p => p.Attack).SequenceEqual(new[] { 40, 35 })).IsTrue();
                AssertThat(entering.Current.Combat.Players.Select(p => p.Defense).SequenceEqual(new[] { 20, 18 })).IsTrue();
                AssertThat(entering.Current.Combat.Players.All(p => p.Dexterity == 100)).IsTrue();
            }
            AssertThat(ReferenceEquals(world, observedWorld) && ReferenceEquals(owner, observedOwner)
                && ReferenceEquals(session, observedSession)).IsTrue();
            AssertThat(world.PartyRoles.SequenceEqual(new[] { 0, 1 }))
                .OverrideFailureMessage("Story shared-owner party order must be the authored [0,1].").IsTrue();
            AssertThat(owner.Roles.Count).IsEqual(2); // PartyWin declares no spectators.
            AssertThat(session.BattleRoles!.Count).IsEqual(2);
            AssertThat(ReferenceEquals(session.BattleRoles, liveProjection)).IsTrue();
            foreach (int roleId in new[] { 0, 1 })
            {
                AssertInitialRole(initialRoles[roleId], roleId); // Held history must never change.
                AssertThat(ReferenceEquals(liveProjection![roleId], owner.Roles[roleId])).IsTrue();
                var vitals = world.PartyVitals[roleId];
                var role = owner.Roles[roleId];
                AssertThat(vitals.Hp).IsEqual(role.Hp); AssertThat(vitals.Mp).IsEqual(role.Mp);
                AssertThat(vitals.MaxHp).IsEqual(role.MaxHp); AssertThat(vitals.MaxMp).IsEqual(role.MaxMp);
            }
            if (main.StoryBattle is { } lifecycle)
            {
                AssertThat(ReferenceEquals(lifecycle, observedBattle)).IsTrue();
                AssertThat(session.Phase).IsEqual(TriggerScriptPhase.WaitingForBattle);
                AssertThat(session.PendingDisplay).IsNull();
                // Neither combat damage nor intermediate settlement may publish before final ACK.
                for (int roleId = 0; roleId < 2; roleId++)
                {
                    AssertPrefixRole(owner.Roles[roleId], roleId);
                    AssertThat(ReferenceEquals(owner.Roles[roleId], prefixRoles[roleId])).IsTrue();
                }
                if (lifecycle.Current.Combat.LastAction is { Attacker: ClassicBattleSide.Enemy } response)
                    enemyResponse ??= response;
            }
            else if (observedEntry && !observedCommit)
            {
                observedCommit = true; // First publication after the owned final ACK, regardless of values.
                var receipt = observedBattle!.Completion!;
                AssertThat(observedBattle.OwnsCompletion(receipt)).IsTrue();
                AssertThat(receipt.Outcome).IsEqual(ClassicBattlePhase.Won);
                AssertThat(receipt.ExperienceGranted).IsEqual(0); AssertThat(receipt.CashGranted).IsEqual(0);
                AssertThat(session.PendingBattle).IsNull();
                AssertThat(session.PendingDisplay!.Content.Parts[0].Text!).IsEqual("胜");
                AssertThat(enemyResponse).IsNotNull();
                // Independent exhaustive bank: one enemy response, role0 damage20..23 or
                // role1 damage21..24 (fight.c), then half-missing recovery (battle.c1347-1350).
                // Select a legal branch by the action, never calculate expected from live HP.
                int[] expectedHp = enemyResponse switch
                {
                    { Evaded: true, Damage: 0, TargetIndex: 0 or 1 } => [123, 121],
                    { Evaded: false, TargetIndex: 0, Damage: 20 or 21 } => [113, 121],
                    { Evaded: false, TargetIndex: 0, Damage: 22 or 23 } => [112, 121],
                    { Evaded: false, TargetIndex: 1, Damage: 21 or 22 } => [123, 110],
                    { Evaded: false, TargetIndex: 1, Damage: 23 or 24 } => [123, 109],
                    _ => throw new InvalidOperationException("Story enemy response is outside the independent HP recovery table.")
                };
                settledRoles = [owner.Roles[0], owner.Roles[1]];
                for (int roleId = 0; roleId < 2; roleId++)
                {
                    var role = settledRoles[roleId];
                    AssertThat(role.Hp).IsEqual(expectedHp[roleId]);
                    AssertThat(role.Mp).IsEqual(33);
                    AssertThat(role.MaxHp).IsEqual(150); AssertThat(role.MaxMp).IsEqual(50);
                    AssertThat(role.Level).IsEqual(2); AssertThat(role.PrimaryExperience.Value).IsEqual(0);
                    AssertThat(ReferenceEquals(role, initialRoles[roleId])).IsFalse();
                    var settled = observedBattle.Current.Roles[roleId];
                    AssertThat(ReferenceEquals(role, settled)).IsFalse();
                    AssertThat(role.Hp).IsEqual(settled.Hp); AssertThat(role.Mp).IsEqual(settled.Mp);
                    AssertThat(role.RoleId).IsEqual(settled.RoleId);
                    AssertThat(role.Level).IsEqual(settled.Level);
                    AssertThat(role.MaxHp).IsEqual(settled.MaxHp); AssertThat(role.MaxMp).IsEqual(settled.MaxMp);
                    AssertThat(role.CoveredByRoleId).IsEqual(settled.CoveredByRoleId);
                    AssertThat(role.Attack).IsEqual(settled.Attack); AssertThat(role.MagicPower).IsEqual(settled.MagicPower);
                    AssertThat(role.Defense).IsEqual(settled.Defense); AssertThat(role.Dexterity).IsEqual(settled.Dexterity);
                    AssertThat(role.FleeRate).IsEqual(settled.FleeRate);
                    AssertThat(role.PrimaryExperience).IsEqual(settled.PrimaryExperience);
                    AssertThat(role.HiddenExperience.OrderBy(p => p.Key).SequenceEqual(settled.HiddenExperience.OrderBy(p => p.Key))).IsTrue();
                    AssertThat(role.Equipment.SequenceEqual(settled.Equipment) && role.Magics.SequenceEqual(settled.Magics)
                        && role.Statuses.SequenceEqual(settled.Statuses)).IsTrue();
                }
            }
            if (observedCommit)
            {
                AssertThat(session.CallId).IsEqual(observedCall);
                AssertThat(session.SourceRequestId).IsEqual(observedOrigin);
                for (int roleId = 0; roleId < 2; roleId++)
                    AssertThat(ReferenceEquals(owner.Roles[roleId], settledRoles[roleId])).IsTrue();
                if (!observedSuffix && session.Phase == TriggerScriptPhase.Completed)
                {
                    observedSuffix = true;
                    AssertThat(main.Interaction!.PendingTrigger).IsNull();
                    AssertThat(main.Interaction.Phase is NpcInteractionPhase.Returning or NpcInteractionPhase.Exploring).IsTrue();
                }
                if (observedSuffix && !observedReturn && main.Interaction!.Phase == NpcInteractionPhase.Exploring)
                    observedReturn = true;
            }
        }
        Main old;
        using (var host = new StoryHost(selection,
            enemySlots: [new(100, 120), new(128, 104)], players: [new(200, 176), new(256, 152)],
            updateObserver: ObserveSharedOwner))
        {
            old = host.Main;
            AssertThat(old.Failure).IsNull();
            await host.Wait(() => observedInitial);
            beforeCall = old.Dialogue!.Session!.CallId;
            beforeSourceRequest = old.Dialogue.Session.SourceRequestId;
            beforeInteractionRequest = old.Interaction!.PendingTrigger?.Id;
            beforePhase = old.Dialogue.Session.Phase;
            host.Tap(Key.Enter);
            searchDelivered = true;
            await host.Wait(() => observedPrefix && old.Dialogue!.Session!.PendingDisplay is not null);
            AssertThat(observedPrefix).IsTrue();
            var script = old.Dialogue!.Session!;
            Guid call = script.CallId;
            await host.FinishDisplay();
            await host.Wait(() => old.StoryBattleMode == StoryBattlePresentation.AttackMenu);
            var battle = old.StoryBattle!;
            var request = script.PendingBattle!;
            AssertThat(ReferenceEquals(old.StoryBattle, request.Lifecycle)).IsTrue();
            short leaderX = old.Exploration!.Leader.X, leaderY = old.Exploration!.Leader.Y;
            AssertThat(battle.Current.Combat.Players.Count).IsEqual(2);
            AssertThat(battle.Current.Combat.Enemies.Count).IsEqual(2);
            AssertThat(battle.Current.Combat.SelectingPlayerIndex is 0).IsTrue();
            // Member 0 confirms attack, then target slot 0; one physical Enter each.
            await host.ConfirmMenu(StoryBattlePresentation.TargetMenu);
            await host.ConfirmMenu(StoryBattlePresentation.AttackMenu);
            // The member-switch Enter selected member 0's target only; member 1 starts unselected.
            AssertThat(battle.Current.Combat.Phase).IsEqual(ClassicBattlePhase.Selecting);
            AssertThat(battle.Current.Combat.Players[0].SelectedEnemySlot).IsEqual(0);
            AssertThat(battle.Current.Combat.Players[1].SelectedEnemySlot).IsNull();
            AssertThat(battle.Current.Combat.SelectingPlayerIndex).IsEqual(1);
            await host.ConfirmMenu(StoryBattlePresentation.TargetMenu);
            // Cycle the highlighted target to slot 1, then confirm member 1's attack with one Enter.
            host.Tap(Key.Right);
            await host.ConfirmMenu(StoryBattlePresentation.Resolving);
            AssertThat(battle.Current.Combat.Players[1].SelectedEnemySlot).IsEqual(1);
            AssertThat(battle.Current.Combat.Phase).IsEqual(ClassicBattlePhase.Resolving);
            AssertThat(battle.Current.Combat.ActionQueue.Count).IsEqual(4);
            while (script.PendingBattle is not null)
            {
                if (old.StoryBattleMode == StoryBattlePresentation.AttackMenu)
                {
                    // If a 43hp slot survived the first hit, the next round selects again.
                    await host.ConfirmMenu(StoryBattlePresentation.TargetMenu);
                    await host.ConfirmMemberTarget(battle);
                }
                else
                {
                    await host.Wait(() => old.StoryBattleMode == StoryBattlePresentation.Feedback
                        || old.StoryBattleMode == StoryBattlePresentation.AttackMenu || script.PendingBattle is null);
                    if (script.PendingBattle is null) break;
                    if (old.StoryBattleMode == StoryBattlePresentation.Feedback)
                        await host.Wait(() => old.StoryBattleMode != StoryBattlePresentation.Feedback || script.PendingBattle is null);
                }
            }
            AssertThat(battle.Completion!.Outcome).IsEqual(ClassicBattlePhase.Won);
            AssertThat(script.CallId).IsEqual(call);
            // Distinct presented actions only; dead-enemy queue entries passed without
            // re-presenting the previous action. Seed-1 dice: member 1 acts first (33..38 on slot 1,
            // survives), member 0's 43+ hit kills slot 0, slot 1's enemy answers, and member 1
            // finishes slot 1 in round 2 (slot 0's dead enemy passed without any presentation).
            AssertThat(host.Actions.Count).IsEqual(4);
            AssertThat(host.Actions.Select(a => a.Attacker).SequenceEqual(new[]
                { ClassicBattleSide.Player, ClassicBattleSide.Player, ClassicBattleSide.Enemy, ClassicBattleSide.Player })).IsTrue();
            for (int i = 0; i < host.Actions.Count; i++)
                for (int j = i + 1; j < host.Actions.Count; j++)
                    AssertThat(ReferenceEquals(host.Actions[i], host.Actions[j])).IsFalse();
            GD.Print("PAL_PARTY_WIN_ACTIONS:" + string.Join(';', host.Actions.Select(a =>
                $"{a.Attacker}{a.ActorIndex}->{a.TargetIndex}={a.Damage}{(a.Evaded ? "E" : "")}"))
                + ";hp=" + string.Join('/', battle.Current.Combat.Players.Select(p => p.Hp)));
            foreach (var presented in host.Actions)
            {
                if (presented.Attacker == ClassicBattleSide.Player)
                {
                    // fight.c157-170 bounds: atk40 vs defense24 gives base42+1..2 (>=43),
                    // atk35 vs defense24 gives base32+1..2 (33..38); critical*3, role0 bonus*2,
                    // float*1..1.125 keeps one member hit in 33..297.
                    AssertThat(presented.Damage >= 33 && presented.Damage <= 297).IsTrue();
                }
                else
                {
                    // Enemy atk0/level0: strength 36..38 vs defense20 -> 20..23, vs defense18 -> 21..24,
                    // or a fully evaded action with damage 0.
                    AssertThat(presented.Evaded ? presented.Damage == 0 : presented.Damage is >= 20 and <= 24).IsTrue();
                }
            }
            AssertThat(host.Actions[0].Attacker == ClassicBattleSide.Player && host.Actions[0].ActorIndex == 1
                && host.Actions[0].TargetIndex == 1 && host.Actions[0].Damage is >= 33 and <= 38).IsTrue();
            AssertThat(host.Actions[1].Attacker == ClassicBattleSide.Player && host.Actions[1].ActorIndex == 0
                && host.Actions[1].TargetIndex == 0 && host.Actions[1].Damage > 42).IsTrue();
            AssertThat(host.Actions[2].Attacker == ClassicBattleSide.Enemy && host.Actions[2].ActorIndex == 1
                && host.Actions[2].TargetIndex is 0 or 1).IsTrue();
            AssertThat(host.Actions[3].Attacker == ClassicBattleSide.Player && host.Actions[3].ActorIndex == 1
                && host.Actions[3].TargetIndex == 1 && host.Actions[3].Damage is >= 33 and <= 38).IsTrue();
            // Members only lost HP to that one enemy action (0 or 20..24).
            int[] startHp = [97, 92];
            for (int i = 0; i < 2; i++)
            {
                int hp = battle.Current.Combat.Players[i].Hp;
                AssertThat(hp == startHp[i] || hp >= startHp[i] - 24 && hp < startHp[i]).IsTrue();
            }
            AssertThat(battle.Current.Combat.Enemies.Select(e => e.Hp).SequenceEqual(new[] { 0, 0 })).IsTrue();
            AssertThat(old.StoryBattleMode).IsNull();
            AssertThat(old.GetChildren().OfType<PalStoryBattleView>().Count()).IsEqual(0);
            AssertThat(old.GetChildren().OfType<Pal.Game.Resources.PalMapScene>().Single().Visible).IsTrue();
            AssertThat(script.PendingDisplay!.Content.Parts[0].Text!).IsEqual("胜");
            await host.FinishDisplay();
            await host.Wait(() => old.Interaction!.Phase == NpcInteractionPhase.Exploring);
            // The Right target cycle never moved the hidden exploration leader.
            AssertThat(old.Exploration!.Leader.X).IsEqual(leaderX);
            AssertThat(old.Exploration!.Leader.Y).IsEqual(leaderY);
            AssertThat(script.Phase).IsEqual(TriggerScriptPhase.Completed);
            AssertThat(observedInitial && observedPrefix && observedEntry && observedCommit && observedSuffix && observedReturn).IsTrue();
            AssertThat(old.Failure).IsNull();
        }
        AssertThat(GodotObject.IsInstanceValid(old)).IsFalse();
        AssertThat(Input.IsKeyPressed(Key.Enter)).IsFalse();
        AssertThat(Input.IsKeyPressed(Key.Right)).IsFalse();
        // Same-URI fresh Main: prepared resources are reused read-only, no session state leaks.
        bool observedFresh = false;
        void ObserveFreshOwner(Main main)
        {
            if (observedFresh) return;
            observedFresh = true;
            var world = main.Dialogue!.FirstBattleWorld!;
            var owner = world.RoleLiveState!;
            var session = main.Dialogue.Session!;
            AssertThat(ReferenceEquals(world, observedWorld) || ReferenceEquals(owner, observedOwner)
                || ReferenceEquals(session, observedSession)).IsFalse();
            AssertThat(world.PartyRoles.SequenceEqual(new[] { 0, 1 })).IsTrue();
            AssertThat(session.CallId).IsEqual(Guid.Empty);
            for (int roleId = 0; roleId < 2; roleId++)
            {
                AssertInitialRole(owner.Roles[roleId], roleId);
                AssertThat(ReferenceEquals(session.BattleRoles![roleId], owner.Roles[roleId])).IsTrue();
                AssertThat(ReferenceEquals(owner.Roles[roleId], initialRoles[roleId])
                    || ReferenceEquals(owner.Roles[roleId], settledRoles[roleId])).IsFalse();
                AssertThat(world.PartyVitals[roleId].Hp).IsEqual(roleId == 0 ? 100 : 95);
                AssertThat(world.PartyVitals[roleId].Mp).IsEqual(20);
            }
        }
        using (var fresh = new StoryHost(selection,
            enemySlots: [new(100, 120), new(128, 104)], players: [new(200, 176), new(256, 152)],
            updateObserver: ObserveFreshOwner))
        {
            await fresh.Wait(() => observedFresh);
            AssertThat(fresh.Main.Failure).IsNull();
            AssertThat(fresh.Main.Dialogue!.Session!.CallId).IsEqual(Guid.Empty);
            AssertThat(fresh.Main.StoryBattle).IsNull();
            fresh.Tap(Key.Enter);
            await fresh.Wait(() => fresh.Main.Dialogue!.Session!.PendingDisplay is not null);
            AssertThat(fresh.Main.Dialogue!.Session!.PendingDisplay!.Content.Parts[0].Text!).IsEqual("战");
        }
    }

    [TestCase(Timeout = 90000)]
    public async Task PartyThreePlayersPickTargetsByCyclingExecuteQueueAndWin()
    {
        // #137: three authored roles (dex100, hp100/95/95, atk40/35/35) act before two id7 slots
        // (dex0, speed18). fight.c157-170 bounds vs defense24: role0's atk40 hit is >=43 (kills a
        // 43hp slot), role1/role2 atk35 hits are 33..38 (crit up to 297), so slot1 survives role1's
        // strike and role2 finishes it: a one-round Won with three distinct member actions and no
        // enemy answer. The party draws at the reference g_rgPlayerPos[2] columns (battle.c:27-31).
        using var fixture = new AuthoredStoryFixture("PartyWin", threePlayer: true);
        var selection = GameSceneConfiguration.FromArguments("--story-profile", fixture.StoryUri,
            "--battle-visual-profile", fixture.VisualUri, "--story-language", "zh-Hans", "--battle-seed", "1");
        ClassicBattleLifecycleSession? observedBattle = null;
        bool observedParty = false;
        void ObserveThreePlayerOwner(Main main)
        {
            var world = main.Dialogue!.FirstBattleWorld!;
            AssertThat(world.PartyRoles.SequenceEqual(new[] { 0, 1, 2 }))
                .OverrideFailureMessage("Story shared-owner party order must be the authored [0,1,2].").IsTrue();
            if (!observedParty)
            {
                observedParty = true;
                AssertThat(world.RoleLiveState!.Roles.Count).IsEqual(3);
                AssertThat(world.RoleLiveState.Roles.Keys.Order().SequenceEqual(new[] { 0, 1, 2 })).IsTrue();
            }
            observedBattle ??= main.StoryBattle;
        }
        Main old;
        using (var host = new StoryHost(selection,
            enemySlots: [new(100, 120), new(128, 104)],
            players: [new(180, 180), new(234, 170), new(270, 146)],
            updateObserver: ObserveThreePlayerOwner))
        {
            old = host.Main;
            AssertThat(old.Failure).IsNull();
            host.Tap(Key.Enter);
            await host.Wait(() => old.Dialogue!.Session!.PendingDisplay is not null);
            await host.FinishDisplay();
            var script = old.Dialogue!.Session!;
            Guid call = script.CallId;
            await host.Wait(() => old.StoryBattleMode == StoryBattlePresentation.AttackMenu);
            var battle = old.StoryBattle!;
            AssertThat(ReferenceEquals(old.StoryBattle, script.PendingBattle!.Lifecycle)).IsTrue();
            AssertThat(battle.Current.Combat.Players.Count).IsEqual(3);
            AssertThat(battle.Current.Combat.Players.Select(p => p.RoleId).ToArray()).IsEqual(new[] { 0, 1, 2 });
            AssertThat(battle.Current.Combat.Players.Select(p => p.Hp).ToArray()).IsEqual(new[] { 100, 95, 95 });
            AssertThat(battle.Current.Combat.Enemies.Count).IsEqual(2);
            AssertThat(battle.Current.Combat.SelectingPlayerIndex).IsEqual(0);
            // Member 0 confirms attack, then the default target slot 0; one physical Enter each.
            await host.ConfirmMenu(StoryBattlePresentation.TargetMenu);
            await host.ConfirmMenu(StoryBattlePresentation.AttackMenu);
            AssertThat(battle.Current.Combat.SelectingPlayerIndex).IsEqual(1);
            AssertThat(battle.Current.Combat.Players[0].SelectedEnemySlot).IsEqual(0);
            AssertThat(battle.Current.Combat.Players[1].SelectedEnemySlot).IsNull();
            // Member 1 confirms attack, cycles the target highlight to slot 1, then confirms.
            await host.ConfirmMenu(StoryBattlePresentation.TargetMenu);
            host.Tap(Key.Right);
            await host.ConfirmMenu(StoryBattlePresentation.AttackMenu);
            AssertThat(battle.Current.Combat.SelectingPlayerIndex).IsEqual(2);
            AssertThat(battle.Current.Combat.Players[1].SelectedEnemySlot).IsEqual(1);
            AssertThat(battle.Current.Combat.Players[2].SelectedEnemySlot).IsNull();
            // Member 2 confirms attack, cycles to slot 1 too; its confirm builds the queue.
            await host.ConfirmMenu(StoryBattlePresentation.TargetMenu);
            host.Tap(Key.Right);
            await host.ConfirmMenu(StoryBattlePresentation.Resolving);
            AssertThat(battle.Current.Combat.Phase).IsEqual(ClassicBattlePhase.Resolving);
            AssertThat(battle.Current.Combat.SelectingPlayerIndex).IsNull();
            AssertThat(battle.Current.Combat.Players.Select(p => p.SelectedEnemySlot).ToArray()).IsEqual(new int?[] { 0, 1, 1 });
            AssertThat(battle.Current.Combat.ActionQueue.Count).IsEqual(5);
            while (script.PendingBattle is not null)
            {
                await host.Wait(() => old.StoryBattleMode == StoryBattlePresentation.Feedback
                    || script.PendingBattle is null);
                if (script.PendingBattle is null) break;
                await host.Wait(() => old.StoryBattleMode != StoryBattlePresentation.Feedback
                    || script.PendingBattle is null);
            }
            AssertThat(battle.Completion!.Outcome).IsEqual(ClassicBattlePhase.Won);
            AssertThat(script.CallId).IsEqual(call);
            // Distinct presented player actions only; a possible atk35 critical ends the battle
            // before the last member's queue entry runs, so the count is 2 or 3.
            AssertThat(host.Actions.All(a => a.Attacker == ClassicBattleSide.Player)).IsTrue();
            AssertThat(host.Actions.Count is 2 or 3).IsTrue();
            for (int i = 0; i < host.Actions.Count; i++)
                for (int j = i + 1; j < host.Actions.Count; j++)
                    AssertThat(ReferenceEquals(host.Actions[i], host.Actions[j])).IsFalse();
            // Actions resolve by party index; the actors are distinct and map to RoleId 0,1,2.
            int[] actingRoles = host.Actions.Select(a => battle.Current.Combat.Players[a.ActorIndex].RoleId).ToArray();
            AssertThat(actingRoles.Distinct().Count() == actingRoles.Length && actingRoles.All(r => r is 0 or 1 or 2)).IsTrue();
            if (host.Actions.Count == 2) AssertThat(actingRoles.Order().SequenceEqual(new[] { 0, 1 })).IsTrue();
            foreach (var presented in host.Actions)
            {
                int role = battle.Current.Combat.Players[presented.ActorIndex].RoleId;
                // fight.c157-170: atk40 vs defense24 gives >=43 (a 43hp slot dies to it); atk35
                // gives 33..38 with critical*3 and float*1..1.125 keeping every hit in 33..297.
                AssertThat(presented.Damage >= 33 && presented.Damage <= 297).IsTrue();
                if (role == 0) AssertThat(presented.Damage >= 43 && presented.TargetIndex == 0).IsTrue();
                else AssertThat(presented.TargetIndex is 0 or 1).IsTrue();
                AssertThat(battle.Current.Combat.Players[presented.ActorIndex].Counts.Attack).IsEqual(1);
            }
            foreach (int actor in new[] { 0, 1, 2 })
            {
                // A member either presented exactly one action or never reached its queue entry.
                int expected = host.Actions.Any(a => a.ActorIndex == actor) ? 1 : 0;
                AssertThat(battle.Current.Combat.Players[actor].Counts.Attack).IsEqual(expected);
            }
            // No enemy answered: the members keep their authored HP words.
            AssertThat(battle.Current.Combat.Players.Select(p => p.Hp).ToArray()).IsEqual(new[] { 100, 95, 95 });
            AssertThat(battle.Current.Combat.Enemies.Select(e => e.Hp).ToArray()).IsEqual(new[] { 0, 0 });
            // Victory half-recovery settles per RoleId, not per party position.
            AssertThat(battle.Current.Roles[0].Hp).IsEqual(125);
            AssertThat(battle.Current.Roles[1].Hp).IsEqual(122);
            AssertThat(battle.Current.Roles[2].Hp).IsEqual(122);
            AssertThat(battle.Current.Roles[0].Mp).IsEqual(35);
            AssertThat(battle.Current.Roles[1].Mp).IsEqual(35);
            AssertThat(battle.Current.Roles[2].Mp).IsEqual(35);
            AssertThat(observedParty).IsTrue();
            AssertThat(ReferenceEquals(observedBattle, battle)).IsTrue();
            await host.FinishDisplay();
            await host.Wait(() => old.Interaction!.Phase == NpcInteractionPhase.Exploring);
            AssertThat(old.StoryBattleMode).IsNull();
            AssertThat(old.GetChildren().OfType<PalStoryBattleView>().Count()).IsEqual(0);
            AssertThat(old.GetChildren().OfType<Pal.Game.Resources.PalMapScene>().Single().Visible).IsTrue();
            AssertThat(script.Phase).IsEqual(TriggerScriptPhase.Completed);
            AssertThat(old.Failure).IsNull();
        }
        AssertThat(GodotObject.IsInstanceValid(old)).IsFalse();
        AssertThat(Input.IsKeyPressed(Key.Enter)).IsFalse();
        AssertThat(Input.IsKeyPressed(Key.Right)).IsFalse();
    }

    [TestCase(Timeout = 120000)]
    public async Task BossFleeFailsStaysInBattleAndLostRunEndsAtTheReloadTerminal()
    {
        // LostReload with playerHp150: the enemy slot (dex500) strikes first (~120 damage,
        // hp150 -> 30ish), then the committed boss flee (Q, fight.c 4119-4173) fails while still
        // consuming its RandomLong(0, 524) draw; the menu returns, so the failed escape really
        // stayed in battle. Round 2's attack cannot clear the 100hp slot and the next strike
        // kills the member: an actual Lost, never an injected one.
        using var fixture = new AuthoredStoryFixture("LostReload", playerHp: 150);
        var selection = GameSceneConfiguration.FromArguments("--story-profile", fixture.StoryUri,
            "--battle-visual-profile", fixture.VisualUri, "--story-language", "zh-Hans", "--battle-seed", "1");
        Main old;
        using (var host = new StoryHost(selection))
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
            AssertThat(battle.Completion).IsNull();
            // input.c kKeyFlee: the boss flee attempt from the action menu. The enemy slot
            // (dex500) resolves first, so the escape-fail feedback follows the enemy strike.
            host.Tap(Key.Q);
            await host.Wait(() => battle.Current.Combat.LastFleeAttempt is not null);
            bool escapeFailLabel = false;
            await host.Wait(() =>
            {
                escapeFailLabel |= old.StoryDrawSnapshot?.FeedbackWord == Pal.Game.Dialogue.PalStoryBattleView.EscapeFailWord;
                return escapeFailLabel;
            });
            await host.Wait(() => old.StoryBattleMode != StoryBattlePresentation.Feedback);
            AssertThat(escapeFailLabel).IsTrue();
            var flee = battle.Current.Combat.LastFleeAttempt;
            AssertThat(flee).IsNotNull();
            AssertThat(flee!.Escaped).IsFalse();
            AssertThat(flee.EnemyFleeDefense).IsEqual(524); // dex500 + (level0+6)*4, fight.c 4128-4137
            AssertThat(battle.Current.Combat.Players[0].Counts.Flee).IsEqual(2);
            // The failed escape spent the member's action; the round ended and selection returned.
            await host.Wait(() => old.StoryBattleMode == StoryBattlePresentation.AttackMenu
                || battle.Completion is not null);
            AssertThat(battle.Completion).IsNull();
            AssertThat(old.StoryBattleMode).IsEqual(StoryBattlePresentation.AttackMenu);
            AssertThat(old.StoryCashGranted == 0 && old.StoryExperienceGranted == 0).IsTrue();
            while (script.PendingBattle is not null)
            {
                if (old.StoryBattleMode == StoryBattlePresentation.AttackMenu)
                {
                    await host.ConfirmMenu(StoryBattlePresentation.TargetMenu);
                    await host.ConfirmMemberTarget(battle);
                }
                else await host.Wait(() => old.StoryBattleMode != StoryBattlePresentation.Feedback
                    || script.PendingBattle is null);
            }
            AssertThat(battle.Completion!.Outcome).IsEqual(ClassicBattlePhase.Lost);
            AssertThat(battle.Current.Combat.Players.All(p => p.Hp == 0)).IsTrue();
            // fight.c 4171: the failed flee's hidden flee count survives into the settled roles.
            AssertThat(battle.Current.Roles[0].HiddenExperience["flee"].Count).IsEqual(2);
            // The failure branch reconnects through the loss target as a centered dialogue.
            await host.Wait(() => script.PendingDisplay is not null);
            AssertThat(script.PendingDisplay!.Context.Box).IsEqual("centerWindow");
            AssertThat(old.Display!.DisplayBox).IsEqual("centerWindow");
            AssertThat(script.PendingDisplay!.Content.Parts[0].Text!).IsEqual("大败");
            await host.FinishDisplay();
            // setMusic/fadeToRed/reload park the call: the terminal prints one diagnostic and
            // never completes, rewards or returns to exploration.
            await host.Wait(() => old.StoryLostTerminal);
            AssertThat(script.Phase).IsEqual(TriggerScriptPhase.WaitingForLostReload);
            AssertThat(script.PendingLostReload!.Position.SourceAddress).IsEqual(11);
            AssertThat(script.PendingLostReload.EnemyTeamId).IsEqual(18);
            AssertThat(script.PendingLostReload.IsBoss).IsTrue();
            AssertThat(script.Music).IsEqual(new TriggerMusicState(0, 0));
            AssertThat(script.FadedToRed).IsTrue();
            AssertThat(old.StoryLostReloadDiagnostics).IsEqual(1);
            AssertThat(old.StoryCashGranted == 0 && old.StoryExperienceGranted == 0).IsTrue();
            AssertThat(old.Interaction!.Phase).IsEqual(NpcInteractionPhase.Trigger);
            AssertThat(old.StoryBattleMode).IsNull();
            AssertThat(old.GetChildren().OfType<Pal.Game.Dialogue.PalStoryBattleView>().Count()).IsEqual(0);
            // The red fade converges (32 steps of 75ms) with the text palette preserved.
            await host.Wait(() => old.StoryRedFadeStep >= 32);
            var red = old.StoryRedPalette!;
            AssertThat(red.Length).IsEqual(256);
            AssertThat(red[0x4F].G8 > 0).IsTrue(); // palette.c preserves 0x4F for readable text
            AssertThat(red.Count(color => color.G8 == 0 && color.B8 == 0) > 200).IsTrue();
            int leaderX = old.Exploration!.Leader.X, leaderY = old.Exploration.Leader.Y;
            int updatesAtTerminal = 0;
            old.UpdateCompleted += () => updatesAtTerminal++;
            for (int i = 0; i < 3; i++) { host.Tap(Key.Enter); host.Tap(Key.Q); }
            await host.Wait(() => updatesAtTerminal >= 5);
            AssertThat(old.StoryLostReloadDiagnostics).IsEqual(1);
            AssertThat(script.Phase).IsEqual(TriggerScriptPhase.WaitingForLostReload);
            AssertThat(old.Interaction!.Phase).IsEqual(NpcInteractionPhase.Trigger);
            AssertThat(old.Exploration!.Leader.X == leaderX && old.Exploration.Leader.Y == leaderY).IsTrue();
            AssertThat(old.Failure).IsNull();
            AssertThat(script.CallId).IsEqual(call);
        }
        AssertThat(GodotObject.IsInstanceValid(old)).IsFalse();
        AssertThat(Input.IsKeyPressed(Key.Enter)).IsFalse();
        AssertThat(Input.IsKeyPressed(Key.Q)).IsFalse();
        // Same-URI fresh Main: the terminal poisoned nothing; a new interaction starts normally.
        using (var fresh = new StoryHost(selection))
        {
            AssertThat(fresh.Main.Failure).IsNull();
            AssertThat(fresh.Main.Interaction!.Phase).IsEqual(NpcInteractionPhase.Exploring);
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
                    AssertThat(selecting.HasValue).IsTrue();
                    // Once a member died, only the living member selects and no dead member acts again.
                    if (deadMember >= 0)
                    {
                        AssertThat(selecting!.Value != deadMember).IsTrue();
                        AssertThat(battle.Current.Combat.Players[deadMember].Hp).IsEqual(0);
                    }
                    while (old.StoryBattleMode == StoryBattlePresentation.AttackMenu)
                    {
                        // Each alive member confirms attack then target; the final member's
                        // target confirm builds the queue and enters Resolving.
                        await host.ConfirmMenu(StoryBattlePresentation.TargetMenu);
                        await host.ConfirmMemberTarget(battle);
                    }
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

    [TestCase(Timeout = 120000)]
    public async Task ZeroHpPuppetMemberEntersAtOneHpStaysDeadAndSettlesByIdentity()
    {
        // #139: a PartyLoss-derived author gives member 0 a dead entry carrying the puppet word
        // (kStatusPuppet, statuses slot 4). PAL_StartBattle's entry loop (battle.c 1568-1583)
        // revives it at exactly 1 HP and clears the puppet at the real battle start while the
        // shared owner keeps the authored 0 HP until the owned ack; the dex500 enemies then kill
        // it for real and the member stays dead through every later round (never revived per
        // action) until the Lost settlement returns its true 0 HP, zero statuses and identity.
        using var fixture = new AuthoredStoryFixture("PartyLoss", zeroHpPuppetEntry: true);
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
            // Entry normalization: authored 0 HP + puppet becomes exactly 1 HP with the puppet cleared.
            AssertThat(battle.Current.Combat.Players[0].Hp).IsEqual(1);
            AssertThat(battle.Current.Roles[0].Hp).IsEqual(1);
            AssertThat(battle.Current.Roles[0].Statuses.All(value => value == 0)).IsTrue();
            AssertThat(battle.Current.Roles[1].Hp).IsEqual(20);
            var world = old.Dialogue!.FirstBattleWorld!;
            AssertThat(world.RoleLiveState!.Roles[0].Hp).IsEqual(0);
            AssertThat(world.RoleLiveState.Roles[0].Statuses[4]).IsEqual(1);
            bool member0Dead = false;
            while (script.PendingBattle is not null)
            {
                if (old.StoryBattleMode == StoryBattlePresentation.AttackMenu)
                {
                    var selecting = battle.Current.Combat.SelectingPlayerIndex;
                    AssertThat(selecting.HasValue).IsTrue();
                    if (member0Dead)
                    {
                        AssertThat(selecting!.Value != 0).IsTrue();
                        AssertThat(battle.Current.Combat.Players[0].Hp).IsEqual(0);
                    }
                    while (old.StoryBattleMode == StoryBattlePresentation.AttackMenu)
                    {
                        await host.ConfirmMenu(StoryBattlePresentation.TargetMenu);
                        await host.ConfirmMemberTarget(battle);
                    }
                }
                await host.Wait(() => old.StoryBattleMode == StoryBattlePresentation.Feedback
                    || old.StoryBattleMode == StoryBattlePresentation.AttackMenu || script.PendingBattle is null);
                if (battle.Current.Combat.Players[0].Hp == 0) member0Dead = true;
                if (script.PendingBattle is null) break;
                await host.Wait(() => old.StoryBattleMode != StoryBattlePresentation.Feedback || script.PendingBattle is null);
            }
            AssertThat(battle.Completion!.Outcome).IsEqual(ClassicBattlePhase.Lost);
            AssertThat(member0Dead).IsTrue();
            AssertThat(battle.Current.Combat.Players.All(p => p.Hp == 0)).IsTrue();
            // In-battle death identity: the settled entry member keeps 0 HP (no revive-to-1), zero
            // statuses and its own RoleId; the Lost settlement performs no recovery.
            AssertThat(battle.Current.Roles[0].Hp).IsEqual(0);
            AssertThat(battle.Current.Roles[0].Statuses.All(value => value == 0)).IsTrue();
            AssertThat(battle.Current.Roles[0].RoleId).IsEqual(0);
            AssertThat(battle.Current.Roles[1].Hp).IsEqual(0);
            AssertThat(script.CallId).IsEqual(call);
            AssertThat(script.PendingDisplay!.Content.Parts[0].Text!).IsEqual("败");
            await host.FinishDisplay();
            await host.Wait(() => old.Interaction!.Phase == NpcInteractionPhase.Exploring);
            AssertThat(script.Phase).IsEqual(TriggerScriptPhase.Completed);
            // The owned ack merges the settled roles: member 0 stays dead in the shared owner too.
            AssertThat(world.RoleLiveState.Roles[0].Hp).IsEqual(0);
            AssertThat(world.RoleLiveState.Roles[0].Statuses.All(value => value == 0)).IsTrue();
            AssertThat(old.StoryBattleMode).IsNull();
            AssertThat(old.Failure).IsNull();
        }
        AssertThat(GodotObject.IsInstanceValid(old)).IsFalse();
        AssertThat(Input.IsKeyPressed(Key.Enter)).IsFalse();
    }

    [TestCase(Timeout = 120000)]
    public async Task EquipmentDualAttackPresentsTwoStrikesWithOncePerActionCounts()
    {
        // PartyEquip: role1 wears the authored five-object closure (effective 46/52/119, object
        // 170's persistent dual attack) and starts dying at hp25 covered by role0; the two fast
        // enemy slots strike first, so a real cover may interpose before both members win.
        using var fixture = new AuthoredStoryFixture("PartyEquip");
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
            AssertThat(battle.Current.Combat.Players.Count).IsEqual(2);
            while (script.PendingBattle is not null)
            {
                if (old.StoryBattleMode == StoryBattlePresentation.AttackMenu)
                {
                    await host.ConfirmMenu(StoryBattlePresentation.TargetMenu);
                    await host.ConfirmMemberTarget(battle);
                }
                else
                {
                    await host.Wait(() => old.StoryBattleMode == StoryBattlePresentation.Feedback
                        || old.StoryBattleMode == StoryBattlePresentation.AttackMenu || script.PendingBattle is null);
                    if (script.PendingBattle is null) break;
                    if (old.StoryBattleMode == StoryBattlePresentation.Feedback)
                        await host.Wait(() => old.StoryBattleMode != StoryBattlePresentation.Feedback || script.PendingBattle is null);
                }
            }
            AssertThat(battle.Completion!.Outcome).IsEqual(ClassicBattlePhase.Won);
            AssertThat(script.CallId).IsEqual(call);
            AssertThat(battle.Current.Combat.Enemies.Select(e => e.Hp).SequenceEqual(new[] { 0, 0 })).IsTrue();
            var memberActions = host.Actions.Where(a => a.Attacker == Pal.Core.ClassicBattleSide.Player).ToArray();
            // Role1's dual attack presents as two distinct action records in one queue action:
            // same target, first/second strike flags, each with its own feedback presentation.
            var strikes = memberActions.Where(a => a.ActorIndex == 1).ToArray();
            AssertThat(strikes.Length).IsEqual(2);
            AssertThat(strikes[0].SecondStrike).IsFalse();
            AssertThat(strikes[1].SecondStrike).IsTrue();
            AssertThat(strikes[0].TargetIndex).IsEqual(strikes[1].TargetIndex);
            AssertThat(ReferenceEquals(strikes[0], strikes[1])).IsFalse();
            // fight.c157-170 bounds with the effective panel: atk46 vs defense24 gives base
            // 54+1..2 (55..56, x3 critical 165..189), so either strike kills a 43hp slot.
            foreach (var strike in strikes)
                AssertThat(strike.Damage >= 55 && strike.Damage <= 189).IsTrue();
            // The clean member still kills its slot alone (atk40: 43..49 or critical 129..148).
            var clean = memberActions.Single(a => a.ActorIndex == 0);
            AssertThat(clean.Damage is >= 43 and <= 148).IsTrue();
            AssertThat(host.Actions.Select(a => a.Attacker).Count(a => a == Pal.Core.ClassicBattleSide.Enemy)).IsEqual(2);
            foreach (var enemy in host.Actions.Where(a => a.Attacker == Pal.Core.ClassicBattleSide.Enemy))
            {
                // Enemy strength 36..38 vs defense52 gives 2..4 on member1, vs defense20 20..23
                // on member0; a covered strike deals zero damage without the evade flag (fight.c:4959).
                if (enemy.CovererPartyIndex is not null)
                {
                    AssertThat(enemy.Damage).IsEqual(0);
                    AssertThat(enemy.Evaded).IsFalse();
                    AssertThat(enemy.CovererPartyIndex).IsEqual(0);
                    AssertThat(enemy.TargetIndex).IsEqual(1);
                }
                else if (enemy.Evaded) AssertThat(enemy.Damage).IsEqual(0);
                else AssertThat(enemy.TargetIndex == 0 ? enemy.Damage is >= 20 and <= 23 : enemy.Damage is >= 2 and <= 4).IsTrue();
            }
            // One dual action counts once; the health weight is the only remaining random 2..3.
            AssertThat(battle.Current.Combat.Players[1].Counts.Attack).IsEqual(1);
            AssertThat(battle.Current.Combat.Players[1].Counts.Health is 2 or 3).IsTrue();
            AssertThat(battle.Current.Combat.Players[0].Counts.Attack).IsEqual(1);
            // Both strikes were presented as their own feedback cycles at distinct updates.
            var strikeStarts = host.ActionStarts.Where(c => c.Action is { Attacker: Pal.Core.ClassicBattleSide.Player, ActorIndex: 1 }).ToArray();
            AssertThat(strikeStarts.Length).IsEqual(2);
            AssertThat(strikeStarts[0].Update != strikeStarts[1].Update).IsTrue();
            AssertThat(script.PendingDisplay!.Content.Parts[0].Text!).IsEqual("胜");
            await host.FinishDisplay();
            await host.Wait(() => old.Interaction!.Phase == NpcInteractionPhase.Exploring);
            AssertThat(script.Phase).IsEqual(TriggerScriptPhase.Completed);
            AssertThat(old.Failure).IsNull();
        }
        AssertThat(GodotObject.IsInstanceValid(old)).IsFalse();
        // Same-URI fresh Main: the prepared equipment resources are reused read-only with no
        // session state leaking into a new story battle (no stacked dual or status pollution).
        using (var fresh = new StoryHost(selection,
            enemySlots: [new(100, 120), new(128, 104)], players: [new(200, 176), new(256, 152)]))
        {
            AssertThat(fresh.Main.Failure).IsNull();
            AssertThat(fresh.Main.StoryBattle).IsNull();
            fresh.Tap(Key.Enter);
            await fresh.Wait(() => fresh.Main.Dialogue!.Session!.PendingDisplay is not null);
            await fresh.FinishDisplay();
            await fresh.Wait(() => fresh.Main.StoryBattleMode == StoryBattlePresentation.AttackMenu);
            var again = fresh.Main.StoryBattle!;
            AssertThat(again.Current.Roles[1].Statuses[8]).IsEqual(60000);
            AssertThat(again.Current.Combat.Players[1].Dexterity).IsEqual(119);
        }
    }

    [TestCase(Timeout = 120000)]
    public async Task RemovingTheDualAttackEquipmentPresentsExactlyOneStrikePerAction()
    {
        // Targeted counterexample: the same authored scenario with object 170 unworn must not
        // produce a second strike; only the dual-attack status drives the two-strike loop.
        using var fixture = new AuthoredStoryFixture("PartyEquip", removeDualAttack: true);
        var selection = GameSceneConfiguration.FromArguments("--story-profile", fixture.StoryUri,
            "--battle-visual-profile", fixture.VisualUri, "--story-language", "zh-Hans", "--battle-seed", "1");
        using var host = new StoryHost(selection,
            enemySlots: [new(100, 120), new(128, 104)], players: [new(200, 176), new(256, 152)]);
        AssertThat(host.Main.Failure).IsNull();
        host.Tap(Key.Enter);
        await host.Wait(() => host.Main.Dialogue!.Session!.PendingDisplay is not null);
        var script = host.Main.Dialogue!.Session!;
        await host.FinishDisplay();
        await host.Wait(() => host.Main.StoryBattleMode == StoryBattlePresentation.AttackMenu);
        var battle = host.Main.StoryBattle!;
        while (script.PendingBattle is not null)
        {
            if (host.Main.StoryBattleMode == StoryBattlePresentation.AttackMenu)
            {
                await host.ConfirmMenu(StoryBattlePresentation.TargetMenu);
                await host.ConfirmMemberTarget(battle);
            }
            else
            {
                await host.Wait(() => host.Main.StoryBattleMode == StoryBattlePresentation.Feedback
                    || host.Main.StoryBattleMode == StoryBattlePresentation.AttackMenu || script.PendingBattle is null);
                if (script.PendingBattle is null) break;
                if (host.Main.StoryBattleMode == StoryBattlePresentation.Feedback)
                    await host.Wait(() => host.Main.StoryBattleMode != StoryBattlePresentation.Feedback || script.PendingBattle is null);
            }
        }
        AssertThat(battle.Completion!.Outcome).IsEqual(ClassicBattlePhase.Won);
        AssertThat(host.Actions.Any(a => a.SecondStrike)).IsFalse();
        AssertThat(host.Actions.Count(a => a.Attacker == Pal.Core.ClassicBattleSide.Player && a.ActorIndex == 1)).IsEqual(1);
        // The panel projection still consumes the remaining worn objects (dex 100+14=114).
        AssertThat(battle.Current.Combat.Players[1].Dexterity).IsEqual(114);
        AssertThat(battle.Current.Roles[1].Statuses[8]).IsEqual(0);
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

    [TestCase(Timeout = 20000)]
    public async Task FailedStoryPreloadPublishesNoPartialScene()
    {
        var missing = GameSceneConfiguration.FromArguments("--story-profile", "res://Assets/Missing-Story.tres",
            "--battle-visual-profile", "res://Assets/Missing-Visual.tres");
        using (var host = new StoryHost(missing))
        {
            AssertThat(host.Main.Failure).IsNotNull();
            AssertThat(host.Main.GetChildCount()).IsEqual(0);
            AssertThat(host.Main.Exploration).IsNull();
        }

        using var fixture = new AuthoredStoryFixture("PartyWin", heterogeneousEnemies: true);
        string[] before = fixture.Snapshot();
        var story = Pal.Game.Profiles.NativeStoryProfile.Load(fixture.StoryUri);
        var visual = Pal.Game.Profiles.NativeBattleVisualProfile.Load(fixture.VisualUri, story);
        AssertThat(visual.Teams[18].EnemySlots.Select(s => s.EnemySprite!.SpriteId)
            .SequenceEqual(new[] { 97, 98 })).IsTrue();
        AssertThat(visual.Teams[18].EnemySlots.Select(s => s.EnemySprite!.FrameCount)
            .SequenceEqual(new[] { 3, 4 })).IsTrue();
        var selection = GameSceneConfiguration.FromArguments("--story-profile", fixture.StoryUri,
            "--battle-visual-profile", fixture.VisualUri, "--battle-seed", "1");
        const string marker = "#136 owner-registered enemy slot1 texture failure";
        var captured = new List<(int? Slot, ImageTexture Texture, ulong Id, bool InitiallyValid)>();
        PalStoryBattleView? partial = null;
        bool offTree = false, failedViewValid = true;
        int referencesAtFailure = -1, partialChildrenAtFailure = -1, failures = 0;
        bool[] releasedAtFailure = [];
        Main failedMain;
        try
        {
            using (var host = new StoryHost(selection, configure: main =>
            {
                main.StoryBattleViewFactory = () => partial = new PalStoryBattleView
                {
                    TextureOwned = (slot, texture) =>
                    {
                        captured.Add((slot, texture, texture.GetInstanceId(), GodotObject.IsInstanceValid(texture)));
                        if (slot != 1) return;
                        offTree = !partial!.IsInsideTree() && partial.GetParent() is null;
                        throw new InvalidOperationException(marker);
                    }
                };
                main.Failed += _ =>
                {
                    failures++;
                    // Snapshot at the production failure boundary, before test cleanup.
                    releasedAtFailure = captured.Select(item => !GodotObject.IsInstanceValid(item.Texture)
                        && GodotObject.InstanceFromId(item.Id) is null).ToArray();
                    referencesAtFailure = partial!.TextureReferenceCount;
                    failedViewValid = GodotObject.IsInstanceValid(partial);
                    partialChildrenAtFailure = main.GetChildren().OfType<PalStoryBattleView>().Count();
                };
            }))
            {
                failedMain = host.Main;
                AssertThat(host.Main.Failure).IsNull();
                host.Tap(Key.Enter);
                await host.Wait(() => host.Main.Dialogue!.Session!.PendingDisplay is not null);
                var display = host.Main.Dialogue!.Session!.PendingDisplay!.Display;
                await host.Wait(() => display.Current.Phase == DialogueDisplayPhase.WaitingForAdvance);
                host.Tap(Key.Enter);
                await host.WaitForFailure();
                GD.Print("PAL_136_PARTIAL_FAILURE:" + JsonSerializer.Serialize(new
                {
                    offTree, failures, referencesAtFailure, failedViewValid, partialChildrenAtFailure,
                    textures = captured.Select((item, i) => new { item.Slot, item.Id, item.InitiallyValid, released = releasedAtFailure[i] }),
                    error = host.Main.Failure
                }));
                AssertThat(captured.All(item => item.InitiallyValid && item.Id != 0)).IsTrue();
                AssertThat(captured.Count(item => item.Slot == 0)).IsEqual(6); // three frames, two shifts
                AssertThat(captured.Count(item => item.Slot == 1)).IsEqual(1);
                AssertThat(captured.Where(item => item.Slot is not null).Select(item => item.Slot!.Value)
                    .SequenceEqual(new[] { 0, 0, 0, 0, 0, 0, 1 })).IsTrue();
                AssertThat(offTree).IsTrue();
                AssertThat(failures).IsEqual(1);
                AssertThat(host.Main.Failure!.Contains(marker, StringComparison.Ordinal)).IsTrue();
                AssertThat(releasedAtFailure.Length).IsEqual(captured.Count);
                AssertThat(releasedAtFailure.All(released => released))
                    .OverrideFailureMessage("Every owner-registered native texture must be released before Main.Failed.").IsTrue();
                AssertThat(referencesAtFailure).IsEqual(0);
                AssertThat(failedViewValid).IsFalse();
                AssertThat(partialChildrenAtFailure).IsEqual(0);
                AssertThat(host.Main.StoryBattle).IsNull();
                AssertThat(host.Main.StoryDrawSnapshot.HasValue).IsFalse();
                partial!.ReleaseTextures(); // repeated cleanup is safe even after off-tree Free
                AssertThat(partial.TextureReferenceCount).IsEqual(0);
            }
            AssertThat(GodotObject.IsInstanceValid(failedMain)).IsFalse();
        }
        finally
        {
            // A genuine RED must not leave task-owned ObjectDB entries in the shared host.
            int rescued = 0;
            foreach (var item in captured)
                if (GodotObject.IsInstanceValid(item.Texture)) { item.Texture.Dispose(); rescued++; }
            if (partial is not null && GodotObject.IsInstanceValid(partial)) partial.Free();
            GD.Print($"PAL_136_PARTIAL_RESCUE:{rescued}");
        }
        AssertThat(fixture.Snapshot().SequenceEqual(before)).IsTrue();

        bool first = false;
        var healthyTextures = new List<(ImageTexture Texture, ulong Id)>();
        Main healthyMain;
        using (var fresh = new StoryHost(selection, [new(100, 120), new(128, 104)], [new(200, 176), new(256, 152)],
            main =>
            {
                if (first || main.StoryBattle is null) return;
                first = true; // first real UpdateCompleted, never wait for a matching state
                var draw = main.StoryDrawSnapshot!.Value;
                AssertThat(main.StoryBattleMode).IsEqual(StoryBattlePresentation.AttackMenu);
                AssertThat(draw.EnemyIdleFrames.SequenceEqual(new[] { 0, 0 })).IsTrue();
                AssertThat(draw.EnemyIdleCounters.SequenceEqual(new[] { 65534, 1 })).IsTrue();
                AssertThat(draw.PartyEnemies.Select(e => e.Position)
                    .SequenceEqual(new[] { new Vector2(100, 120), new Vector2(128, 104) })).IsTrue();
                AssertThat(draw.PartyEnemies.Select(e => e.Hp).SequenceEqual(new[] { 43, 43 })).IsTrue();
                AssertThat(draw.PartyEnemies.All(e => e.Visible)).IsTrue();
                AssertThat(main.StoryBattle.Current.Combat.Enemies.Select(e => e.Id)
                    .SequenceEqual(new[] { 7, 7 })).IsTrue();
                AssertThat(healthyTextures.All(t => GodotObject.IsInstanceValid(t.Texture))).IsTrue();
                AssertThat(healthyTextures.All(t => captured.All(old => old.Id != t.Id))).IsTrue();
                GD.Print("PAL_136_SAME_URI_HEALTHY:" + JsonSerializer.Serialize(new
                {
                    fixture.StoryUri, fixture.VisualUri, draw.EnemyIdleFrames, draw.EnemyIdleCounters,
                    textures = healthyTextures.Select(t => t.Id)
                }));
            }, configure: main => main.StoryBattleViewFactory = () => new PalStoryBattleView
            {
                TextureOwned = (_, texture) => healthyTextures.Add((texture, texture.GetInstanceId()))
            }))
        {
            healthyMain = fresh.Main;
            AssertThat(fresh.Main.Failure).IsNull();
            fresh.Tap(Key.Enter);
            await fresh.Wait(() => fresh.Main.Dialogue!.Session!.PendingDisplay is not null);
            await fresh.FinishDisplay();
            await fresh.Wait(() => first);
            AssertThat(fresh.Main.Failure).IsNull();
        }
        AssertThat(GodotObject.IsInstanceValid(healthyMain)).IsFalse();
        AssertThat(healthyTextures.Count > captured.Count).IsTrue();
        AssertThat(healthyTextures.All(t => !GodotObject.IsInstanceValid(t.Texture)
            && GodotObject.InstanceFromId(t.Id) is null)).IsTrue();
        AssertThat(fixture.Snapshot().SequenceEqual(before)).IsTrue();
        GD.Print($"PAL_136_HEALTHY_RELEASED:{healthyTextures.Count}; source/output hashes unchanged");
    }

    private static System.Collections.Generic.HashSet<string> FriendDeathHookFirstDisplaysSet(bool zh) =>
        zh ? FriendDeathHookFirstDisplays : FriendDeathHookFirstDisplaysEn;

    private static readonly System.Collections.Generic.HashSet<string> DyingHookFirstDisplays =
        new(["我能战", "别管我", "我败了", "谢"]);
    private static readonly System.Collections.Generic.HashSet<string> DyingHookFirstDisplaysEn =
        new(["I CAN FIGHT", "LET ME GO", "I LOST", "THANKS"]);
    private static readonly System.Collections.Generic.HashSet<string> FriendDeathHookFirstDisplays =
        new(["兄弟", "我会胜", "悲为力", "回家"]);
    private static readonly System.Collections.Generic.HashSet<string> FriendDeathHookFirstDisplaysEn =
        new(["BROTHER", "I WILL WIN", "GRIEF TO POWER", "HOME"]);

    // Menus, action feedback and (once connected) the paused role-hook dialogue are all driven
    // from the real Main callback; no direct session advancing.
    private static async Task DriveMidbattleMenus(StoryHost host, Main main)
    {
        var script = main.Dialogue!.Session!;
        var battle = main.StoryBattle!;
        while (script.PendingBattle is not null)
        {
            if (main.StoryBattleMode == StoryBattlePresentation.AttackMenu)
            {
                if (script.PendingBattleHook is not null)
                {
                    // A paused hook must show its dialogue before any further menu input.
                    await host.Wait(() => script.PendingDisplay is not null);
                    while (main.Dialogue!.Session!.PendingDisplay is { } parked)
                    {
                        await host.FinishDisplay();
                        if (main.Dialogue.Session.PendingDisplay?.Id == parked.Id) break;
                    }
                    await host.Wait(() => main.StoryBattleMode != StoryBattlePresentation.HookDialogue || script.PendingBattle is null);
                    continue;
                }
                await host.ConfirmMenu(StoryBattlePresentation.TargetMenu);
                await host.ConfirmMemberTarget(battle);
            }
            else if (main.StoryBattleMode == StoryBattlePresentation.Feedback)
                await host.Wait(() => main.StoryBattleMode != StoryBattlePresentation.Feedback || script.PendingBattle is null);
            else if (main.StoryBattleMode == StoryBattlePresentation.HookDialogue)
            {
                while (main.Dialogue!.Session!.PendingDisplay is { } current)
                {
                    await host.FinishDisplay();
                    if (main.Dialogue.Session.PendingDisplay?.Id == current.Id) break;
                }
                await host.Wait(() => main.StoryBattleMode != StoryBattlePresentation.HookDialogue || script.PendingBattle is null);
            }
            else
                await host.Wait(() => main.StoryBattleMode is StoryBattlePresentation.AttackMenu
                    or StoryBattlePresentation.Feedback or StoryBattlePresentation.HookDialogue || script.PendingBattle is null);
        }
    }

    // Drives menus/feedback until the battle pauses on its first role hook, then proves the real
    // Main consumes it: the paused hook's script dialogue reaches the real display boundary.
    private static async Task AwaitFirstHookDisplay(StoryHost host, Main main)
    {
        var script = main.Dialogue!.Session!;
        while (script.PendingBattleHook is null && script.PendingBattle is not null)
        {
            if (main.StoryBattleMode == StoryBattlePresentation.AttackMenu)
            {
                await host.ConfirmMenu(StoryBattlePresentation.TargetMenu);
                await host.ConfirmMemberTarget(main.StoryBattle!);
            }
            else if (main.StoryBattleMode == StoryBattlePresentation.Feedback)
                await host.Wait(() => main.StoryBattleMode != StoryBattlePresentation.Feedback);
            else
                await host.Wait(() => main.StoryBattleMode is StoryBattlePresentation.AttackMenu
                    or StoryBattlePresentation.Feedback || script.PendingBattle is null);
        }
        await host.Wait(() => script.PendingDisplay is not null);
    }

    [TestCase("zh-Hans", Timeout = 120000)]
    [TestCase("en", Timeout = 120000)]
    public async Task DyingRoleHookShowsDialogueInRealBattleAndResumesToWin(string language)
    {
        // Midbattle: two strong enemy slots act first; their strikes push role1 (dual-attack closure,
        // covered by role0) across the dying threshold mid-queue, so the paused role hook must run its
        // script dialogue over the real battle and the queue resumes from its saved position.
        using var fixture = new AuthoredStoryFixture("Midbattle");
        var selection = GameSceneConfiguration.FromArguments("--story-profile", fixture.StoryUri,
            "--battle-visual-profile", fixture.VisualUri, "--story-language", language, "--battle-seed", "1");
        bool zh = language == "zh-Hans";
        var firstDisplays = zh ? DyingHookFirstDisplays : DyingHookFirstDisplaysEn;
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
            // Hooks pause and resume inside one drive; observe them on the real Main callback.
            var hookEvents = new System.Collections.Generic.List<(Pal.Core.ClassicBattleHookKind Kind, int RoleId, string? First)>();
            Pal.Core.ClassicBattleHookKind? recordedKind = null;
            void CaptureHook()
            {
                if (script.PendingBattleHook is not { } hook) { recordedKind = null; return; }
                if (recordedKind != hook.Kind)
                {
                    recordedKind = hook.Kind;
                    hookEvents.Add((hook.Kind, hook.RoleId, null));
                }
                if (hookEvents.Count > 0 && hookEvents[^1].First is null && script.PendingDisplay is { } display)
                {
                    // The paused battle shows its script dialogue while the battle stays visible.
                    AssertThat(old.StoryBattleMode).IsEqual(StoryBattlePresentation.HookDialogue);
                    AssertThat(old.GetChildren().OfType<PalStoryBattleView>().Count()).IsEqual(1);
                    var text = display.Content.Parts[0].Text!;
                    AssertThat(hook.Kind == Pal.Core.ClassicBattleHookKind.Dying
                        ? firstDisplays.Contains(text) : FriendDeathHookFirstDisplaysSet(zh).Contains(text)).IsTrue();
                    hookEvents[^1] = (hook.Kind, hook.RoleId, text);
                    GD.Print($"PAL_HOOK:{hook.Kind};role={hook.RoleId};first={text}");
                }
            }
            old.UpdateCompleted += CaptureHook;
            while (script.PendingBattle is not null) await DriveMidbattleMenus(host, old);
            old.UpdateCompleted -= CaptureHook;
            AssertThat(hookEvents.Any(h => h.Kind == Pal.Core.ClassicBattleHookKind.Dying)).IsTrue();
            AssertThat(hookEvents.All(h => h.First is not null)).IsTrue();
            AssertThat(battle.Completion!.Outcome).IsEqual(ClassicBattlePhase.Won);
            // The dying member really acted after the pause (its dual strikes are real queue actions).
            AssertThat(host.Actions.Any(a => a.Attacker == Pal.Core.ClassicBattleSide.Player && a.ActorIndex == 1)).IsTrue();
            AssertThat(host.Actions.Any(a => a.SecondStrike)).IsTrue(); // the dual second strike ran in the resumed queue
            AssertThat(script.CallId).IsEqual(call);
            AssertThat(script.PendingDisplay!.Content.Parts[0].Text!).IsEqual(zh ? "胜" : "WIN");
            await host.FinishDisplay();
            await host.Wait(() => old.Interaction!.Phase == NpcInteractionPhase.Exploring);
            AssertThat(script.Phase).IsEqual(TriggerScriptPhase.Completed);
            AssertThat(old.Failure).IsNull();
            GD.Print("PAL_MIDBATTLE_WIN:hooks=" + string.Join(",", hookEvents.Select(h => $"{h.Kind}/{h.RoleId}/{h.First}"))
                + ";hp=" + string.Join('/', battle.Current.Combat.Players.Select(p => p.Hp)) + ";enemies=" + string.Join('/',
                    battle.Current.Combat.Enemies.Select(e => e.Hp)) + ";actions=" + host.Actions.Count);
        }
        AssertThat(GodotObject.IsInstanceValid(old)).IsFalse();
        // Same-URI fresh Main: prepared resources are reused read-only; no session state leaks.
        using (var fresh = new StoryHost(selection,
            enemySlots: [new(100, 120), new(128, 104)], players: [new(200, 176), new(256, 152)]))
        {
            AssertThat(fresh.Main.Failure).IsNull();
            AssertThat(fresh.Main.Dialogue!.Session!.CallId).IsEqual(Guid.Empty);
            AssertThat(fresh.Main.StoryBattle).IsNull();
            fresh.Tap(Key.Enter);
            await fresh.Wait(() => fresh.Main.Dialogue!.Session!.PendingDisplay is not null);
            AssertThat(fresh.Main.Dialogue!.Session!.PendingDisplay!.Content.Parts[0].Text!).IsEqual(zh ? "战" : "FIGHT");
        }
    }

    [TestCase(Timeout = 120000)]
    public async Task FriendDeathHookHealsCovererAndBattleStillEndsWon()
    {
        // The cover scenario: once an enemy strike kills role1, the coverer role0's friend-death
        // script runs (dialogue + changeHp/changeMp/boostStat on the caller); the outcome stays
        // deferred until the hook completes and the battle continues to a real end.
        using var fixture = new AuthoredStoryFixture("Midbattle");
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
            await host.FinishDisplay();
            await host.Wait(() => old.StoryBattleMode == StoryBattlePresentation.AttackMenu);
            var battle = old.StoryBattle!;
            int hpBeforeHook = 0, mpBeforeHook = 0, attackBeforeHook = 0;
            bool friendDeathSeen = false;
            string? friendFirstText = null;
            var observedBoosts = new System.Collections.Generic.List<IReadOnlyList<int>>();
            void CaptureFriendDeath()
            {
                if (script.PendingBattleHook is { Kind: Pal.Core.ClassicBattleHookKind.FriendDeath } hook && !friendDeathSeen)
                {
                    friendDeathSeen = true;
                    var player = battle.Current.Combat.Players[hook.PartyIndex];
                    hpBeforeHook = player.Hp; mpBeforeHook = battle.Current.Roles[hook.RoleId].Mp;
                    attackBeforeHook = player.Attack;
                    AssertThat(hook.RoleId).IsEqual(0); // the coverer is the friend-death caller
                    AssertThat(battle.Current.Combat.Players[1].Hp).IsEqual(0); // the covered member died
                    GD.Print($"PAL_FRIEND_DEATH:before hp={hpBeforeHook} mp={mpBeforeHook} atk={attackBeforeHook}");
                }
                if (friendDeathSeen && script.PendingBattleHook is { Kind: Pal.Core.ClassicBattleHookKind.FriendDeath }
                    && script.PendingDisplay is { } friendDisplay)
                {
                    // The paused battle shows the coverer's script over the visible battle.
                    AssertThat(old.StoryBattleMode).IsEqual(StoryBattlePresentation.HookDialogue);
                    AssertThat(FriendDeathHookFirstDisplays.Contains(friendDisplay.Content.Parts[0].Text!)).IsTrue();
                    friendFirstText ??= friendDisplay.Content.Parts[0].Text!;
                }
                var panel = old.StoryBattleBoosts;
                if (panel.Count > 0 && (observedBoosts.Count == 0 || !observedBoosts[^1].SequenceEqual(panel)))
                    observedBoosts.Add(panel.ToArray());
            }
            old.UpdateCompleted += CaptureFriendDeath;
            while (script.PendingBattle is not null) await DriveMidbattleMenus(host, old);
            old.UpdateCompleted -= CaptureFriendDeath;
            AssertThat(friendDeathSeen).IsTrue();
            var healed = battle.Current.Roles[0];
            // The hook's changeHp +9999 healed the coverer full (capped at maxHp 150) before the
            // Won half recovery, and the boost panel exposed the applied boost registers.
            AssertThat(observedBoosts.Count > 0).IsTrue();
            // Midbattle declares attack=40, magicPower=20, dexterity=100 and fleeRate=10.
            // script.c 0x0030 scales each base word; the visible panel sums all five registers.
            // Check the actual dialogue branch, not a union that accepts another branch's boost.
            int expectedBoost = friendFirstText switch
            {
                "兄弟" => 2,    // L0044: 40 * 5 / 100
                "我会胜" => 2,  // L0051: 20 * 10 / 100
                "悲为力" => 15, // L0057-L0058: 40 * 25 / 100 + 20 * 25 / 100
                "回家" => 99,   // L0062-L0063: 100 * 90 / 100 + 10 * 90 / 100
                _ => throw new InvalidOperationException($"Unexpected friend-death dialogue block: {friendFirstText}.")
            };
            GD.Print($"PAL_FRIEND_BOOST_EXPECTED:block={friendFirstText};value={expectedBoost}");
            foreach (var panel in observedBoosts)
            {
                GD.Print("PAL_BOOST_PANEL:" + string.Join('/', panel));
                AssertThat(panel.Count).IsEqual(2);
                AssertThat(panel[1]).IsEqual(0); // only the caller (coverer) is boosted
                AssertThat(panel[0]).OverrideFailureMessage("panel=" + string.Join('/', panel)
                    + $"; block={friendFirstText}; expected={expectedBoost}").IsEqual(expectedBoost);
            }
            GD.Print("PAL_BOOST_PANELS:" + string.Join("|", observedBoosts.Select(p => string.Join('/', p))));
            AssertThat(battle.Completion!.Outcome).IsEqual(ClassicBattlePhase.Won);
            // The hook's per-block effects are visible in the settled roles: the 兄弟 block healed
            // the coverer full before the Won half recovery, the 我会胜 block restored the mana, and
            // 悲为力/回家 leave both to the half recovery (hp 149+0/2, mp 20+(50-20)/2=35).
            GD.Print($"PAL_FRIEND_DEATH_AFTER:block={friendFirstText};hp={healed.Hp};mp={healed.Mp}");
            if (friendFirstText == "兄弟")
            {
                AssertThat(healed.Hp).IsEqual(150); // changeHp +9999 capped at maxHp, half recovery keeps 150
                AssertThat(healed.Mp).IsEqual(35); // no mana change, then 20+(50-20)/2
            }
            else if (friendFirstText == "我会胜")
            {
                AssertThat(healed.Hp).IsEqual(150); // Won keeps a full-HP coverer at 150
                AssertThat(healed.Mp).IsEqual(50); // changeMp +9999 capped at maxMp
            }
            else
            {
                AssertThat(healed.Hp).IsEqual(hpBeforeHook + (150 - hpBeforeHook) / 2);
                AssertThat(healed.Mp).IsEqual(mpBeforeHook + (50 - mpBeforeHook) / 2);
            }
            AssertThat(script.PendingDisplay!.Content.Parts[0].Text!).IsEqual("胜");
            await host.FinishDisplay();
            await host.Wait(() => old.Interaction!.Phase == NpcInteractionPhase.Exploring);
            AssertThat(script.Phase).IsEqual(TriggerScriptPhase.Completed);
            AssertThat(old.Failure).IsNull();
            GD.Print("PAL_FRIEND_DEATH_AFTER:hp0=" + healed.Hp + " mp0=" + healed.Mp + " actions=" + host.Actions.Count);
        }
        AssertThat(GodotObject.IsInstanceValid(old)).IsFalse();
    }

    [TestCase(Timeout = 240000)]
    public async Task HookRandomBandsDrawDifferentDialoguePaths()
    {
        // The three retail random bands select different dialogue blocks; two fixed seeds must
        // produce at least two distinct first-display paths for the first hook in each battle.
        var firstTexts = new System.Collections.Generic.List<string>();
        foreach (uint seed in new[] { 1u, 7u })
        {
            using var fixture = new AuthoredStoryFixture("Midbattle");
            var selection = GameSceneConfiguration.FromArguments("--story-profile", fixture.StoryUri,
                "--battle-visual-profile", fixture.VisualUri, "--story-language", "zh-Hans", "--battle-seed", seed.ToString());
            using var host = new StoryHost(selection,
                enemySlots: [new(100, 120), new(128, 104)], players: [new(200, 176), new(256, 152)]);
            AssertThat(host.Main.Failure).IsNull();
            host.Tap(Key.Enter);
            await host.Wait(() => host.Main.Dialogue!.Session!.PendingDisplay is not null);
            var script = host.Main.Dialogue!.Session!;
            await host.FinishDisplay();
            await host.Wait(() => host.Main.StoryBattleMode == StoryBattlePresentation.AttackMenu);
            await AwaitFirstHookDisplay(host, host.Main);
            firstTexts.Add(script.PendingDisplay!.Content.Parts[0].Text!);
            GD.Print($"PAL_BAND_SEED{seed}:first={script.PendingDisplay!.Content.Parts[0].Text}");
            // Enough of the battle runs to prove the hook's displays really bind; the remainder
            // of this battle is covered by the other roundtrips, so free the host here.
            host.Main.GetParent().RemoveChild(host.Main);
        }
        AssertThat(firstTexts.Count).IsEqual(2);
        AssertThat(firstTexts[0] != firstTexts[1]).IsTrue();
    }

    [TestCase(Timeout = 120000)]
    public async Task HooklessCounterpartNeverBindsMidbattleDialogue()
    {
        // Targeted counterexample: PartyEquip shares the numeric/visual shape but declares no
        // role-hook scripts; its battle must never pause on a hook or bind a mid-battle display,
        // and the dual attack still presents exactly two strikes.
        using var fixture = new AuthoredStoryFixture("PartyEquip");
        var selection = GameSceneConfiguration.FromArguments("--story-profile", fixture.StoryUri,
            "--battle-visual-profile", fixture.VisualUri, "--story-language", "zh-Hans", "--battle-seed", "1");
        using var host = new StoryHost(selection,
            enemySlots: [new(100, 120), new(128, 104)], players: [new(200, 176), new(256, 152)]);
        AssertThat(host.Main.Failure).IsNull();
        host.Tap(Key.Enter);
        await host.Wait(() => host.Main.Dialogue!.Session!.PendingDisplay is not null);
        var script = host.Main.Dialogue!.Session!;
        await host.FinishDisplay();
        await host.Wait(() => host.Main.StoryBattleMode == StoryBattlePresentation.AttackMenu);
        var battle = host.Main.StoryBattle!;
        while (script.PendingBattle is not null)
        {
            AssertThat(script.PendingBattleHook).IsNull();
            AssertThat(script.PendingDisplay).IsNull();
            if (host.Main.StoryBattleMode == StoryBattlePresentation.AttackMenu)
            {
                await host.ConfirmMenu(StoryBattlePresentation.TargetMenu);
                await host.ConfirmMemberTarget(host.Main.StoryBattle!);
            }
            else if (host.Main.StoryBattleMode == StoryBattlePresentation.Feedback)
                await host.Wait(() => host.Main.StoryBattleMode != StoryBattlePresentation.Feedback || script.PendingBattle is null);
            else
                await host.Wait(() => host.Main.StoryBattleMode is StoryBattlePresentation.AttackMenu
                    or StoryBattlePresentation.Feedback || script.PendingBattle is null);
        }
        AssertThat(battle.Completion!.Outcome).IsEqual(ClassicBattlePhase.Won);
        // role1's one dual action presented as two strikes: the first plus exactly one flagged second.
        AssertThat(host.Actions.Count(a => a.Attacker == Pal.Core.ClassicBattleSide.Player && a.ActorIndex == 1)).IsEqual(2);
        AssertThat(host.Actions.Count(a => a.SecondStrike)).IsEqual(1);
        AssertThat(host.Main.StoryBattleMode).IsNull();
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

    [TestCase("zh-Hans", Timeout = 120000)]
    [TestCase("en", Timeout = 120000)]
    public async Task VictorySettlementDisplaysGrowthRunsPostBattleScriptAndFlowsPanels(string language)
    {
        // Rewards: battle 1 (team18, experience120/cash250) settles through the authored boxes —
        // exp/cash, the level-up line (5+120 crosses the level-2 threshold once: a split-experience
        // mutation would show none of these) and the learned magic 10 — then battle 2 consumes
        // the settled grown panels as its base declaration and runs the enemy's post-victory
        // dialogue before the event resumes to the victory line.
        using var fixture = new AuthoredStoryFixture("Rewards");
        string hash = Godot.FileAccess.GetSha256(fixture.StoryUri);
        var selection = GameSceneConfiguration.FromArguments("--story-profile", fixture.StoryUri,
            "--battle-visual-profile", fixture.VisualUri, "--story-language", language, "--battle-seed", "1");
        bool zh = language == "zh-Hans";
        Main old;
        using (var host = new StoryHost(selection))
        {
            old = host.Main;
            AssertThat(old.Failure).IsNull();
            host.Tap(Key.Enter);
            await host.Wait(() => old.Dialogue!.Session!.PendingDisplay is not null);
            var script = old.Dialogue!.Session!;
            Guid call = script.CallId;
            await host.FinishDisplay();
            await host.Wait(() => old.StoryBattleMode == StoryBattlePresentation.AttackMenu);
            var first = script.PendingBattle!.Lifecycle;
            // Battle 1: one round kills the 43hp slot through the real menus.
            await host.ConfirmMenu(StoryBattlePresentation.TargetMenu);
            await host.ConfirmMenu(StoryBattlePresentation.Resolving);
            await host.Wait(() => old.StoryBattleMode == StoryBattlePresentation.Feedback);
            await host.Wait(() => old.StoryBattleMode == StoryBattlePresentation.Settlement);
            AssertThat(old.StorySettlementLine).IsEqual(zh ? "经验:120 现金:250" : "EXP:120 CASH:250");
            host.Tap(Key.Enter);
            await host.Wait(() => old.StorySettlementLine == (zh ? "升级:1:3" : "LEVEL UP:1:3"));
            host.Tap(Key.Enter);
            await host.Wait(() => old.StorySettlementLine == (zh ? "习得法术:1:10" : "LEARNED MAGIC:1:10"));
            // The confirming Enter is consumed by the settlement box only; the next display needs a new tap.
            AssertThat(old.Dialogue!.Session!.PendingDisplay).IsNull();
            host.Tap(Key.Enter);
            await host.Wait(() => old.StoryBattleMode == StoryBattlePresentation.AttackMenu);
            var second = script.PendingBattle!.Lifecycle;
            AssertThat(second.Completion).IsNull();
            var settled = first.Current.Role;
            AssertThat(settled.Level).IsEqual(3);
            AssertThat(settled.PrimaryExperience.Value).IsEqual(25);
            AssertThat(settled.Magics.ToArray()[..2].SequenceEqual(new[] { 9, 10 })).IsTrue();
            // The settled grown panels are battle 2's base declaration (no equipment overlay here).
            var basePlayer = second.Current.Combat.Players[0];
            AssertThat(basePlayer.Attack).IsEqual(settled.Attack);
            AssertThat(basePlayer.Defense).IsEqual(settled.Defense);
            AssertThat(basePlayer.Dexterity).IsEqual(settled.Dexterity);
            AssertThat(second.Current.Role.MaxHp).IsEqual(settled.MaxHp);
            // Battle 2 has no rewards, so no settlement box: the enemy's post-victory script runs
            // its dialogue over the finished battle (battle.c 1334-1337) before the recovery.
            await host.ConfirmMenu(StoryBattlePresentation.TargetMenu);
            await host.ConfirmMenu(StoryBattlePresentation.Resolving);
            await host.Wait(() => old.StoryBattleMode == StoryBattlePresentation.Feedback);
            await host.Wait(() => old.StoryBattleMode == StoryBattlePresentation.HookDialogue
                && script.PendingDisplay is not null);
            AssertThat(script.RunningBattleEndScript!.ScriptAddress).IsEqual(8);
            AssertThat(old.GetChildren().OfType<PalStoryBattleView>().Count()).IsEqual(1);
            AssertThat(script.PendingDisplay!.Content.Parts[0].Text!).IsEqual(zh ? "我会报仇" : "I WILL RETURN");
            await host.FinishDisplay();
            await host.Wait(() => script.PendingBattle is null);
            AssertThat(old.StoryBattleMode).IsNull();
            // The ledger consumed exactly one owned Won receipt per granted battle.
            AssertThat(old.StoryExperienceGranted).IsEqual(120);
            AssertThat(old.StoryCashGranted).IsEqual(250);
            AssertThat(script.CallId).IsEqual(call);
            AssertThat(script.PendingDisplay!.Content.Parts[0].Text!).IsEqual(zh ? "胜" : "WIN");
            await host.FinishDisplay();
            await host.Wait(() => old.Interaction!.Phase == NpcInteractionPhase.Exploring);
            AssertThat(script.Phase).IsEqual(TriggerScriptPhase.Completed);
            AssertThat(old.Failure).IsNull();
        }
        AssertThat(GodotObject.IsInstanceValid(old)).IsFalse();
        AssertThat(Godot.FileAccess.GetSha256(fixture.StoryUri)).IsEqual(hash);
        // Same-URI fresh Main: the settled growth of the first session never leaks into a new run.
        using (var fresh = new StoryHost(selection))
        {
            AssertThat(fresh.Main.Failure).IsNull();
            AssertThat(fresh.Main.StoryCashGranted).IsEqual(0);
            fresh.Tap(Key.Enter);
            await fresh.Wait(() => fresh.Main.Dialogue!.Session!.PendingDisplay is not null);
            await fresh.FinishDisplay();
            await fresh.Wait(() => fresh.Main.StoryBattleMode == StoryBattlePresentation.AttackMenu);
            var restart = fresh.Main.Dialogue!.Session!.PendingBattle!.Lifecycle;
            AssertThat(restart.Current.Role.Level).IsEqual(2);
            AssertThat(restart.Current.Role.MaxHp).IsEqual(150);
        }
    }

    [TestCase(Timeout = 120000)]
    public async Task EquipmentStatusAbove999SurvivesTheWonSettlementCleanup()
    {
        // Targeted counterexample: the persistent dual-attack equipment status (60000 > 999) must
        // survive the battle-end cleanup of the Won settlement (global.c 2297 keeps >999 words).
        using var fixture = new AuthoredStoryFixture("PartyEquip");
        var selection = GameSceneConfiguration.FromArguments("--story-profile", fixture.StoryUri,
            "--battle-visual-profile", fixture.VisualUri, "--story-language", "zh-Hans", "--battle-seed", "1");
        using var host = new StoryHost(selection,
            enemySlots: [new(100, 120), new(128, 104)], players: [new(200, 176), new(256, 152)]);
        AssertThat(host.Main.Failure).IsNull();
        host.Tap(Key.Enter);
        await host.Wait(() => host.Main.Dialogue!.Session!.PendingDisplay is not null);
        var script = host.Main.Dialogue!.Session!;
        await host.FinishDisplay();
        await host.Wait(() => host.Main.StoryBattleMode == StoryBattlePresentation.AttackMenu);
        var battle = host.Main.StoryBattle!;
        while (script.PendingBattle is not null)
        {
            if (host.Main.StoryBattleMode == StoryBattlePresentation.AttackMenu)
            {
                await host.ConfirmMenu(StoryBattlePresentation.TargetMenu);
                await host.ConfirmMemberTarget(battle);
            }
            else
            {
                await host.Wait(() => host.Main.StoryBattleMode == StoryBattlePresentation.Feedback
                    || host.Main.StoryBattleMode == StoryBattlePresentation.AttackMenu || script.PendingBattle is null);
                if (script.PendingBattle is null) break;
                if (host.Main.StoryBattleMode == StoryBattlePresentation.Feedback)
                    await host.Wait(() => host.Main.StoryBattleMode != StoryBattlePresentation.Feedback || script.PendingBattle is null);
            }
        }
        AssertThat(battle.Completion!.Outcome).IsEqual(ClassicBattlePhase.Won);
        AssertThat(battle.Current.Roles[1].Statuses[8]).IsEqual(60000);
        AssertThat(host.Main.StoryCashGranted).IsEqual(0);
        AssertThat(host.Main.StoryExperienceGranted).IsEqual(0);
    }

    [TestCase(Timeout = 120000)]
    public async Task DeadMemberRevivesAtHalfMaximumThroughTheVictorySettlement()
    {
        // Targeted counterexample: the dying member is dead when the battle ends Won; the automatic
        // half recovery must still include it (battle.c 1332-1349: revive at half the maximum),
        // while its growth records stay untouched.
        using var fixture = new AuthoredStoryFixture("Midbattle");
        var selection = GameSceneConfiguration.FromArguments("--story-profile", fixture.StoryUri,
            "--battle-visual-profile", fixture.VisualUri, "--story-language", "zh-Hans", "--battle-seed", "1");
        using var host = new StoryHost(selection,
            enemySlots: [new(100, 120), new(128, 104)], players: [new(200, 176), new(256, 152)]);
        AssertThat(host.Main.Failure).IsNull();
        host.Tap(Key.Enter);
        await host.Wait(() => host.Main.Dialogue!.Session!.PendingDisplay is not null);
        var script = host.Main.Dialogue!.Session!;
        await host.FinishDisplay();
        await host.Wait(() => host.Main.StoryBattleMode == StoryBattlePresentation.AttackMenu);
        var battle = host.Main.StoryBattle!;
        var initial = battle.Current.Roles.ToDictionary(pair => pair.Key, pair => pair.Value);
        while (script.PendingBattle is not null) await DriveMidbattleMenus(host, host.Main);
        AssertThat(battle.Completion!.Outcome).IsEqual(ClassicBattlePhase.Won);
        // The dying member is dead at the end of combat (hp0 in the frozen combat view).
        int dead = battle.Current.Combat.Players.Select((player, index) => (player, index))
            .Single(x => x.player.Hp == 0).index;
        var revived = battle.Current.Roles[battle.Current.Combat.Players[dead].RoleId];
        AssertThat(revived.Hp).IsEqual(revived.MaxHp / 2);
        AssertThat(revived.Level).IsEqual(initial[revived.RoleId].Level);
        AssertThat(revived.PrimaryExperience).IsEqual(initial[revived.RoleId].PrimaryExperience);
        // The survivor recovers half its missing HP; no exp was granted, so nothing else changes.
        int survivor = dead == 0 ? 1 : 0;
        var survivorRole = battle.Current.Roles[battle.Current.Combat.Players[survivor].RoleId];
        int combatHp = battle.Current.Combat.Players[survivor].Hp;
        AssertThat(survivorRole.Hp).IsEqual(combatHp + (survivorRole.MaxHp - combatHp) / 2);
        AssertThat(host.Main.StoryExperienceGranted).IsEqual(0);
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
        private readonly Action<Main>? updateObserver;
        private readonly int enemyImpactPose;
        internal StoryHost(GameSceneConfiguration selection, Vector2[]? enemySlots = null, Vector2[]? players = null,
            Action<Main>? updateObserver = null, int enemyImpactPose = 5, Action<Main>? configure = null)
        {
            // Party hosts reuse the same production scene; only the observer switches to party checks.
            party = enemySlots is not null;
            if (party)
            {
                if (enemySlots!.Length is not (2 or 5) || players is not { Length: 1 or 2 or 3 })
                    throw new ArgumentException("Multi-actor hosts require two or five enemy slots and one to three player origins.");
                partyEnemies = enemySlots; partyPlayers = players!;
            }
            this.updateObserver = updateObserver;
            this.enemyImpactPose = enemyImpactPose;
            Main.Configure(selection); configure?.Invoke(Main); Main.UpdateCompleted += Updated;
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
                updateObserver?.Invoke(Main);
                var combat = Main.StoryBattle?.Current.Combat;
                var pose = Main.StoryBattlePose;
                var draw = Main.StoryDrawSnapshot;
                if (!party && pose is { Pose: 0 } && previousPose is null) numberBornAt = null;
                if (party)
                {
                    if (pose is { Pose: 0 } && previousPose is null) numberBornAt = null;
                    if (draw is { PartyPlayers: { Count: > 0 } members, PartyEnemies: { Count: 2 } slots } partyDraw)
                    {
                        var currentAction = combat?.LastAction;
                        bool feedback = Main.StoryBattleMode == StoryBattlePresentation.Feedback;
                        AssertThat(members.Count).IsEqual(partyPlayers.Length);
                        for (int i = 0; i < members.Count; i++)
                        {
                            bool animating = feedback && currentAction is { Attacker: ClassicBattleSide.Player, ActorIndex: var actor } && actor == i;
                            // fight.c:5012-5022: a covering member stands at the target's side with frame 3.
                            if (feedback && currentAction is { Attacker: ClassicBattleSide.Enemy, CovererPartyIndex: var coverer } && coverer == i)
                            {
                                AssertThat(members[i].Position == partyPlayers[currentAction.TargetIndex] + new Vector2(-24, -12)).IsTrue();
                                AssertThat(members[i].Frame).IsEqual(3);
                            }
                            else if (!animating)
                            {
                                Vector2 recoil = Vector2.Zero;
                                if (feedback && currentAction is { Attacker: ClassicBattleSide.Enemy, TargetIndex: var target, CovererPartyIndex: null }
                                    && target == i && pose is { } incomingPose)
                                    recoil = incomingPose.Pose <= enemyImpactPose ? Vector2.Zero
                                        : incomingPose.Pose == enemyImpactPose + 1 ? new(8, 4) : new(10, 5);
                                AssertThat(members[i].Position == partyPlayers[i] + recoil).IsTrue();
                            }
                        }
                        for (int slot = 0; slot < 2; slot++)
                        {
                            bool targeted = feedback && currentAction is { Attacker: ClassicBattleSide.Player, TargetIndex: var hit } && hit == slot;
                            bool moving = feedback && currentAction is { Attacker: ClassicBattleSide.Enemy, ActorIndex: var mover } && mover == slot;
                            if (!targeted && !moving) AssertThat(slots[slot].Position == partyEnemies[slot]).IsTrue();
                        }
                        if (Main.StoryBattleMode is StoryBattlePresentation.AttackMenu or StoryBattlePresentation.TargetMenu)
                            AssertThat(partyDraw.SelectingPlayer == combat!.SelectingPlayerIndex).IsTrue();
                        // Index-zero compat projections follow party actor zero, not a merged total.
                        AssertThat(partyDraw.DisplayedPlayerHp).IsEqual(members[0].Hp);
                        AssertThat(partyDraw.DisplayedEnemyHp).IsEqual(slots[0].Hp);
                        AssertThat(Main.StoryDisplayedPlayerHp == members[0].Hp).IsTrue();
                        AssertThat(Main.StoryDisplayedEnemyHp == slots[0].Hp).IsTrue();
                    }
                    if (pose is { } partyPose && draw is { } numberDraw && combat?.LastAction is { } partyAction
                        && numberDraw.PartyEnemies.Count == 2)
                    {
                        int birthPose = partyAction.Attacker == ClassicBattleSide.Player ? 7 : enemyImpactPose - 1;
                        if (partyPose.Pose == birthPose && numberBornAt is null)
                        { numberBornAt = Main.StoryPresentationTicks; NumberBirths++; }
                        if (numberBornAt is { } birth)
                        {
                            uint elapsed = unchecked(Main.StoryPresentationTicks - birth);
                            bool visible = elapsed < 440 && partyPose.Pose >= (partyAction.Attacker == ClassicBattleSide.Player ? 8 : enemyImpactPose);
                            var currentDigits = numberDraw.NumberDigits.Where(d => d.BornAt == birth).ToArray();
                            bool numeric = visible && !partyAction.Evaded && partyAction.Damage > 0;
                            AssertThat(currentDigits.Length > 0 == numeric).IsTrue();
                            if (numeric)
                            {
                                NumberDraws++;
                                Vector2 anchor = partyAction.Attacker == ClassicBattleSide.Player
                                    ? partyEnemies[partyAction.TargetIndex] : partyPlayers[partyAction.TargetIndex];
                                float y = Math.Max(10, anchor.Y - (partyAction.Attacker == ClassicBattleSide.Player ? 115 : 75)) - elapsed / 40;
                                AssertThat(currentDigits[0].Position == new Vector2(anchor.X, y)).IsTrue();
                                string text = string.Concat(currentDigits.Reverse().Select(d => d.Digit.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                                AssertThat(text).IsEqual(partyAction.Damage.ToString(System.Globalization.CultureInfo.InvariantCulture));
                                AssertThat(currentDigits.All(d => d.Color == StoryNumberColor.Blue)).IsTrue();
                            }
                        }
                    }
                }
                if (!party && pose is { } feedbackPose && draw is { } feedbackDraw && combat?.LastAction is { } feedbackAction
                    && feedbackDraw.FeedbackWord != PalStoryBattleView.EscapeFailWord)
                {
                    // (#51) The failed-escape feedback carries its own label and no digits; the
                    // strike-model assertions below apply only to real attack feedback.
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
                    // (#51) The authored flee feedback (11 poses, final pose 10) freezes the
                    // scene instead of the enemy strike's final UpdateFighters tick.
                    bool fleeEnded = finalPose.Pose == StoryPlayerFleePresentation.Duration - 1;
                    int expected = !fleeEnded && previousCombat!.LastAction!.Attacker == ClassicBattleSide.Enemy
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
                        if (beforePose.Pose != StoryPlayerFleePresentation.Duration - 1
                            && previousCombat?.LastAction?.Attacker == ClassicBattleSide.Enemy)
                            AssertThat(beforePose.Pose).IsEqual(enemyImpactPose + 10); // Eleven timed recovery ticks, including impact.
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
        // The target-confirm Enter either starts the next alive member's selection or, for
        // the final selecting member, builds the queue and enters Resolving.
        internal async Task ConfirmMemberTarget(Pal.Core.ClassicBattleLifecycleSession battle)
        {
            var combat = Main.StoryBattle!.Current.Combat;
            int selecting = combat.SelectingPlayerIndex
                ?? throw new InvalidOperationException("Target menu has no selecting member.");
            bool final = Enumerable.Range(selecting + 1, combat.Players.Count - selecting - 1)
                .All(index => combat.Players[index].Hp == 0);
            await ConfirmMenu(final ? StoryBattlePresentation.Resolving : StoryBattlePresentation.AttackMenu);
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
        internal async Task WaitForFailure()
        {
            ulong start = Time.GetTicksMsec();
            while (Main.Failure is null)
            {
                if (observationFailure is not null) throw observationFailure;
                if (Time.GetTicksMsec() - start > 5000) throw new TimeoutException("Expected story initialization failure not observed.");
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
