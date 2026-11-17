using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Pal.Core;
using Pal.Game.Resources;

namespace Pal.Game.Profiles;

/// <summary>Typed numeric resources only. No provenance authentication or selected-entry capability grant.</summary>
public static class NativeBattleContent
{
    public static TriggerBattleContent Load(string uri) => LoadChecked(uri).Content;

    public static CheckedNativeBattleContent LoadChecked(string uri)
    {
        try
        {
            NativeExplorationProfile.ValidateUri(uri);
            if (!ResourceLoader.Exists(uri)) throw new InvalidDataException("native battle content missing; prepare it explicitly");
            var native = ResourceLoader.Load(uri, cacheMode: ResourceLoader.CacheMode.IgnoreDeep) as PalBattleContent
                ?? throw new InvalidDataException("expected PalBattleContent");
            return CreateChecked(native, uri);
        }
        catch (InvalidDataException e) { throw new InvalidDataException(uri + ": " + e.Message, e); }
    }

    public static TriggerBattleContent CreateCore(PalBattleContent native, string identity = "native battle content") =>
        CreateChecked(native, identity).Content;

    public static CheckedNativeBattleContent CreateChecked(PalBattleContent native, string identity = "native battle content")
    {
        try
        {
            if (native is null || native.FormatVersion != 1) throw new InvalidDataException("FormatVersion must be 1");
            var roles = Items(native.Roles, "Roles").Select(NativeBattleRoleAdapter.Read).ToArray();
            var players = Items(native.Players, "Players").Select(player =>
            {
                if (!player.HasCoveredByRoleId && player.CoveredByRoleId != 0)
                    throw new InvalidDataException("Player.CoveredByRoleId must be 0 when HasCoveredByRoleId is false");
                if (!player.HasNameObjectId && player.NameObjectId != 0)
                    throw new InvalidDataException("Player.NameObjectId must be 0 when HasNameObjectId is false");
                return ReadPlayer(player);
            }).ToArray();
            var growth = new Dictionary<int, ClassicBattleGrowthData>();
            foreach (var row in Items(native.Growth, "Growth"))
                if (!growth.TryAdd(row.RoleId, new(Copy(row.LevelUpExperience, "Growth.LevelUpExperience"),
                    Items(row.LevelUpMagics, "Growth.LevelUpMagics").Select(x => new BattleLevelUpMagic(x.Level, x.MagicId)).ToArray())))
                    throw new InvalidDataException("duplicate Growth.RoleId");
            var teams = new Dictionary<int, IReadOnlyList<ClassicBattleEnemy>>();
            foreach (var team in Items(native.Teams, "Teams"))
            {
                var enemies = Items(team.Enemies, "Team.Enemies").Select(ReadEnemy).ToArray();
                if (!teams.TryAdd(team.TeamId, enemies))
                    throw new InvalidDataException("duplicate TeamId");
                // Native reads keep an explicit outer loaded-slot boundary aligned with Core's
                // reference capacity (palcommon.h MAX_ENEMIES_IN_TEAM = 5): 0 and >5 stay rejected
                // before any RNG or gameplay side effects, with teamId diagnostics. #141: the count
                // covers mixed runtime slots; #140's canonical empty spelling is validated with the
                // numeric contract, and at least one non-empty enemy is enforced at battle start.
                if (enemies.Length is < 1 or > 5)
                    throw new InvalidDataException($"Team {team.TeamId} requires one to five runtime enemy slots");
            }
            var content = new TriggerBattleContent(roles, players, growth, teams, ReadGrid(native.RoleEquipment), ReadEquipments(native.Equipments),
                Items(native.Roles, "Roles").Where(role => role.PanelDeclared).Select(role => role.RoleId).ToArray());
            ModernBattleContent.Validate(content);
            return new(content, ReadProjection(native, content));
        }
        catch (Exception e) when (e is InvalidDataException or ArgumentException or NullReferenceException)
        { throw new InvalidDataException(identity + ": malformed typed native battle content: " + e.Message, e); }
    }

    public static PalBattleContent Write(TriggerBattleContent content) => Write(content, null);

