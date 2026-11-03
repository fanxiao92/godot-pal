using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Godot;
using Pal.Game.Resources;
using static Pal.Game.Profiles.NativeContentDependencies;

namespace Pal.Game.Profiles;

/// <summary>Authored story visual binding addressed per story enemy slot (duplicate enemy
/// definition IDs stay separate slots); no battle or animation is advanced.</summary>
public static class NativeBattleVisualProfile
{
    // Reference SDLPal battle.c:27-31 g_rgPlayerPos bottom-center origins. This chain
    // supports parties of one or two; index is party size minus one, column zero is the head.
    private static readonly Vector2I[] PlayerOrigins = [new(240, 170), new(200, 176)];

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
            Require(native.RoleId == story!.BattleContent.Players[0].RoleId, "visual RoleId must match story player role");
            Position(native.PlayerPosition);
            Require(native.PlayerPosition == PlayerOrigins[story.BattleContent.Players.Count - 1],
                "player position must equal the reference party layout origin g_rgPlayerPos[party-1][0] (battle.c:27-31)");
            Require(native.Teams is not null && native.Teams.All(x => x is not null), "visual Teams contains null");
            var teams = new Dictionary<int, PalBattleVisualTeam>();
            var teamSlots = new Dictionary<int, CheckedBattleVisualEnemySlot[]>();
            foreach (var team in native.Teams!)
            {
                Require(teams.TryAdd(team.TeamId, team), "duplicate visual TeamId");
                Require(story.BattleContent.EnemyTeams.TryGetValue(team.TeamId, out var enemies)
                    && enemies.All(x => x is not null), "visual TeamId does not match a story battle team");
                if (team.EnemySlots is { Length: > 0 } slots)
                {
                    Require(slots.All(x => x is not null), "visual EnemySlots contains null");
                    Require(slots.Length == enemies!.Count,
                        "visual EnemySlots must match the story team's enemy slots one-to-one (slot order preserved)");
                    var checkedSlots = new CheckedBattleVisualEnemySlot[slots.Length];
                    for (int slot = 0; slot < slots.Length; slot++)
                    {
                        Require(slots[slot].TargetId == enemies[slot].Id,
                            "visual EnemySlots TargetId must match the story enemy slot Id (not sprite ID)");
                        Position(slots[slot].EnemyPosition);
                        checkedSlots[slot] = new(slots[slot].TargetId, slots[slot].EnemyPosition);
                    }
                    teamSlots.Add(team.TeamId, checkedSlots);
                }
                else
                {
                    // Legacy single pair. A multi-enemy story team must not collapse into it:
                    // slot identities would be merged or silently dropped.
                    Require(enemies!.Count == 1, "a multi-enemy story team requires visual EnemySlots, not the legacy single pair");
                    Require(team.TargetId == enemies[0].Id, "visual TeamId/TargetId does not match story battle target (not sprite ID)");
                    Position(team.EnemyPosition);
                    teamSlots.Add(team.TeamId, [new(team.TargetId, team.EnemyPosition)]);
                }
                Require(team.IdleFrames is > 0 and <= ushort.MaxValue && Word(team.MagicFrames) && Word(team.AttackFrames)
                    && Word(team.ActWaitFrames) && (team.IdleAnimSpeed == -1 || Word(team.IdleAnimSpeed)), "invalid enemy frame metadata WORDs");
            }
            Require(story.Requirements.BattleTeamIds.ToHashSet().SetEquals(teams.Keys), "visual Teams must exactly cover selected battle teams");
            Require(native.ResourceSha256 is not null, "visual ResourceSha256 is required");
            var hashes = native.ResourceSha256!.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
            foreach (string path in hashes.Keys) CheckFile(path, hashes);
            bool hasEffect = native.AttackEffectSpriteUri != "" || native.AttackEffectIndex != -1;
            Require(!hasEffect || (!string.IsNullOrWhiteSpace(native.AttackEffectSpriteUri) && Word(native.AttackEffectIndex)),
                "attack effect requires paired SpriteUri and WORD AttackEffectIndex");
            var seeds = new[] { native.PlayerSpriteUri, native.PaletteUri }
                .Concat(hasEffect ? new[] { native.AttackEffectSpriteUri } : Array.Empty<string>())
                .Concat(native.NumberSpriteUri != "" ? new[] { native.NumberSpriteUri } : Array.Empty<string>())
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
            PalSprite? effect = hasEffect ? LoadSprite(native.AttackEffectSpriteUri) : null;
            if (effect is not null)
                Require(Enumerable.Range(native.AttackEffectIndex * 3, 3).All(effect.Frames.Select(x => x.Index).ToHashSet().Contains),
                    "attack effect references unaddressable frames");
            PalSprite? numbers = native.NumberSpriteUri != "" ? LoadSprite(native.NumberSpriteUri) : null;
            if (numbers is not null)
            {
                var indices = numbers.Frames.Select(frame => frame.Index).ToHashSet();
                Require(new[] { 19, 29, 56 }.All(start => Enumerable.Range(start, 10).All(indices.Contains)),
                    "number sprite requires addressable Yellow19..28, Blue29..38 and Cyan56..65 digits");
            }
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
                checkedTeams.Add(team.TeamId, new(team, teamSlots[team.TeamId], background, sprite));
            }
            foreach (string path in hashes.Keys) CheckFile(path, hashes);
            return new(native, player, palette, effect, numbers, checkedTeams, hashes);
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
    public int AttackEffectIndex { get; }
    public PalSprite? AttackEffectSprite { get; }
    public IReadOnlyList<PalFrame> AttackEffectFrames { get; }
    public PalSprite? NumberSprite { get; }
    // UI digits use top-left coordinates; six pixels is layout advance, not a frame-size constraint.
    public IReadOnlyList<PalFrame> YellowNumberFrames { get; }
    public IReadOnlyList<PalFrame> BlueNumberFrames { get; }
    public IReadOnlyList<PalFrame> CyanNumberFrames { get; }

    public void RequireNumberPresentation()
    {
        if (NumberSprite is null) throw new InvalidDataException("number presentation requires NumberSpriteUri");
    }

    public void RequireReferencePresentation()
    {
        if (AttackEffectSprite is null) throw new InvalidDataException("reference presentation requires attack effect metadata");
        if (Teams.Values.Any(t => t.IdleAnimSpeed < 0)) throw new InvalidDataException("reference presentation requires selected enemy IdleAnimSpeed");
    }
    public IReadOnlyDictionary<int, CheckedBattleVisualTeam> Teams { get; }
    public IReadOnlyDictionary<string, string> ResourceSha256 { get; }
    internal CheckedBattleVisualProfile(PalBattleVisualProfile native, PalSprite player, PalPalette palette, PalSprite? effect, PalSprite? numbers,
        Dictionary<int, CheckedBattleVisualTeam> teams, Dictionary<string, string> hashes)
    {
        SourceIdentity = native.SourceIdentity; RoleId = native.RoleId; PlayerPosition = native.PlayerPosition;
        PlayerSprite = player; Palette = palette;
        AttackEffectIndex = native.AttackEffectIndex; AttackEffectSprite = effect;
        AttackEffectFrames = Array.AsReadOnly(effect is null ? Array.Empty<PalFrame>() : Enumerable.Range(AttackEffectIndex * 3, 3)
            .Select(index => effect.Frames.Single(frame => frame.Index == index)).ToArray());
        NumberSprite = numbers;
        YellowNumberFrames = NumberFrames(19); BlueNumberFrames = NumberFrames(29); CyanNumberFrames = NumberFrames(56);
        IReadOnlyList<PalFrame> NumberFrames(int start) => Array.AsReadOnly(numbers is null ? Array.Empty<PalFrame>() :
            Enumerable.Range(start, 10).Select(index => numbers.Frames.Single(frame => frame.Index == index)).ToArray());
        Teams = new ReadOnlyDictionary<int, CheckedBattleVisualTeam>(new Dictionary<int, CheckedBattleVisualTeam>(teams));
        ResourceSha256 = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(hashes, StringComparer.Ordinal));
    }
}

