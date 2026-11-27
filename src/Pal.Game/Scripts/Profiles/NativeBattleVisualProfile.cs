using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Godot;
using Pal.Game.Resources;
using static Pal.Game.Profiles.NativeContentDependencies;
using Pal.Core.Battle;

namespace Pal.Game.Profiles;

/// <summary>Authored story visual binding addressed per story enemy slot (duplicate enemy
/// definition IDs stay separate slots); no battle or animation is advanced.</summary>
public static class NativeBattleVisualProfile
{
    // Reference SDLPal battle.c:27-31 g_rgPlayerPos bottom-center origins, indexed
    // [party size - 1][party position]; positions follow the party column, never RoleId.
    // This chain supports parties of one to three players.
    private static readonly Vector2I[][] PlayerLayout =
        [[new(240, 170)], [new(200, 176), new(256, 152)], [new(180, 180), new(234, 170), new(270, 146)]];

    public static CheckedBattleVisualProfile Load(string uri, CheckedStoryProfile story)
    {
        ArgumentNullException.ThrowIfNull(story);
        return Load(uri, story.BattleContent.Players.Select(player => player.RoleId).ToArray(),
            story.BattleContent.EnemyTeams, story.Requirements.BattleTeamIds, story.Palette);
    }

    /// <summary>Unified strict binding for story and non-story hosts: validates typed per-role
    /// player bindings against the ordered party the caller's verified battle content actually
    /// selected. Every party position is authenticated; there is no head-role fallback and no
    /// legacy scalar/shared-sprite form (old resources are re-prepared, not dual-loaded).</summary>
    public static CheckedBattleVisualProfile Load(string uri, IReadOnlyList<int> partyRoleIds,
        IReadOnlyDictionary<int, IReadOnlyList<Pal.Core.Battle.ClassicBattleEnemy>> enemyTeams,
        IReadOnlyList<int> requirementTeamIds, IReadOnlyList<Color> palette)
    {
        try
        {
            NativeExplorationProfile.ValidateUri(uri);
            // URI strings defer heavy leaf loading; preflight also protects external metadata references.
            ContentDependencies(new[] { uri });
            var native = LoadResource<PalBattleVisualProfile>(uri);
            Require(native.FormatVersion == 2, "visual FormatVersion must be 2; re-prepare legacy shared-sprite profiles explicitly");
            Require(!string.IsNullOrWhiteSpace(native.SourceIdentity), "visual SourceIdentity is required (declaration only)");
            Require(partyRoleIds is not null && partyRoleIds.Count is 1 or 2 or 3,
                "visual party requires one to three ordered role bindings (battle.c:27-31)");
            var declared = native.Players;
            Require(declared is not null && declared.Length > 0 && declared.All(x => x is not null),
                "visual Players requires typed per-role bindings (no legacy scalar form)");
            Require(declared!.Length == partyRoleIds!.Count,
                "visual Players must match the battle party one-to-one (party order preserved)");
            for (int member = 0; member < declared.Length; member++)
            {
                string memberPath = "visual Players[" + member + "]";
                var entry = declared[member]!;
                Require(Word(entry.RoleId) && entry.RoleId == partyRoleIds[member],
                    memberPath + ".RoleId must match the battle party role at position " + member + " (no leader fallback)");
                Position(entry.PlayerPosition);
                Require(entry.PlayerPosition == PlayerLayout[partyRoleIds.Count - 1][member],
                    memberPath + ".PlayerPosition must equal the reference party layout g_rgPlayerPos[" + (partyRoleIds.Count - 1) + "][" + member + "] (battle.c:27-31)");
                bool memberEffect = entry.AttackEffectSpriteUri != "" || entry.AttackEffectIndex != -1;
                Require(!memberEffect || (!string.IsNullOrWhiteSpace(entry.AttackEffectSpriteUri) && Word(entry.AttackEffectIndex)),
                    memberPath + ": attack effect requires paired SpriteUri and WORD AttackEffectIndex");
            }
            Require(native.Teams is not null && native.Teams.All(x => x is not null), "visual Teams contains null");
            var teams = new Dictionary<int, PalBattleVisualTeam>();
            var teamSlots = new Dictionary<int, (int TargetId, Vector2I EnemyPosition, bool IsEmpty)[]>();
            foreach (var team in native.Teams!)
            {
                Require(teams.TryAdd(team.TeamId, team), "duplicate visual TeamId");
                Require(enemyTeams.TryGetValue(team.TeamId, out var enemies)
                    && enemies.All(x => x is not null), "visual TeamId does not match a story battle team");
                string teamPath = "visual Teams[TeamId=" + team.TeamId + "]";
                var slots = team.EnemySlots ?? throw new InvalidDataException(teamPath + " requires visual EnemySlots");
                Require(slots.Length > 0, teamPath + " requires visual EnemySlots, including a single-enemy team; prepare it explicitly");
                Require(team.TargetId == -1 && team.EnemyPosition == new Vector2I(int.MinValue, int.MinValue),
                    teamPath + ": legacy TargetId/EnemyPosition cannot be mixed with EnemySlots");
                Require(team.EnemySpriteUri == "", teamPath + ": legacy team EnemySpriteUri cannot be mixed with EnemySlots; prepare per-slot sprite URIs explicitly");
                Require(slots.All(x => x is not null), teamPath + ": visual EnemySlots contains null");
                Require(slots.Length == enemies!.Count,
                    teamPath + ": visual EnemySlots must match the story team's enemy slots one-to-one (slot order preserved)");
                Require(team.IdleFrames == -1 && team.IdleAnimSpeed == -1 && team.MagicFrames == -1
                    && team.AttackFrames == -1 && team.ActWaitFrames == -1,
                    teamPath + ": legacy team frame metadata cannot be mixed with EnemySlots; prepare per-slot metadata explicitly");
                var checkedSlots = new (int TargetId, Vector2I EnemyPosition, bool IsEmpty)[slots.Length];
                for (int slot = 0; slot < slots.Length; slot++)
                {
                    Require(slots[slot].TargetId == enemies[slot].Id,
                        teamPath + ".EnemySlots[" + slot + "]: visual EnemySlots TargetId must match the story enemy slot Id (not sprite ID)");
                    Position(slots[slot].EnemyPosition);
                    if (enemies[slot].IsEmpty)
                    {
                        // #141: the numeric empty runtime slot keeps its overlay binding (TargetId 0,
                        // authored position, slot order) but never carries sprite or frame metadata;
                        // emptiness comes from the numeric state, never inferred from a missing sprite.
                        Require(slots[slot].EnemySpriteUri == "" && slots[slot].IdleFrames == -1 && slots[slot].IdleAnimSpeed == -1
                            && slots[slot].MagicFrames == -1 && slots[slot].AttackFrames == -1 && slots[slot].ActWaitFrames == -1,
                            teamPath + ".EnemySlots[" + slot + "]: an empty enemy slot must not declare sprite or frame metadata");
                    }
                    else
                    {
                        Require(!string.IsNullOrWhiteSpace(slots[slot].EnemySpriteUri),
                            teamPath + ".EnemySlots[" + slot + "]: EnemySpriteUri is required");
                        Require(slots[slot].IdleFrames is > 0 and <= ushort.MaxValue && Word(slots[slot].IdleAnimSpeed)
                            && Word(slots[slot].MagicFrames) && Word(slots[slot].AttackFrames) && Word(slots[slot].ActWaitFrames),
                            teamPath + ".EnemySlots[" + slot + "]: invalid or missing enemy frame metadata WORDs");
                    }
                    checkedSlots[slot] = (slots[slot].TargetId, slots[slot].EnemyPosition, enemies[slot].IsEmpty);
                }
                teamSlots.Add(team.TeamId, checkedSlots);
            }
            Require(requirementTeamIds.ToHashSet().SetEquals(teams.Keys), "visual Teams must exactly cover selected battle teams");
            Require(native.ResourceSha256 is not null, "visual ResourceSha256 is required");
            var hashes = native.ResourceSha256!.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
            foreach (string path in hashes.Keys) CheckFile(path, hashes);
            var seeds = declared.Select(x => x!.PlayerSpriteUri)
                .Concat(declared.Where(x => x!.AttackEffectSpriteUri != "").Select(x => x!.AttackEffectSpriteUri))
                .Concat(new[] { native.PaletteUri })
                .Concat(native.NumberSpriteUri != "" ? new[] { native.NumberSpriteUri } : Array.Empty<string>())
                .Concat(teams.Values.SelectMany(x => new[] { x.BackgroundUri }.Concat(x.EnemySlots.Select(slot => slot.EnemySpriteUri).Where(uri => uri != ""))))
                .Concat(Dependencies(uri)).ToArray();
            Require(!hashes.ContainsKey(uri), "visual root cannot hash itself");
            var dependencies = ContentDependencies(seeds, hashes);
            Require(dependencies.SetEquals(hashes.Keys), "visual ResourceSha256 must cover exactly native content dependencies");
            var paletteResource = LoadResource<PalPalette>(native.PaletteUri);
            Require(paletteResource.Rgb is { Length: 768 }, "visual palette requires RGB768");
            for (int i = 0; i < 256; i++)
            {
                var expected = new Color(paletteResource.Rgb[i * 3] / 255f, paletteResource.Rgb[i * 3 + 1] / 255f, paletteResource.Rgb[i * 3 + 2] / 255f);
                Require(expected == palette[i], "visual palette must match active story palette");
            }
            var players = new CheckedBattleVisualProfile.CheckedBattleVisualPlayer[declared.Length];
            for (int member = 0; member < declared.Length; member++)
            {
                string memberPath = "visual Players[" + member + "]";
                try
                {
                    var entry = declared[member]!;
                    var sprite = ReadAndValidateSprite(entry.PlayerSpriteUri);
                    Require(Enumerable.Range(0, 10).All(sprite.Frames.Select(x => x.Index).ToHashSet().Contains),
                        "player requires addressable frames 0..9");
                    PalSprite? effect = entry.AttackEffectSpriteUri != "" ? ReadAndValidateSprite(entry.AttackEffectSpriteUri) : null;
                    if (effect is not null)
                        Require(Enumerable.Range(entry.AttackEffectIndex * 3, 3).All(effect.Frames.Select(x => x.Index).ToHashSet().Contains),
                            "attack effect references unaddressable frames");
                    players[member] = new(entry.RoleId, entry.PlayerPosition, sprite, entry.AttackEffectIndex, effect,
                        Array.AsReadOnly(effect is null ? Array.Empty<PalFrame>() : Enumerable.Range(entry.AttackEffectIndex * 3, 3)
                            .Select(index => effect.Frames.Single(frame => frame.Index == index)).ToArray()));
                }
                catch (InvalidDataException error)
                {
                    throw new InvalidDataException(memberPath + ": " + error.Message, error);
                }
            }
            PalSprite? numbers = native.NumberSpriteUri != "" ? ReadAndValidateSprite(native.NumberSpriteUri) : null;
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
                var slots = teamSlots[team.TeamId].Select((slot, ordinal) =>
                {
                    try
                    {
                        if (slot.IsEmpty)
                            return new CheckedBattleVisualEnemySlot(slot.TargetId, slot.EnemyPosition, null, 0, 0, 0, 0, 0)
                                { IsEmpty = true };
                        var metadata = team.EnemySlots[ordinal];
                        var sprite = ReadAndValidateSprite(metadata.EnemySpriteUri);
                        Require(Enumerable.Range(0, metadata.IdleFrames + metadata.MagicFrames + metadata.AttackFrames)
                            .All(sprite.Frames.Select(x => x.Index).ToHashSet().Contains), "enemy frame metadata references unaddressable frames");
                        return new CheckedBattleVisualEnemySlot(slot.TargetId, slot.EnemyPosition, sprite,
                            metadata.IdleFrames, metadata.IdleAnimSpeed, metadata.MagicFrames, metadata.AttackFrames, metadata.ActWaitFrames);
                    }
                    catch (InvalidDataException error)
                    {
                        throw new InvalidDataException("visual Teams[TeamId=" + team.TeamId + "].EnemySlots[" + ordinal + "]: " + error.Message, error);
                    }
                }).ToArray();
                checkedTeams.Add(team.TeamId, new(team.TeamId, slots, background));
            }
            foreach (string path in hashes.Keys) CheckFile(path, hashes);
            return new(native, paletteResource, numbers, players, checkedTeams, hashes);
        }
        catch (InvalidDataException error) { throw new InvalidDataException(uri + ": " + error.Message, error); }
        catch (Exception error) when (error is ArgumentException or NullReferenceException or InvalidCastException or InvalidOperationException)
        { throw new InvalidDataException(uri + ": malformed native battle visuals: " + error.Message, error); }
    }

    /// <summary>Loads isolated native indexed sprite data with compatibility structural validation.
    /// Source remains a declaration; null textures and extra addressable aliases remain legal.</summary>
    public static PalSprite LoadSprite(string uri)
    {
        try
        {
            NativeExplorationProfile.ValidateUri(uri);
            ContentDependencies(new[] { uri });
            return ReadAndValidateSprite(uri);
        }
        catch (InvalidDataException error) { throw new InvalidDataException(uri + ": " + error.Message, error); }
        catch (Exception error) when (error is ArgumentException or NullReferenceException or InvalidCastException or InvalidOperationException)
        { throw new InvalidDataException(uri + ": malformed native sprite: " + error.Message, error); }
    }

    /// <summary>Checks sprite/frame scalars, the actual frame Index set (including aliases),
    /// indexed planes and typed Source against an owned managed snapshot captured before target I/O.
    /// Frame collection order is not semantic; nominal FrameCount does not count extra aliases.
    /// Each Source tree is limited to depth 64 (root depth 0) and 65536 visited Variant values,
    /// including dictionary keys; children and keys are at parent depth + 1. Repeated aliases
    /// are copied by value. Cycles or otherwise valid trees exceeding either bound are rejected
    /// as possibly cyclic, without claiming cycle identity detection. Compatibility/full-profile
    /// loading does not acquire these limits. An owned ordinal hash snapshot authenticates the
    /// prepared native content dependency graph, including the sprite root, and exact manifest
    /// membership before loading and after field/Source comparison. Optional Texture identity is
    /// captured as normalized native URI, native type and frame dimensions, never a Resource
    /// reference. Null must match null; non-null textures must match this full descriptor.</summary>
    public static PalSprite LoadSprite(string uri, PalSprite expected, IReadOnlyDictionary<string, string> resourceSha256)
    {
        try
        {
            var expectation = SnapshotSpriteExpectation(expected);
            if (resourceSha256 is null) throw new InvalidDataException("sprite manifest is required");
            var ownedHashes = resourceSha256.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
            var dependencies = ContentDependencies(new[] { uri }, ownedHashes);
            Require(dependencies.SetEquals(ownedHashes.Keys), "sprite manifest must cover exactly native content dependencies");
            var sprite = LoadSprite(uri);
            CompareSpriteExpectation(expectation, sprite);
            int visited = 0;
            var actualSource = SnapshotSpriteSource(sprite.Source, "Source", 0, ref visited);
            CompareSpriteSource(expectation.Source, actualSource, "Source");
            dependencies = ContentDependencies(new[] { uri }, ownedHashes);
            Require(dependencies.SetEquals(ownedHashes.Keys), "sprite manifest must cover exactly native content dependencies");
            return sprite;
        }
        catch (InvalidDataException error) { throw new InvalidDataException(uri + ": " + error.Message, error); }
        catch (Exception error) when (error is ArgumentException or NullReferenceException or InvalidCastException or InvalidOperationException)
        { throw new InvalidDataException(uri + ": malformed native sprite expectation/manifest: " + error.Message, error); }
    }

    private sealed record SpriteExpectation(int SpriteId, int DecodedByteLength, int FrameCount,
        IReadOnlyDictionary<int, SpriteFrameExpectation> Frames, SpriteSourceValue Source);
    private sealed record SpriteFrameExpectation(int SourceOffset, int Width, int Height,
        IReadOnlyList<byte> Indices, IReadOnlyList<byte> Alpha, SpriteTextureExpectation? Texture);
    private sealed record SpriteTextureExpectation(string Uri, string NativeType, int Width, int Height);

    private static SpriteExpectation SnapshotSpriteExpectation(PalSprite? expected)
    {
        if (expected is null) throw new InvalidDataException("expected sprite is required");
        int spriteId = expected.SpriteId, decodedByteLength = expected.DecodedByteLength, frameCount = expected.FrameCount;
        Require(Word(spriteId), "expected SpriteId must be WORD");
        var metadata = expected.Source ?? throw new InvalidDataException("expected Source is required");
        Require(metadata.Count > 0, "expected Source declaration is required");
        Require(decodedByteLength is > 0 and <= 8 * 1024 * 1024, "expected DecodedByteLength is invalid");
        Require(frameCount is > 0 and <= 4096, "expected FrameCount is invalid");
        var nativeFrames = expected.Frames ?? throw new InvalidDataException("expected Frames is required");
        var frames = new Dictionary<int, SpriteFrameExpectation>();
        // Preserve the shared count/offset/alpha policy; Texture identity is checked-mode only.
        foreach (var frame in nativeFrames)
        {
            if (frame is null) throw new InvalidDataException("expected Frames contains null frame");
            int index = frame.Index;
            Require(Word(index) && !frames.ContainsKey(index), "expected frame Index is invalid or duplicate: " + index);
            string path = "expected Frames[Index=" + index + "]";
            int sourceOffset = frame.SourceOffset, width = frame.Width, height = frame.Height;
            Require(sourceOffset >= 0, path + ": SourceOffset must be nonnegative");
            Require(width is > 0 and <= ushort.MaxValue, path + ": Width is invalid");
            Require(height is > 0 and <= ushort.MaxValue, path + ": Height is invalid");
            long pixels = (long)width * height;
            Require(pixels <= 16 * 1024 * 1024, path + ": Width/Height exceed plane size bound");
            var indices = frame.Indices ?? throw new InvalidDataException(path + ": Indices plane is required");
            var alpha = frame.Alpha ?? throw new InvalidDataException(path + ": Alpha plane is required");
            Require(indices.Length == pixels, path + ": Indices plane length must match Width/Height");
            Require(alpha.Length == indices.Length, path + ": Alpha plane length must match Indices");
            frames.Add(index, new(sourceOffset, width, height,
                Array.AsReadOnly(indices.ToArray()), Array.AsReadOnly(alpha.ToArray()),
                SnapshotSpriteTexture(frame.Texture, path, width, height)));
        }
        Require(Enumerable.Range(0, frameCount).All(frames.ContainsKey), "expected sprite nominal frame Index missing");
        int visited = 0;
        var source = SnapshotSpriteSource(metadata, "Source", 0, ref visited);
        return new(spriteId, decodedByteLength, frameCount,
            new ReadOnlyDictionary<int, SpriteFrameExpectation>(frames), source);
    }

    private static SpriteTextureExpectation? SnapshotSpriteTexture(Texture2D? texture, string path, int width, int height)
    {
        if (texture is null) return null;
        string uri = texture.ResourcePath;
        try { NativeExplorationProfile.ValidateUri(uri); }
        catch (InvalidDataException error) { throw new InvalidDataException(path + ": Texture URI: " + error.Message, error); }
        int textureWidth = texture.GetWidth(), textureHeight = texture.GetHeight();
        Require(textureWidth == width && textureHeight == height, path + ": Texture dimensions must match frame Width/Height");
        return new(uri, texture.GetClass(), textureWidth, textureHeight);
    }

    private static void CompareSpriteExpectation(SpriteExpectation expected, PalSprite actual)
    {
        Require(expected.SpriteId == actual.SpriteId, "SpriteId differs from expected");
        Require(expected.DecodedByteLength == actual.DecodedByteLength, "DecodedByteLength differs from expected");
        Require(expected.FrameCount == actual.FrameCount, "FrameCount differs from expected");
        // The shared core has already rejected null, duplicate and non-WORD actual indices.
        var frames = actual.Frames.ToDictionary(frame => frame.Index);
        Require(expected.Frames.Keys.ToHashSet().SetEquals(frames.Keys), "Frames Index set differs from expected (including aliases)");
        foreach (var entry in expected.Frames)
        {
            string path = "Frames[Index=" + entry.Key + "]";
            var frame = frames[entry.Key];
            var wanted = entry.Value;
            Require(wanted.SourceOffset == frame.SourceOffset, path + ": SourceOffset differs from expected");
            Require(wanted.Width == frame.Width, path + ": Width differs from expected");
            Require(wanted.Height == frame.Height, path + ": Height differs from expected");
            Require(wanted.Indices.SequenceEqual(frame.Indices), path + ": Indices plane differs from expected");
            Require(wanted.Alpha.SequenceEqual(frame.Alpha), path + ": Alpha plane differs from expected");
            var texture = SnapshotSpriteTexture(frame.Texture, path, frame.Width, frame.Height);
            if (wanted.Texture is not { } wantedTexture)
                Require(texture is null, path + ": Texture must be null as expected");
            else
            {
                if (texture is null) throw new InvalidDataException(path + ": Texture is required as expected");
                Require(wantedTexture.Uri == texture.Uri, path + ": Texture URI differs from expected");
                Require(wantedTexture.NativeType == texture.NativeType, path + ": Texture native type differs from expected");
                Require(wantedTexture.Width == texture.Width && wantedTexture.Height == texture.Height,
                    path + ": Texture dimensions differ from expected");
            }
        }
    }

    // These private snapshots contain only immutable managed scalars and owned read-only trees,
    // never a Variant or an aliased Godot collection. Type tags prevent numeric/string coercion.
    private abstract record SpriteSourceValue(Variant.Type Type);
    private sealed record SpriteSourceScalar(Variant.Type Type, object? Value) : SpriteSourceValue(Type);
    private sealed record SpriteSourceDictionary(IReadOnlyDictionary<string, SpriteSourceValue> Fields)
        : SpriteSourceValue(Variant.Type.Dictionary);
    private sealed record SpriteSourceArray(IReadOnlyList<SpriteSourceValue> Items)
        : SpriteSourceValue(Variant.Type.Array);

    private static void VisitSpriteSource(string path, int depth, ref int visited)
    {
        Require(depth <= 64, path + ": Source metadata exceeds depth bound (possibly cyclic)");
        Require(++visited <= 65536, path + ": Source metadata exceeds work bound (possibly cyclic)");
    }

    private static SpriteSourceValue SnapshotSpriteSource(Variant value, string path, int depth, ref int visited)
    {
        VisitSpriteSource(path, depth, ref visited);
        switch (value.VariantType)
        {
            case Variant.Type.Nil: return new SpriteSourceScalar(Variant.Type.Nil, null);
            case Variant.Type.Bool: return new SpriteSourceScalar(Variant.Type.Bool, value.AsBool());
            case Variant.Type.Int: return new SpriteSourceScalar(Variant.Type.Int, value.AsInt64());
            case Variant.Type.Float:
                double number = value.AsDouble();
                Require(double.IsFinite(number), path + ": Source Float must be finite JSON-like metadata");
                return new SpriteSourceScalar(Variant.Type.Float, number);
            case Variant.Type.String: return new SpriteSourceScalar(Variant.Type.String, value.AsString());
            case Variant.Type.Dictionary:
                var fields = new Dictionary<string, SpriteSourceValue>(StringComparer.Ordinal);
                foreach (var entry in value.AsGodotDictionary())
                {
                    VisitSpriteSource(path + "[key]", depth + 1, ref visited);
                    Require(entry.Key.VariantType == Variant.Type.String, path + "[key]: Source dictionary key must be String");
                    string key = entry.Key.AsString();
                    string fieldPath = SpriteSourceFieldPath(path, key);
                    Require(fields.TryAdd(key, SnapshotSpriteSource(entry.Value, fieldPath, depth + 1, ref visited)),
                        fieldPath + ": duplicate Source String key");
                }
                return new SpriteSourceDictionary(new ReadOnlyDictionary<string, SpriteSourceValue>(fields));
            case Variant.Type.Array:
                var items = new List<SpriteSourceValue>();
                foreach (var item in value.AsGodotArray())
                    items.Add(SnapshotSpriteSource(item, path + "[" + items.Count + "]", depth + 1, ref visited));
                return new SpriteSourceArray(items.AsReadOnly());
            default:
                throw new InvalidDataException(path + ": unsupported Source Variant type " + value.VariantType);
        }
    }

    private static string SpriteSourceFieldPath(string path, string key) => path + "[\""
        + key.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t") + "\"]";

    private static void CompareSpriteSource(SpriteSourceValue expected, SpriteSourceValue actual, string path)
    {
        Require(expected.Type == actual.Type, path + ": Source Variant type differs from expected " + expected.Type);
        switch (expected)
        {
            case SpriteSourceScalar scalar:
                Require(Equals(scalar.Value, ((SpriteSourceScalar)actual).Value), path + ": Source value differs from expected");
                break;
            case SpriteSourceDictionary dictionary:
                var fields = ((SpriteSourceDictionary)actual).Fields;
                foreach (var field in dictionary.Fields)
                {
                    string fieldPath = SpriteSourceFieldPath(path, field.Key);
                    Require(fields.ContainsKey(field.Key), fieldPath + ": expected Source key is missing");
                    CompareSpriteSource(field.Value, fields[field.Key], fieldPath);
                }
                foreach (string key in fields.Keys)
                    Require(dictionary.Fields.ContainsKey(key), SpriteSourceFieldPath(path, key) + ": unexpected Source key");
                break;
            case SpriteSourceArray array:
                var items = ((SpriteSourceArray)actual).Items;
                Require(array.Items.Count == items.Count, path + ": Source array length differs from expected");
                for (int index = 0; index < array.Items.Count; index++)
                    CompareSpriteSource(array.Items[index], items[index], path + "[" + index + "]");
                break;
        }
    }

    // Full visual loading keeps its existing root diagnostic order and hash flow through this core.
    private static PalSprite ReadAndValidateSprite(string uri)
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
    public PalPalette Palette { get; }
    public PalSprite? NumberSprite { get; }
    // UI digits use top-left coordinates; six pixels is layout advance, not a frame-size constraint.
    public IReadOnlyList<PalFrame> YellowNumberFrames { get; }
    public IReadOnlyList<PalFrame> BlueNumberFrames { get; }
    public IReadOnlyList<PalFrame> CyanNumberFrames { get; }
    /// <summary>Party-ordered per-role bindings; a single player is one binding, never a head
    /// projection. Shared sprites remain distinct bindings (identity/poses are not merged).</summary>
    public IReadOnlyList<CheckedBattleVisualPlayer> Players { get; }

    /// <summary>Per-role checked binding: one party position's RoleId, own sprite, own
    /// reference-layout column and own attack-effect frames.</summary>
    public sealed record CheckedBattleVisualPlayer(int RoleId, Vector2I PlayerPosition, PalSprite PlayerSprite,
        int AttackEffectIndex, PalSprite? AttackEffectSprite, IReadOnlyList<PalFrame> AttackEffectFrames)
    {
        public void RequireReferencePresentation()
        {
            if (AttackEffectSprite is null) throw new InvalidDataException("reference presentation requires attack effect metadata");
        }
    }

    public void RequireNumberPresentation()
    {
        if (NumberSprite is null) throw new InvalidDataException("number presentation requires NumberSpriteUri");
    }

    public void RequireReferencePresentation()
    {
        for (int member = 0; member < Players.Count; member++) Players[member].RequireReferencePresentation();
    }
    public IReadOnlyDictionary<int, CheckedBattleVisualTeam> Teams { get; }
    public IReadOnlyDictionary<string, string> ResourceSha256 { get; }

    /// <summary>Reference bottom-center player origins for the actual party size
    /// (battle.c:27-31 g_rgPlayerPos): the per-role bindings' validated columns.</summary>
    public IReadOnlyList<Vector2I> PlayerOrigins(int partyCount) => partyCount switch
    {
        1 => [Players[0].PlayerPosition],
        2 => [Players[0].PlayerPosition, Players[1].PlayerPosition],
        3 => [Players[0].PlayerPosition, Players[1].PlayerPosition, Players[2].PlayerPosition],
        _ => throw new InvalidOperationException("Unsupported party size " + partyCount + ".")
    };
    internal CheckedBattleVisualProfile(PalBattleVisualProfile native, PalPalette palette, PalSprite? numbers,
        CheckedBattleVisualPlayer[] players, Dictionary<int, CheckedBattleVisualTeam> teams, Dictionary<string, string> hashes)
    {
        SourceIdentity = native.SourceIdentity; Palette = palette;
        Players = Array.AsReadOnly(players);
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
    public PalDialogueIndexedImage Background { get; }
    internal CheckedBattleVisualTeam(int teamId, CheckedBattleVisualEnemySlot[] slots, PalDialogueIndexedImage background)
    {
        TeamId = teamId; EnemySlots = Array.AsReadOnly(slots); Background = background;
    }
}

/// <summary>One ordinal binding. Prepared sprite data may be shared; live presentation state may not.
/// A numeric-empty runtime slot keeps its overlay (TargetId 0, authored position, slot order)
/// with no sprite or frame metadata and never draws, acts or targets.</summary>
public sealed record CheckedBattleVisualEnemySlot(int TargetId, Vector2I EnemyPosition, PalSprite? EnemySprite,
    int IdleFrames, int IdleAnimSpeed, int MagicFrames, int AttackFrames, int ActWaitFrames)
{
    /// <summary>True only for a numeric-empty runtime slot (wObjectID 0); a killed real enemy
    /// keeps its slot identity and stays IsEmpty=false, so the two zero-HP kinds never merge.</summary>
    public bool IsEmpty { get; init; }
}