    public static PalBattleContent Write(TriggerBattleContent content, BattleProjectionSourceInfo? projectedSource)
    {
        ModernBattleContent.Validate(content);
        projectedSource?.ValidateFor(content);
        // #141 (T13c): the native transport carries #140's typed empty runtime slots symmetrically
        // (PalBattleEnemy.IsEmpty); the old T13b boundary that rejected them before this chain
        // migrated is intentionally removed, never silently dropping the flag.
        // Without a projection receipt this is the non-projected native/Prepare chain: it keeps
        // the same explicit outer 1..5 runtime-slot boundary the projection slot rows enforce.
        if (projectedSource is null)
            foreach (var team in content.EnemyTeams)
                if (team.Value.Count is < 1 or > 5)
                    throw new InvalidDataException($"Team {team.Key} requires one to five runtime enemy slots");
        return new()
        {
            FormatVersion = 1,
            ProjectionPresent = projectedSource is not null,
            ProjectionFormatVersion = projectedSource?.FormatVersion ?? -1,
            ProjectionKind = projectedSource?.Kind ?? "",
            ProjectionSnapshotKind = projectedSource?.SnapshotKind ?? "",
            ProjectionReceiptSha256 = projectedSource?.ReceiptSha256 ?? "",
            ProjectionReceiptByteLength = projectedSource?.ReceiptByteLength ?? -1,
            ProjectionNumericSha256 = projectedSource?.NumericSha256 ?? "",
            ProjectionNumericByteLength = projectedSource?.NumericByteLength ?? -1,
            ProjectionInputFiles = projectedSource?.ModernInputs.Select(row => row.File).ToArray() ?? [],
            ProjectionInputSha256 = projectedSource?.ModernInputs.Select(row => row.Sha256).ToArray() ?? [],
            ProjectionInputByteLengths = projectedSource?.ModernInputs.Select(row => row.ByteLength).ToArray() ?? [],
            ProjectionRoleIds = projectedSource?.RoleIds.ToArray() ?? [],
            ProjectionTeamIds = projectedSource?.TeamIds.ToArray() ?? [],
            ProjectionEnemySlots = projectedSource?.EnemySlots.SelectMany(row => new[] { row.TeamId, row.SourceSlot,
                row.ObjectId, row.EnemyId ?? -1, row.CompactSlot ?? -1, row.BattleId ?? -1,
                row.Disposition == "loaded" ? 1 : row.Disposition == "empty-0" ? 2 : 0 }).ToArray() ?? [],
            Roles = content.InitialRoles.Values.Select(role => { PalBattleRole native = NativeBattleRoleAdapter.Write(role);
                native.PanelDeclared = content.PanelDeclaredRoles.Contains(role.RoleId); return native; }).ToArray(),
            Players = content.Players.Select(WritePlayer).ToArray(),
            Growth = content.GrowthByRole.Select(x => new PalBattleGrowth { RoleId = x.Key, LevelUpExperience = x.Value.LevelUpExperience.ToArray(),
                LevelUpMagics = x.Value.LevelUpMagics.Select(row => new PalBattleLearnableMagic { Level = row.Level, MagicId = row.MagicId }).ToArray() }).ToArray(),
            Teams = content.EnemyTeams.Select(x => new PalBattleTeam { TeamId = x.Key, Enemies = x.Value.Select(WriteEnemy).ToArray() }).ToArray(),
            RoleEquipment = content.RoleEquipment.SelectMany(row => row).ToArray(),
            Equipments = content.EquipmentObjects.Select(x => new PalBattleEquipment { ObjectId = x.Key,
                Effects = x.Value.Effects.Select(effect => new PalBattleEquipmentEffect { Attack = effect.Attack, Defense = effect.Defense,
                    Dexterity = effect.Dexterity, BattleSprite = effect.BattleSprite, Statuses = effect.Statuses.ToArray(),
                    MagicPower = effect.MagicPower ?? -1, FleeRate = effect.FleeRate ?? -1, PoisonResistance = effect.PoisonResistance ?? -1 }).ToArray() }).ToArray()
        };
    }

