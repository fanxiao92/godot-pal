using System;
using System.Threading.Tasks;
using Godot;
using Pal.Core;
using Pal.Game.Dialogue;

namespace Pal.Game.Tests;

/// <summary>Real-process observer of the #51 lost-reload terminal: drives the shipped story
/// Main through search, the failed boss flee, the lost failure branch and the 0x004E boundary
/// with real input events, then quits through the normal process exit. NOT GPU pixels, manual
/// desktop acceptance or a substitute for the SceneRunner roundtrip.</summary>
public partial class ObserveStoryLost : Node
{
    private Main main = null!;

    public override void _Ready() => CallDeferred(nameof(Observe));

    private async void Observe()
    {
        try
        {
            main = ResourceLoader.Load<PackedScene>("res://Scenes/Main.tscn").Instantiate<Main>();
            GetTree().Root.AddChild(main);
            await Frames(3);
            if (main.Failure is not null) { Fail("story failed to start: " + main.Failure); return; }
            Tap(Key.Enter); // search owner1: the pre-battle dialogue
            if (!await Wait(() => main.Dialogue!.Session!.PendingDisplay is not null, 300)) { Fail("pre-battle display missing"); return; }
            if (!await Wait(() => main.Dialogue!.Session!.PendingDisplay is { Display.Current.Phase: DialogueDisplayPhase.WaitingForAdvance }, 300)) { Fail("pre-battle text never waited"); return; }
            Tap(Key.Enter); // advance the pre-battle text into the action menu
            if (!await Wait(() => main.StoryBattleMode == StoryBattlePresentation.AttackMenu, 300)) { Fail("attack menu missing"); return; }
            Tap(Key.Q); // input.c kKeyFlee: the boss flee attempt
            if (!await Wait(() => main.StoryBattle!.Current.Combat.LastFleeAttempt is not null, 300)) { Fail("no failed flee attempt"); return; }
            // The failed flee stayed in battle: the action menu returns for the killing round.
            if (!await Wait(() => main.StoryBattleMode == StoryBattlePresentation.AttackMenu, 600)) { Fail("menu did not return after the failed flee"); return; }
            Tap(Key.Enter); // attack
            if (!await Wait(() => main.StoryBattleMode == StoryBattlePresentation.TargetMenu, 300)) { Fail("target menu missing"); return; }
            Tap(Key.Enter); // target: the queue trades into the killing enemy strike
            // hp150 member: the enemy strikes first every round (~120 damage); the failed flee
            // leaves ~30hp and the second round's strike completes the actual Lost.
            if (!await Wait(() => main.Dialogue!.Session!.PendingDisplay is not null, 600)) { Fail("loss dialogue missing"); return; }
            if (!await Wait(() => main.Dialogue!.Session!.PendingDisplay is { Display.Current.Phase: DialogueDisplayPhase.WaitingForAdvance }, 600)) { Fail("loss text never waited"); return; }
            Tap(Key.Enter); // advance the centered loss text
            if (!await Wait(() => main.StoryLostTerminal, 900)) { Fail("lost terminal not reached"); return; }
            if (main.StoryLostReloadDiagnostics != 1) { Fail("diagnostic count is not 1"); return; }
            if (main.StoryCashGranted != 0 || main.StoryExperienceGranted != 0) { Fail("lost run granted rewards"); return; }
            await Frames(150); // the red fade keeps stepping as presentation while business is stopped
            Tap(Key.Enter);
            Tap(Key.Q); // terminal input stays inert; no re-print, no return to exploration
            await Frames(30);
            if (main.StoryLostReloadDiagnostics != 1) { Fail("terminal printed again"); return; }
            if (main.Interaction!.Phase != NpcInteractionPhase.Trigger) { Fail("exploration resumed from the terminal"); return; }
            GD.Print("PAL_STORY_LOST_OBSERVED:terminal=1; diagnostic=PAL_LOST_RELOAD_TODO once; inert input; exit=normal");
            GetTree().Quit(0);
        }
        catch (Exception error) { Fail(error.ToString()); }
    }

    private static void Tap(Key key)
    {
        using var press = new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true };
        Input.ParseInputEvent(press);
        Input.FlushBufferedEvents();
        using var release = new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false };
        Input.ParseInputEvent(release);
        Input.FlushBufferedEvents();
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task<bool> Wait(Func<bool> predicate, int limitFrames)
    {
        for (int i = 0; i < limitFrames; i++)
        {
            if (predicate()) return true;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        return predicate();
    }

    private void Fail(string message)
    {
        GD.PrintErr("STORY_LOST_OBSERVE_FAIL:" + message);
        GetTree().Quit(1);
    }
}
