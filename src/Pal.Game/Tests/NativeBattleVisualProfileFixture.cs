using System;
using System.IO;
using System.Linq;
using Godot;
using Pal.Game.Profiles;
using Pal.Game.Resources;

namespace Pal.Game.Tests;

internal sealed class NativeBattleVisualProfileFixture : IDisposable
{
    public NativeStoryProfileFixture Story { get; } = new();
    public CheckedStoryProfile CheckedStory { get; }
    public string Root => Story.Root;
    public string Uri => Root + "/Visual.tres";
    public PalBattleVisualProfile Profile { get; }
    public PalBattleVisualTeam Team => Profile.Teams[0];
    public PalSprite Player { get; } = Sprite(41, 10);
    public PalSprite Enemy { get; } = Sprite(97, 3);
    public PalPalette Palette { get; } = new() { Rgb = new byte[768] };
    public PalDialogueIndexedImage Background { get; } = new()
    { SourceIdentity = "authored synthetic background", Width = 320, Height = 200, PaletteIndices = new byte[64000], Alpha = Enumerable.Repeat((byte)255, 64000).ToArray() };
    public string[] ExpectedPaths { get; }

    public NativeBattleVisualProfileFixture()
    {
        CheckedStory = NativeStoryProfile.Load(Story.Uri);
        Profile = new PalBattleVisualProfile
        {
            FormatVersion = 1, SourceIdentity = "literal synthetic visuals; no provenance authentication", RoleId = 0,
            PlayerSpriteUri = Root + "/Player.tres", PaletteUri = Root + "/BattlePalette.tres", PlayerPosition = new(240, 170),
            Teams = [new PalBattleVisualTeam { TeamId = 18, TargetId = 7, BackgroundUri = Root + "/Background.tres",
                EnemySpriteUri = Root + "/Enemy.tres", EnemyPosition = new(100, 120), IdleFrames = 1, MagicFrames = 1, AttackFrames = 1, ActWaitFrames = 0 }]
        };
        ExpectedPaths = new[] { "Player.tres", "Enemy.tres", "BattlePalette.tres", "Background.tres" }.Select(x => Root + "/" + x).ToArray();
        SaveLeaves(); RefreshHashes(); Save();
    }
    public void SaveLeaves()
    {
        Story.Save(Player, "Player.tres"); Story.Save(Enemy, "Enemy.tres");
        Story.Save(Palette, "BattlePalette.tres"); Story.Save(Background, "Background.tres");
    }
    public void RefreshHashes()
    {
        Profile.ResourceSha256.Clear();
        foreach (string path in ExpectedPaths) Profile.ResourceSha256.Add(path, Godot.FileAccess.GetSha256(path));
    }
    public void Save() => NativeProfileFixture.SaveResource(Profile, Uri);
    public CheckedBattleVisualProfile Load() => NativeBattleVisualProfile.Load(Uri, CheckedStory);
    public void AddNumberSprite()
    {
        var sprite = Sprite(0, 1);
        foreach (int index in Enumerable.Range(19, 20).Concat(Enumerable.Range(56, 10)))
            sprite.Frames.Add(new PalFrame { Index = index, SourceOffset = index * 4, Width = 2, Height = 1,
                Indices = [(byte)index, 79], Alpha = [17, 255] });
        Story.Save(sprite, "Numbers.tres");
        Profile.NumberSpriteUri = Root + "/Numbers.tres";
        Profile.ResourceSha256.Add(Profile.NumberSpriteUri, Godot.FileAccess.GetSha256(Profile.NumberSpriteUri));
        Save();
    }

    private static PalSprite Sprite(int id, int count)
    {
        var sprite = new PalSprite { SpriteId = id, FrameCount = count, DecodedByteLength = 200,
            Source = new Godot.Collections.Dictionary { ["archive"] = "synthetic indexed frames", ["chunk"] = id } };
        for (int i = 0; i < count; i++) sprite.Frames.Add(new PalFrame { Index = i, SourceOffset = 2 + i * 4,
            Width = 2, Height = 1, Indices = [4, 19], Alpha = [0, 255] });
        return sprite;
    }
    public void Dispose() => Story.Dispose();
}