    private static BattleProjectionSourceInfo? ReadProjection(PalBattleContent native, TriggerBattleContent content)
    {
        if (!native.ProjectionPresent)
        {
            if (native.ProjectionFormatVersion != -1 || native.ProjectionKind != "" || native.ProjectionSnapshotKind != ""
                || native.ProjectionReceiptSha256 != "" || native.ProjectionReceiptByteLength != -1
                || native.ProjectionNumericSha256 != "" || native.ProjectionNumericByteLength != -1
                || native.ProjectionInputFiles is not { Length: 0 } || native.ProjectionInputSha256 is not { Length: 0 }
                || native.ProjectionInputByteLengths is not { Length: 0 } || native.ProjectionRoleIds is not { Length: 0 }
                || native.ProjectionTeamIds is not { Length: 0 } || native.ProjectionEnemySlots is not { Length: 0 })
                throw new InvalidDataException("ProjectionPresent is false but projection fields are not all absent defaults");
            return null;
        }
        string[] files = Items(native.ProjectionInputFiles, "ProjectionInputFiles");
        string[] hashes = Items(native.ProjectionInputSha256, "ProjectionInputSha256");
        long[] lengths = native.ProjectionInputByteLengths ?? throw new InvalidDataException("ProjectionInputByteLengths is null");
        if (files.Length != 14 || hashes.Length != 14 || lengths.Length != 14)
            throw new InvalidDataException("Projection.ModernInputs requires 14 aligned rows");
        int[] slots = Copy(native.ProjectionEnemySlots, "ProjectionEnemySlots");
        if (slots.Length % 7 != 0) throw new InvalidDataException("Projection.EnemySlots requires complete seven-word rows");
        var rows = new List<BattleProjectionEnemySlot>();
        for (int i = 0; i < slots.Length; i += 7)
        {
            if (slots[i + 6] is not (0 or 1 or 2)) throw new InvalidDataException("Projection.EnemySlots unknown disposition");
            int? Optional(int value) => value == -1 ? null : value;
            rows.Add(new(slots[i], slots[i + 1], slots[i + 2], slots[i + 6] == 1 ? "loaded" : slots[i + 6] == 2 ? "empty-0" : "skip-FFFF",
                Optional(slots[i + 3]), Optional(slots[i + 4]), Optional(slots[i + 5])));
        }
        var source = new BattleProjectionSourceInfo(native.ProjectionFormatVersion, native.ProjectionKind, native.ProjectionSnapshotKind,
            native.ProjectionReceiptSha256, native.ProjectionReceiptByteLength, native.ProjectionNumericSha256, native.ProjectionNumericByteLength,
            files.Select((file, i) => new BattleProjectionModernInput(file, hashes[i], lengths[i])).ToArray(),
            Copy(native.ProjectionRoleIds, "ProjectionRoleIds"), Copy(native.ProjectionTeamIds, "ProjectionTeamIds"), rows);
        source.ValidateFor(content);
        return source;
    }

    private static T[] Items<T>(T[]? values, string field) where T : class =>
        values is not null && values.All(x => x is not null) ? values : throw new InvalidDataException(field + " contains null");
    private static int[] Copy(int[]? values, string field) => values?.ToArray() ?? throw new InvalidDataException(field + " is null");

    private static IReadOnlyList<IReadOnlyList<int>> ReadGrid(int[]? values)
    {
        var flat = values ?? throw new InvalidDataException("RoleEquipment is null");
        if (flat.Length == 0) return [];
        if (flat.Length != BattleEquipmentState.RoleCount * BattleEquipmentState.SlotCount)
            throw new InvalidDataException($"RoleEquipment requires {BattleEquipmentState.RoleCount * BattleEquipmentState.SlotCount} words");
        return Enumerable.Range(0, BattleEquipmentState.RoleCount)
            .Select(role => Array.AsReadOnly(flat.Skip(role * BattleEquipmentState.SlotCount).Take(BattleEquipmentState.SlotCount).ToArray())).ToList();
    }

    private static IReadOnlyList<BattleEquipmentObject> ReadEquipments(PalBattleEquipment[]? equipments)
    {
        var objects = new List<BattleEquipmentObject>();
        foreach (PalBattleEquipment obj in Items(equipments, "Equipments"))
        {
            PalBattleEquipmentEffect[] rows = Items(obj.Effects, "Equipments.Effects");
            if (rows.Length != BattleEquipmentState.RoleCount)
                throw new InvalidDataException($"Equipments.Effects requires exactly {BattleEquipmentState.RoleCount} rows");
            objects.Add(new(obj.ObjectId, rows.Select(row => new BattleEquipmentEffect(row.Attack, row.Defense,
                row.Dexterity, row.BattleSprite, Copy(row.Statuses, "Equipments.Effects.Statuses"),
                row.MagicPower < 0 ? null : row.MagicPower, row.FleeRate < 0 ? null : row.FleeRate,
                row.PoisonResistance < 0 ? null : row.PoisonResistance)).ToArray()));
        }
        return objects;
    }

