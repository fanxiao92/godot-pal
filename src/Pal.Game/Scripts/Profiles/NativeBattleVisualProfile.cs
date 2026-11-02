using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Godot;
using Pal.Game.Resources;
using static Pal.Game.Profiles.NativeContentDependencies;

namespace Pal.Game.Profiles;

/// <summary>Authored single-player/single-target visual binding; no battle or animation is advanced.</summary>
public static class NativeBattleVisualProfile
{
    public static CheckedBattleVisualProfile Load(string uri, CheckedStoryProfile story)
    {
        try
        {
            NativeExplorationProfile.ValidateUri(uri);
            Require(story is not null, "checked story is required");
            // URI strings defer heavy leaf loading; preflight also protects external metadata references.
            ContentDependencies(new[] { uri });
            var native = LoadResource<PalBattleVisualProfile>(uri);
            Require(native.FormatVersion == 1, "visual FormatVersion must be 1");
            Require(!string.IsNullOrWhiteSpace(native.SourceIdentity), "visual SourceIdentity is required (declaration only)");
            Require(native.RoleId == story!.BattleContent.Player.RoleId, "visual RoleId must match story player role");
            Position(native.PlayerPosition);
            Require(native.Teams is not null && native.Teams.All(x => x is not null), "visual Teams contains null");
            var teams = new Dictionary<int, PalBattleVisualTeam>();
            foreach (var team in native.Teams!)
            {
                Require(teams.TryAdd(team.TeamId, team), "duplicate visual TeamId");
                Require(story.BattleContent.Teams.TryGetValue(team.TeamId, out var enemy) && team.TargetId == enemy.Id,
                    "visual TeamId/TargetId does not match story battle target (not sprite ID)");
                Position(team.EnemyPosition);
                Require(team.IdleFrames is > 0 and <= ushort.MaxValue && Word(team.MagicFrames) && Word(team.AttackFrames)
                    && Word(team.ActWaitFrames), "invalid enemy frame metadata WORDs");
            }
            Require(story.Requirements.BattleTeamIds.ToHashSet().SetEquals(teams.Keys), "visual Teams must exactly cover selected battle teams");
            Require(native.ResourceSha256 is not null, "visual ResourceSha256 is required");
            var hashes = native.ResourceSha256!.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
            foreach (string path in hashes.Keys) CheckFile(path, hashes);
            var seeds = new[] { native.PlayerSpriteUri, native.PaletteUri }
                .Concat(teams.Values.SelectMany(x => new[] { x.BackgroundUri, x.EnemySpriteUri }))
                .Concat(Dependencies(uri)).ToArray();
            Require(!hashes.ContainsKey(uri), "visual root cannot hash itself");
            var dependencies = ContentDependencies(seeds, hashes);
            Require(dependencies.SetEquals(hashes.Keys), "visual ResourceSha256 must cover exactly native content dependencies");
            var palette = LoadResource<PalPalette>(native.PaletteUri);
            Require(palette.Rgb is { Length: 768 }, "visual palette requires RGB768");
            for (int i = 0; i < 256; i++)
            {
                var expected = new Color(palette.Rgb[i * 3] / 255f, palette.Rgb[i * 3 + 1] / 255f, palette.Rgb[i * 3 + 2] / 255f);
                Require(expected == story.Palette[i], "visual palette must match active story palette");
            }
            var player = LoadSprite(native.PlayerSpriteUri);
            Require(Enumerable.Range(0, 10).All(player.Frames.Select(x => x.Index).ToHashSet().Contains),
                "player requires addressable frames 0..9");
            var checkedTeams = new Dictionary<int, CheckedBattleVisualTeam>();
            foreach (var team in teams.Values)
            {
                var background = LoadResource<PalDialogueIndexedImage>(team.BackgroundUri);
                background.Validate();
                Require(!string.IsNullOrWhiteSpace(background.SourceIdentity) && background.Width == 320 && background.Height == 200,
                    "battle background requires SourceIdentity and 320x200 indexed planes");
                var sprite = LoadSprite(team.EnemySpriteUri);
                Require(Enumerable.Range(0, team.IdleFrames + team.MagicFrames + team.AttackFrames)
                    .All(sprite.Frames.Select(x => x.Index).ToHashSet().Contains), "enemy frame metadata references unaddressable frames");
                checkedTeams.Add(team.TeamId, new(team, background, sprite));
            }
            foreach (string path in hashes.Keys) CheckFile(path, hashes);
            return new(native, player, palette, checkedTeams, hashes);
        }
        catch (InvalidDataException error) { throw new InvalidDataException(uri + ": " + error.Message, error); }
        catch (Exception error) when (error is ArgumentException or NullReferenceException or InvalidCastException or InvalidOperationException)
        { throw new InvalidDataException(uri + ": malformed native battle visuals: " + error.Message, error); }
    }