public sealed class CheckedBattleVisualTeam
{
    public int TeamId { get; }
    /// <summary>Story-slot-ordered bindings; duplicate target IDs stay separate slots.</summary>
    public IReadOnlyList<CheckedBattleVisualEnemySlot> EnemySlots { get; }
    public int TargetId => SingleSlot().TargetId;
    public Vector2I EnemyPosition => SingleSlot().EnemyPosition;
    public int IdleFrames { get; }
    public int IdleAnimSpeed { get; }
    public int MagicFrames { get; }
    public int AttackFrames { get; }
    public int ActWaitFrames { get; }
    public PalDialogueIndexedImage Background { get; }
    public PalSprite EnemySprite { get; }
    private CheckedBattleVisualEnemySlot SingleSlot() => EnemySlots.Count == 1 ? EnemySlots[0]
        : throw new InvalidOperationException("Single-slot projection cannot address multi-enemy visual slots; use EnemySlots.");
    internal CheckedBattleVisualTeam(PalBattleVisualTeam native, CheckedBattleVisualEnemySlot[] slots,
        PalDialogueIndexedImage background, PalSprite enemy)
    {
        TeamId = native.TeamId; EnemySlots = Array.AsReadOnly(slots);
        IdleAnimSpeed = native.IdleAnimSpeed;
        IdleFrames = native.IdleFrames; MagicFrames = native.MagicFrames; AttackFrames = native.AttackFrames; ActWaitFrames = native.ActWaitFrames;
        Background = background; EnemySprite = enemy;
    }
}

public sealed record CheckedBattleVisualEnemySlot(int TargetId, Vector2I EnemyPosition);