    private static ClassicBattlePlayer ReadPlayer(PalBattlePlayer x) => new(x.RoleId, x.Hp, x.MaxHp, x.Attack, x.Defense, x.Dexterity)
    {
        Equipment = Copy(x.Equipment, "Player.Equipment"), Statuses = Copy(x.Statuses, "Player.Statuses"), Poisons = Copy(x.Poisons, "Player.Poisons"),
        DyingScript = x.DyingScript, FriendDeathScript = x.FriendDeathScript,
        NameObjectId = x.HasNameObjectId ? x.NameObjectId : null,
        MagicPower = x.MagicPower, FleeRate = x.FleeRate,
        CoveredByRoleId = x.HasCoveredByRoleId ? x.CoveredByRoleId : null, AttackAll = x.AttackAll
    };

    private static PalBattlePlayer WritePlayer(ClassicBattlePlayer x) => new()
    {
        RoleId = x.RoleId, Hp = x.Hp, MaxHp = x.MaxHp, Attack = x.Attack, Defense = x.Defense, Dexterity = x.Dexterity,
        Equipment = x.Equipment.ToArray(), Statuses = x.Statuses.ToArray(), Poisons = x.Poisons.ToArray(),
        DyingScript = x.DyingScript, FriendDeathScript = x.FriendDeathScript,
        HasNameObjectId = x.NameObjectId.HasValue, NameObjectId = x.NameObjectId ?? 0,
        MagicPower = x.MagicPower, FleeRate = x.FleeRate,
        HasCoveredByRoleId = x.CoveredByRoleId.HasValue, CoveredByRoleId = x.CoveredByRoleId ?? 0, AttackAll = x.AttackAll
    };

    private static ClassicBattleEnemy ReadEnemy(PalBattleEnemy x) => new(x.Id, x.Level, x.Hp, x.Attack, x.Defense, x.Dexterity, x.PhysicalResistance)
    {
        Statuses = Copy(x.Statuses, "Enemy.Statuses"), Poisons = Copy(x.Poisons, "Enemy.Poisons"),
        TurnStartScript = x.TurnStartScript, ReadyScript = x.ReadyScript, BattleEndScript = x.BattleEndScript,
        MagicId = x.MagicId, DualMove = x.DualMove, AttackEquivItem = x.AttackEquivItem, AttackEquivItemRate = x.AttackEquivItemRate,
        Experience = x.Experience, Cash = x.Cash,
        IsEmpty = x.IsEmpty
    };

    private static PalBattleEnemy WriteEnemy(ClassicBattleEnemy x) => new()
    {
        Id = x.Id, Level = x.Level, Hp = x.Hp, Attack = x.Attack, Defense = x.Defense, Dexterity = x.Dexterity, PhysicalResistance = x.PhysicalResistance,
        Statuses = x.Statuses.ToArray(), Poisons = x.Poisons.ToArray(),
        TurnStartScript = x.TurnStartScript, ReadyScript = x.ReadyScript, BattleEndScript = x.BattleEndScript,
        MagicId = x.MagicId, DualMove = x.DualMove, AttackEquivItem = x.AttackEquivItem, AttackEquivItemRate = x.AttackEquivItemRate,
        Experience = x.Experience, Cash = x.Cash,
        IsEmpty = x.IsEmpty
    };
}

/// <summary>One native read: independent numeric data and optional immutable provenance, not authentication.</summary>
public sealed class CheckedNativeBattleContent
{
    public TriggerBattleContent Content { get; }
    public BattleProjectionSourceInfo? ProjectedSource { get; }
    internal CheckedNativeBattleContent(TriggerBattleContent content, BattleProjectionSourceInfo? projectedSource)
    { Content = content; ProjectedSource = projectedSource; }
}