    private static PalSprite LoadSprite(string uri)
    {
        var sprite = LoadResource<PalSprite>(uri);
        Require(Word(sprite.SpriteId) && sprite.Source is { Count: > 0 }, "sprite requires source declaration and WORD SpriteId");
        Require(sprite.DecodedByteLength is > 0 and <= 8 * 1024 * 1024 && sprite.FrameCount is > 0 and <= 4096 && sprite.Frames is not null,
            "invalid sprite length/FrameCount");
        var indices = new HashSet<int>();
        foreach (var frame in sprite.Frames!)
        {
            Require(frame is not null && Word(frame.Index) && indices.Add(frame.Index), "invalid or duplicate sprite frame Index");
            Require(frame!.SourceOffset >= 0 && frame.Width is > 0 and <= ushort.MaxValue && frame.Height is > 0 and <= ushort.MaxValue
                && (long)frame.Width * frame.Height <= 16 * 1024 * 1024 && frame.Indices is not null && frame.Alpha is not null
                && frame.Indices.Length == (long)frame.Width * frame.Height && frame.Alpha.Length == frame.Indices.Length,
                "invalid sprite frame dimensions/indices/alpha planes");
        }
        Require(Enumerable.Range(0, sprite.FrameCount).All(indices.Contains), "sprite nominal frame missing");
        return sprite;
    }
    private static T LoadResource<T>(string uri) where T : Resource =>
        ResourceLoader.Load(uri, cacheMode: ResourceLoader.CacheMode.IgnoreDeep) as T
        ?? throw new InvalidDataException(uri + ": expected " + typeof(T).Name);
    private static bool Word(int value) => value is >= 0 and <= ushort.MaxValue;
    private static void Position(Vector2I position) => Require(position.X is >= short.MinValue and <= short.MaxValue
        && position.Y is >= short.MinValue and <= short.MaxValue, "position requires explicit bottom-center SHORT coordinates");
    private static void Require(bool valid, string message) { if (!valid) throw new InvalidDataException(message); }
}

/// <summary>Native resources are isolated but mutable: consumers must treat them as prepared read-only data.</summary>
public sealed class CheckedBattleVisualProfile
{
    public string SourceIdentity { get; }
    public int RoleId { get; }
    public Vector2I PlayerPosition { get; }
    public PalSprite PlayerSprite { get; }
    public PalPalette Palette { get; }
    public IReadOnlyDictionary<int, CheckedBattleVisualTeam> Teams { get; }
    public IReadOnlyDictionary<string, string> ResourceSha256 { get; }
    internal CheckedBattleVisualProfile(PalBattleVisualProfile native, PalSprite player, PalPalette palette,
        Dictionary<int, CheckedBattleVisualTeam> teams, Dictionary<string, string> hashes)
    {
        SourceIdentity = native.SourceIdentity; RoleId = native.RoleId; PlayerPosition = native.PlayerPosition;
        PlayerSprite = player; Palette = palette;
        Teams = new ReadOnlyDictionary<int, CheckedBattleVisualTeam>(new Dictionary<int, CheckedBattleVisualTeam>(teams));
        ResourceSha256 = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(hashes, StringComparer.Ordinal));
    }
}

public sealed class CheckedBattleVisualTeam
{
    public int TeamId { get; }
    public int TargetId { get; }
    public Vector2I EnemyPosition { get; }
    public int IdleFrames { get; }
    public int MagicFrames { get; }
    public int AttackFrames { get; }
    public int ActWaitFrames { get; }
    public PalDialogueIndexedImage Background { get; }
    public PalSprite EnemySprite { get; }
    internal CheckedBattleVisualTeam(PalBattleVisualTeam native, PalDialogueIndexedImage background, PalSprite enemy)
    {
        TeamId = native.TeamId; TargetId = native.TargetId; EnemyPosition = native.EnemyPosition;
        IdleFrames = native.IdleFrames; MagicFrames = native.MagicFrames; AttackFrames = native.AttackFrames; ActWaitFrames = native.ActWaitFrames;
        Background = background; EnemySprite = enemy;
    }
}
