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
        private StoryBattlePoseSnapshot? previousPose;
        private ClassicBattleAction? last;
        private int updates;
        private Exception? observationFailure;
        private StoryBattlePresentation? previousMode;
        private ClassicBattleView? previousCombat;
        private ulong? resultBoundFrame;
        private PalDialogueDisplay? display;
        internal StoryHost(GameSceneConfiguration selection)
        {
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
                    }
                }
                if (pose is { } firstPose && previousPose is null)
                {
                    AssertThat(firstPose.Pose).IsEqual(0);
                    AssertThat(firstPose.PoseStartedAt).IsEqual(firstPose.Now);
                }
                previousPose = pose;
                if (Main.StoryBattleMode == StoryBattlePresentation.Feedback && previousMode == StoryBattlePresentation.Feedback)
                    AssertThat(ReferenceEquals(combat, previousCombat)).IsTrue();
                if (combat?.LastAction is { } action && !ReferenceEquals(action, last))
                {
                    AssertThat(previousMode).IsEqual(StoryBattlePresentation.Resolving);
                    AssertThat(Main.StoryBattleMode).IsEqual(StoryBattlePresentation.Feedback);
                    Actions.Add(action); last = action;
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