public sealed record BattleProjectionModernInput(string File, string Sha256, long ByteLength);
public sealed record BattleProjectionEnemySlot(int TeamId, int SourceSlot, int ObjectId, string Disposition,
    int? EnemyId = null, int? CompactSlot = null, int? BattleId = null);

/// <summary>Deep-frozen typed transport. Identities describe bytes; callers must verify upstream input.</summary>
public sealed class BattleProjectionSourceInfo
{
    private static readonly string[] InputFiles = ["battle-effect-index.json", "battle-field.json", "enemy-pos.json", "enemy-team.json",
        "enemy.json", "event-object.json", "index.json", "level-up-exp.json", "level-up-magic.json", "magic.json",
        "object.json", "player-roles.json", "scene.json", "store.json"];
    public int FormatVersion { get; }
    public string Kind { get; }
    public string SnapshotKind { get; }
    public string InputTrust => "caller-verified-upstream; declarations-and-byte-identities-not-independent-authentication";
    public string ReceiptSha256 { get; }
    public long ReceiptByteLength { get; }
    public string NumericSha256 { get; }
    public long NumericByteLength { get; }
    public IReadOnlyList<BattleProjectionModernInput> ModernInputs { get; }
    public IReadOnlyList<int> RoleIds { get; }
    public IReadOnlyList<int> TeamIds { get; }
    public IReadOnlyList<BattleProjectionEnemySlot> EnemySlots { get; }

    public BattleProjectionSourceInfo(int formatVersion, string kind, string snapshotKind, string receiptSha256, long receiptByteLength,
        string numericSha256, long numericByteLength, IReadOnlyList<BattleProjectionModernInput> modernInputs,
        IReadOnlyList<int> roleIds, IReadOnlyList<int> teamIds, IReadOnlyList<BattleProjectionEnemySlot> enemySlots)
    {
        FormatVersion = formatVersion; Kind = kind; SnapshotKind = snapshotKind;
        ReceiptSha256 = receiptSha256; ReceiptByteLength = receiptByteLength;
        NumericSha256 = numericSha256; NumericByteLength = numericByteLength;
        if (modernInputs is null || roleIds is null || teamIds is null || enemySlots is null
            || modernInputs.Any(row => row is null) || enemySlots.Any(row => row is null))
            throw Error("collections", "null collection or row");
        ModernInputs = Array.AsReadOnly(modernInputs.Select(row => row with { }).ToArray());
        RoleIds = Array.AsReadOnly(roleIds.ToArray()); TeamIds = Array.AsReadOnly(teamIds.ToArray());
        EnemySlots = Array.AsReadOnly(enemySlots.Select(row => row with { }).ToArray());
        ValidateStructure();
    }

