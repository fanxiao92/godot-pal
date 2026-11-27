using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Pal.Core;
using Pal.Game.Dialogue;
using Pal.Game.Profiles;
using Pal.Game.Resources;
using Pal.Core.State;
using Pal.Core.Battle;
using Pal.Core.Events;
using Pal.Core.Menu;
using Pal.Game.Battle;

namespace Pal.Game.Tests;

/// <summary>#236 real-process observer of the complete route-A chain. It consumes only the
/// prepared Story/Visual native resources selected by argv, proves the Story expectation
/// equals the checked native receipt identity, then drives the shipped Main through the
/// projected battle and owned settlement/ACK back to exploration. Test-only observation,
/// not GPU pixels, manual desktop acceptance or a substitute for SceneRunner coverage.</summary>
public partial class ObserveProjectedBattle : Node
{
    private Main main = null!;

    public override void _Ready() => CallDeferred(nameof(Observe));

    private async void Observe()
    {
        try
        {
            GameProjectMount.EnsureMounted();
            string[] args = OS.GetCmdlineUserArgs();
            string storyUri = Argument(args, "--story-profile");
            var profile = ResourceLoader.Load<PalStoryProfile>(storyUri)
                ?? throw new InvalidOperationException(storyUri + ": expected PalStoryProfile");
            var checkedBattle = NativeBattleContent.LoadChecked(profile.BattleContentUri);
            var source = checkedBattle.ProjectedSource
                ?? throw new InvalidOperationException("projected chain requires projected native battle content");
            if (profile.BattleProjectionReceiptSha256 != source.ReceiptSha256
                || profile.BattleProjectionReceiptByteLength != source.ReceiptByteLength)
                throw new InvalidOperationException("Story expectation does not match the checked native receipt identity");
            var story = NativeStoryProfile.Load(storyUri);
            int[] slots = story.BattleContent.EnemyTeams[18].Select(enemy => enemy.Id).ToArray();
            if (!slots.SequenceEqual(new[] { 1, 2 }))
                throw new InvalidOperationException("projected team 18 enemy ids are not 1,2");
            GD.Print("PAL_PROJECTED_CHAIN:receipt=" + source.ReceiptSha256 + "|" + source.ReceiptByteLength
                + ";numeric=" + source.NumericSha256 + "|" + source.NumericByteLength
                + ";inputs=" + source.ModernInputs.Count + ";roles=" + string.Join(',', source.RoleIds)
                + ";teams=" + string.Join(',', source.TeamIds) + ";slots=" + string.Join(',', slots));

            main = ResourceLoader.Load<PackedScene>("res://Scenes/Main.tscn").Instantiate<Main>();
            GetTree().Root.AddChild(main);
            await Frames(3);
            if (main.Failure is not null) { Fail("story failed to start: " + main.Failure); return; }
            var script = main.Dialogue!.Session!;
            Tap(Key.Enter);
            bool battleSeen = false;
            for (int frame = 0; frame < 1200; frame++)
            {
                if (main.Failure is not null) { Fail("story failed before battle: " + main.Failure); return; }
                if (main.StoryBattleMode is not null) { battleSeen = true; break; }
                if (main.Dialogue?.Session?.PendingDisplay is not null && frame % 8 == 0) Tap(Key.Enter);
                await Frame();
            }
            if (!battleSeen) { Fail("projected battle did not start"); return; }
            var battle = main.StoryBattle!;
            for (int frame = 0; frame < 2400 && main.StoryBattleMode is not null; frame++)
            {
                if (main.Failure is not null) { Fail("story failed during battle: " + main.Failure); return; }
                switch (main.StoryBattleMode)
                {
                    case StoryBattlePresentation.AttackMenu:
                    case StoryBattlePresentation.TargetMenu:
                    case StoryBattlePresentation.Settlement:
                        Tap(Key.Enter);
                        break;
                    case StoryBattlePresentation.HookDialogue:
                        if (main.Dialogue?.Session?.PendingDisplay is not null) Tap(Key.Enter);
                        break;
                }
                await Frame();
            }
            if (main.StoryBattleMode is not null) { Fail("projected battle did not finish"); return; }
            if (battle.Completion is not { Outcome: ClassicBattlePhase.Won }) { Fail("battle did not complete Won"); return; }
            if (battle.Current.Combat.Enemies.Any(enemy => enemy.Hp != 0)) { Fail("enemy slots did not settle at zero"); return; }
            for (int frame = 0; frame < 1200; frame++)
            {
                if (main.Failure is not null) { Fail("story failed after battle: " + main.Failure); return; }
                if (main.Interaction?.Phase == NpcInteractionPhase.Exploring
                    && main.Dialogue?.Session?.PendingDisplay is null) break;
                if (main.Dialogue?.Session?.PendingDisplay is not null && frame % 8 == 0) Tap(Key.Enter);
                await Frame();
            }
            if (main.Interaction?.Phase != NpcInteractionPhase.Exploring) { Fail("exploration did not resume"); return; }
            if (main.StoryExperienceGranted != 6 || main.StoryCashGranted != 10)
            { Fail("owned settlement was not consumed exactly once: xp=" + main.StoryExperienceGranted + " cash=" + main.StoryCashGranted); return; }
            if (script.Phase != TriggerScriptPhase.Completed) { Fail("trigger session is not completed: " + script.Phase); return; }
            if (main.GetChildren().OfType<PalStoryBattleView>().Count() != 0) { Fail("battle view was not released"); return; }
            if (main.StoryBattleMode is not null || main.Failure is not null) { Fail("terminal state still owns battle or failure"); return; }
            // #236 review: the ledgers above are only handoff facts. Prove the settlement reached
            // the same shared RoleLiveState owner the exploration menu projects by reading role 0's
            // status page through the production input/menu path (#134: reusable after return).
            Tap(Key.Escape);
            bool menuOpen = false;
            for (int frame = 0; frame < 120; frame++)
            {
                if (main.Failure is not null) { Fail("menu open failed: " + main.Failure); return; }
                if (main.Menu?.Mode == ExplorationMenuMode.MainMenu) { menuOpen = true; break; }
                await Frame();
            }
            if (!menuOpen) { Fail("exploration menu did not open"); return; }
            Tap(Key.Enter);
            bool statusOpen = false;
            for (int frame = 0; frame < 120; frame++)
            {
                if (main.Failure is not null) { Fail("status page failed: " + main.Failure); return; }
                if (main.Menu?.Mode == ExplorationMenuMode.Status) { statusOpen = true; break; }
                await Frame();
            }
            if (!statusOpen) { Fail("status page did not open"); return; }
            var live = main.Menu!.StatusView();
            if (live.RoleId != 0 || live.Experience != 6)
            { Fail("settled live role state not updated: role=" + live.RoleId + " experience=" + live.Experience); return; }
            Tap(Key.Escape);
            for (int frame = 0; frame < 120 && main.Menu?.Mode != ExplorationMenuMode.Closed; frame++) await Frame();
            if (main.Menu?.Mode != ExplorationMenuMode.Closed) { Fail("status page did not close after settlement check"); return; }
            GD.Print("PAL_PROJECTED_BATTLE_OBSERVED:outcome=Won;xp=6;cash=10;liveRole=0;liveXp=6;enemies=0;phase=Completed;failure=none");
            GetTree().Quit(0);
        }
        catch (Exception error) { Fail(error.ToString()); }
    }

    private static string Argument(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        if (index < 0 || index + 1 >= args.Length) throw new InvalidOperationException("missing " + name);
        return args[index + 1];
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

    private async Task Frame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private void Fail(string message)
    {
        GD.PrintErr("PROJECTED_BATTLE_OBSERVE_FAIL:" + message);
        GetTree().Quit(1);
    }
}
