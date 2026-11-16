using System;
using System.IO;
using System.Linq;
using Godot;
using Pal.Game.Profiles;
using Pal.Game.Resources;

namespace Pal.Game.Tests;

internal sealed class NativeBattleVisualProfileFixture : IDisposable
{
    public bool Party { get; }
    public NativeStoryProfileFixture Story { get; }
    public CheckedStoryProfile CheckedStory { get; }
    public string Root => Story.Root;
    public string Uri => Root + "/Visual.tres";
    public PalBattleVisualProfile Profile { get; }
    public PalBattleVisualTeam Team => Profile.Teams[0];
    public PalSprite Player { get; } = Sprite(41, 10);
    // Distinct second member sprite: role1 draws its own frames, never the head's.
    public PalSprite SecondPlayer { get; } = Sprite(42, 10);
    public PalSprite Enemy { get; } = Sprite(97, 3);
    public PalPalette Palette { get; } = new() { Rgb = new byte[768] };
    public PalDialogueIndexedImage Background { get; } = new()
    { SourceIdentity = "authored synthetic background", Width = 320, Height = 200, PaletteIndices = new byte[64000], Alpha = Enumerable.Repeat((byte)255, 64000).ToArray() };
    public string[] ExpectedPaths { get; }

    public NativeBattleVisualProfileFixture(bool party = false, bool fiveEnemies = false)
    {
        Party = party;
        Story = new NativeStoryProfileFixture(party, fiveEnemies: fiveEnemies);
        CheckedStory = NativeStoryProfile.Load(Story.Uri);
        var team = new PalBattleVisualTeam { TeamId = 18, BackgroundUri = Root + "/Background.tres" };
        if (fiveEnemies)
        {
            // #210 five authored slot positions; resources may be shared, slot identity is not.
            team.EnemySlots =
            [
                new PalBattleVisualEnemySlot { TargetId = 7, EnemyPosition = new(100, 120) },
                new PalBattleVisualEnemySlot { TargetId = 7, EnemyPosition = new(128, 104) },
                new PalBattleVisualEnemySlot { TargetId = 7, EnemyPosition = new(156, 88) },
                new PalBattleVisualEnemySlot { TargetId = 7, EnemyPosition = new(184, 72) },
                new PalBattleVisualEnemySlot { TargetId = 7, EnemyPosition = new(212, 56) }
            ];
        }
        else if (party)
        {
            // Two story enemy slots share enemy definition id 7; positions use distinct authored slots.
            team.EnemySlots =
            [
                new PalBattleVisualEnemySlot { TargetId = 7, EnemyPosition = new(100, 120) },
                new PalBattleVisualEnemySlot { TargetId = 7, EnemyPosition = new(128, 104) }
            ];
        }
        else { team.EnemySlots = [new PalBattleVisualEnemySlot { TargetId = 7, EnemyPosition = new(100, 120) }]; }
        foreach (var slot in team.EnemySlots)
        {
            slot.EnemySpriteUri = Root + "/Enemy.tres";
            slot.IdleFrames = 1; slot.IdleAnimSpeed = 0; slot.MagicFrames = 1;
            slot.AttackFrames = 1; slot.ActWaitFrames = 0;
        }
        Profile = new PalBattleVisualProfile
        {
            FormatVersion = 2, SourceIdentity = "literal synthetic visuals; no provenance authentication",
            Players = party
                ? [PlayerEntry(0, Root + "/Player.tres", new(200, 176)), PlayerEntry(1, Root + "/Player2.tres", new(256, 152))]
                : [PlayerEntry(0, Root + "/Player.tres", new(240, 170))],
            PaletteUri = Root + "/BattlePalette.tres",
            Teams = [team]
        };
        ExpectedPaths = (party ? new[] { "Player.tres", "Player2.tres" } : new[] { "Player.tres" })
            .Concat(new[] { "Enemy.tres", "BattlePalette.tres", "Background.tres" }).Select(x => Root + "/" + x).ToArray();
        SaveLeaves(); RefreshHashes(); Save();
    }
    public void SaveLeaves()
    {
        Story.Save(Player, "Player.tres");
        if (Party) Story.Save(SecondPlayer, "Player2.tres");
        Story.Save(Enemy, "Enemy.tres");
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

    private static PalBattleVisualPlayer PlayerEntry(int roleId, string uri, Vector2I position) =>
        new() { RoleId = roleId, PlayerSpriteUri = uri, PlayerPosition = position };
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