    private static InvalidDataException Error(string field, string detail) => new("Projection." + field + ": " + detail);
    private static void Hash(string? value, string field)
    {
        if (value is not { Length: 64 } || value.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw Error(field, "expected 64 lowercase hexadecimal SHA256 characters");
    }
    private void ValidateStructure()
    {
        if (FormatVersion != 1) throw Error("FormatVersion", "must be 1");
        if (Kind != "m5-base-table-physical-projection") throw Error("Kind", "unsupported kind");
        if (SnapshotKind != "independent-base-table-context-not-natural-save") throw Error("SnapshotKind", "unsupported snapshot");
        Hash(ReceiptSha256, "ReceiptSha256"); Hash(NumericSha256, "NumericSha256");
        if (ReceiptByteLength < 1) throw Error("ReceiptByteLength", "must be a positive Int64");
        if (NumericByteLength is < 1 or > 16 * 1024 * 1024) throw Error("NumericByteLength", "must be 1..16MiB");
        if (!ModernInputs.Select(row => row.File).SequenceEqual(InputFiles, StringComparer.Ordinal))
            throw Error("ModernInputs", "requires the exact 14 files in ordinal order");
        foreach (var row in ModernInputs)
        {
            Hash(row.Sha256, "ModernInputs." + row.File + ".Sha256");
            if (row.ByteLength is < 1 or > 64 * 1024 * 1024) throw Error("ModernInputs." + row.File + ".ByteLength", "must be 1..64MiB");
        }
        if (RoleIds.Count is < 1 or > 3 || RoleIds.Any(id => id is < 0 or > 4) || RoleIds.Distinct().Count() != RoleIds.Count)
            throw Error("RoleIds", "requires 1..3 unique ordered roles in 0..4");
        if (TeamIds.Count == 0 || TeamIds.Any(id => id is < 0 or > 65535) || TeamIds.Distinct().Count() != TeamIds.Count)
            throw Error("TeamIds", "requires unique ordered WORD team IDs");
        if (EnemySlots.Count != (long)TeamIds.Count * 5) throw Error("EnemySlots", "requires exactly five rows per team");
        for (int t = 0; t < TeamIds.Count; t++)
        {
            // #141: a runtime slot is occupied by every non-skip row. battle.c:1617-1717 keeps the
            // zero source row as a real runtime slot (wObjectID 0, no enemy-table identity), while
            // 0xFFFF never reaches i; loaded rows keep battleId = runtime slot + 1 (#210/#140).
            int runtime = 0, loaded = 0;
            for (int slot = 0; slot < 5; slot++)
            {
                var row = EnemySlots[t * 5 + slot];
                if (row.TeamId != TeamIds[t] || row.SourceSlot != slot)
                    throw Error("EnemySlots", "team selection/sourceSlot order or identity mismatch");
                switch (row.Disposition)
                {
                    case "skip-FFFF":
                        if (row.ObjectId != 65535 || row.EnemyId.HasValue || row.CompactSlot.HasValue || row.BattleId.HasValue)
                            throw Error("EnemySlots", "skip-FFFF requires objectId65535 and absent loaded fields");
                        break;
                    case "empty-0":
                        if (row.ObjectId != 0 || row.EnemyId.HasValue || row.CompactSlot.HasValue || row.BattleId.HasValue)
                            throw Error("EnemySlots", "empty-0 requires objectId0 and absent loaded fields");
                        runtime++;
                        break;
                    case "loaded":
                        if (row.ObjectId is < 1 or > 65534 || row.EnemyId is not (>= 0 and <= 65535)
                            || row.CompactSlot != runtime || row.BattleId != runtime + 1)
                            throw Error("EnemySlots", "loaded object/enemy/compactSlot/battleId mismatch");
                        runtime++;
                        loaded++;
                        break;
                    default:
                        throw Error("EnemySlots", "unknown disposition");
                }
            }
            if (loaded is < 1 or > 5) throw Error("EnemySlots", "requires 1..5 loaded slots per team");
        }
    }

    internal void ValidateFor(TriggerBattleContent content)
    {
        if (!RoleIds.SequenceEqual(content.Players.Select(player => player.RoleId))
            || !RoleIds.ToHashSet().SetEquals(content.InitialRoles.Keys) || !RoleIds.ToHashSet().SetEquals(content.GrowthByRole.Keys))
            throw Error("RoleIds", "native players order or roles/growth coverage mismatch");
        if (!TeamIds.SequenceEqual(content.EnemyTeams.Keys)) throw Error("TeamIds", "native team order or identity mismatch");
        for (int t = 0; t < TeamIds.Count; t++)
        {
            // #141: every non-skip row binds one numeric runtime slot in order: an empty-0 row
            // binds the numeric empty slot (IsEmpty, canonical id 0) and a loaded row binds the
            // non-empty enemy's battle id, so the two empty kinds never merge or swap slots.
            var bound = EnemySlots.Skip(t * 5).Take(5).Where(row => row.Disposition != "skip-FFFF").ToArray();
            var enemies = content.EnemyTeams[TeamIds[t]];
            if (bound.Length != enemies.Count)
                throw Error("EnemySlots", "native enemy IDs or slot count mismatch");
            for (int slot = 0; slot < bound.Length; slot++)
            {
                if (bound[slot].Disposition == "empty-0")
                {
                    if (!enemies[slot].IsEmpty || enemies[slot].Id != 0)
                        throw Error("EnemySlots", "empty-0 row must bind the numeric empty runtime slot");
                }
                else if (enemies[slot].IsEmpty || enemies[slot].Id != bound[slot].BattleId)
                    throw Error("EnemySlots", "loaded row must bind the numeric enemy battle id");
            }
            ModernBattleContent.ValidatePhysicalBattle(content, TeamIds[t]);
        }
    }
}
