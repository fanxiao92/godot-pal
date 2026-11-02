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
            host.Tap(Key.Enter);
            await host.Wait(() => old.Dialogue!.Session!.PendingDisplay is not null);
            var script = old.Dialogue!.Session!;
            Guid call = script.CallId;
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

    private sealed class StoryHost : IDisposable
    {
        internal Main Main { get; } = new();
        private readonly ISceneRunner runner;
        private readonly Control? focus = ((SceneTree)Engine.GetMainLoop()).Root.GuiGetFocusOwner();
        internal List<ClassicBattleAction> Actions { get; } = [];
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
            runner = ISceneRunner.Load(Main, autoFree: true);
            display = Main.Display;
            if (display is not null) display.UpdateCompleted += DisplayUpdated;
        }
        private void Updated()
        {
            updates++;
            try
            {
                var combat = Main.StoryBattle?.Current.Combat;
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
