using System;
using System.IO;
using System.Linq;
using Godot;
using Pal.Core;
using Pal.Game.Map;
using Pal.Game.Profiles;
using Pal.Game.Resources;

namespace Pal.Game;

public partial class Main : Node2D
{
    private static readonly string[] DirectionActions =
        ["exploration_south", "exploration_west", "exploration_north", "exploration_east"];
    // Matches ExplorationKey order: primary/alternate pairs for each direction action.
    private static readonly Key[] DirectionKeys =
        [Key.Down, Key.S, Key.Left, Key.A, Key.Up, Key.W, Key.Right, Key.D];
    private ExplorationInput? input;
    private PalMapScene? mapScene;
    public ExplorationState? Exploration => input?.Current;

    public override void _Ready()
    {
        string? scenePath = null, profilePath = null;
        try
        {
            GetWindow().MinSize = new Vector2I(320, 200);
            var (root, profile, staticAcceptance) = NativeSceneLoader.Arguments(OS.GetCmdlineUserArgs());
            scenePath = root + "/Scenes/Scene.tscn";
            profilePath = profile;
            var map = NativeSceneLoader.Load(root);
            try
            {
                var checkedProfile = NativeExplorationProfile.Load(profile, map.Data);
                if (staticAcceptance) ValidateStaticAcceptance(map.Data, checkedProfile);
                input = new ExplorationInput(new ExplorationSession(checkedProfile.Rules, checkedProfile.Leader));
                map.InitializeScene(input.Current.Plan, checkedProfile.TextureFor);
            }
            catch
            {
                map.Free();
                throw;
            }
            mapScene = map;
            AddChild(map);
            GD.Print($"PAL_MAP_LOADED:{root}/Scenes/Scene.tscn");
            if (staticAcceptance) GD.Print("PAL_M3_STATIC_ACCEPTANCE:scene4/map10; events are not scheduled");
            GD.Print($"PAL_GAME_READY:{Pal.Core.Startup.Status}");
        }
        catch (Exception e)
        {
            GD.PrintErr($"Static scene startup failed (scene={scenePath ?? "unselected"}, profile={profilePath ?? "unselected"}): {e.Message}");
            SetProcess(false);
            GetTree().Quit(1);
        }
    }

    private static void ValidateStaticAcceptance(PalSceneData scene, CheckedExplorationProfile profile)
    {
        if (scene.SceneId != 4 || scene.MapId != 10 || scene.Objects.Count != 32)
            throw new InvalidDataException("M3 static acceptance requires scene 4 / map 10 and all 32 original objects");
        var owner = scene.Objects.SingleOrDefault(item => item.Id == 60);
        if (owner == null || owner.X != 1216 || owner.Y != 1344 || owner.WSpriteNum != 207
            || owner.SState != 2 || owner.SVanishTime != 0)
            throw new InvalidDataException("M3 static acceptance owner 60 identity/initial state mismatch");
        if (profile.Leader.X != 1232 || profile.Leader.Y != 1336 || profile.Leader.Direction != 0
            || profile.Leader.Layer != 0 || profile.Leader.StepPhase != 0)
            throw new InvalidDataException("M3 static acceptance profile must use start (1232,1336), South0, layer0/phase0, role0");
    }

    public override void _Input(InputEvent @event)
    {
        if (input == null || @event is not InputEventKey key) return;
        for (int i = 0; i < DirectionKeys.Length; i++)
        {
            if (key.Keycode != DirectionKeys[i] || !key.IsAction(DirectionActions[i / 2])) continue;
            if (key.Pressed) input.Press((ExplorationKey)i, key.Echo);
            else input.Release((ExplorationKey)i);
            return;
        }
    }

    public override void _Process(double delta)
    {
        if (input == null || mapScene == null) return;
        try
        {
            if (input.Update(unchecked((uint)Time.GetTicksMsec())))
                mapScene.UpdateScene(input.Current.Plan);
        }
        catch (Exception e)
        {
            SetProcess(false);
            GD.PrintErr($"Exploration update failed: {e.Message}");
            GetTree().Quit(1);
        }
    }
}
