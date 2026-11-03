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
    public static TriggerBattleContent Load(string uri)
    {
        try
        {
            NativeExplorationProfile.ValidateUri(uri);
            if (!ResourceLoader.Exists(uri)) throw new InvalidDataException("native battle content missing; prepare it explicitly");
            var native = ResourceLoader.Load(uri, cacheMode: ResourceLoader.CacheMode.IgnoreDeep) as PalBattleContent
                ?? throw new InvalidDataException("expected PalBattleContent");
            return CreateCore(native, uri);
        }
        catch (InvalidDataException e) { throw new InvalidDataException(uri + ": " + e.Message, e); }
    }

    public static TriggerBattleContent CreateCore(PalBattleContent native, string identity = "native battle content")
    {
        try
        {
            if (native is null || native.FormatVersion != 1) throw new InvalidDataException("FormatVersion must be 1");
            var roles = Items(native.Roles, "Roles").Select(NativeBattleRoleAdapter.Read).ToArray();
            var players = Items(native.Players, "Players").Select(player =>
            {
                if (!player.HasCoveredByRoleId && player.CoveredByRoleId != 0)
                    throw new InvalidDataException("Player.CoveredByRoleId must be 0 when HasCoveredByRoleId is false");
                return ReadPlayer(player);
            }).ToArray();
            var growth = new Dictionary<int, ClassicBattleGrowthData>();
            foreach (var row in Items(native.Growth, "Growth"))
                if (!growth.TryAdd(row.RoleId, new(Copy(row.LevelUpExperience, "Growth.LevelUpExperience"),
                    Items(row.LevelUpMagics, "Growth.LevelUpMagics").Select(x => new BattleLevelUpMagic(x.Level, x.MagicId)).ToArray())))
                    throw new InvalidDataException("duplicate Growth.RoleId");
            var teams = new Dictionary<int, IReadOnlyList<ClassicBattleEnemy>>();
            foreach (var team in Items(native.Teams, "Teams"))
                if (!teams.TryAdd(team.TeamId, Items(team.Enemies, "Team.Enemies").Select(ReadEnemy).ToArray()))
                    throw new InvalidDataException("duplicate TeamId");
            var content = new TriggerBattleContent(roles, players, growth, teams, ReadGrid(native.RoleEquipment), ReadEquipments(native.Equipments),
                Items(native.Roles, "Roles").Where(role => role.PanelDeclared).Select(role => role.RoleId).ToArray());
            ModernBattleContent.Validate(content);
            return content;
        }
        catch (Exception e) when (e is InvalidDataException or ArgumentException or NullReferenceException)
        { throw new InvalidDataException(identity + ": malformed typed native battle content: " + e.Message, e); }
    }

    public static PalBattleContent Write(TriggerBattleContent content)
    {
        ModernBattleContent.Validate(content);
        return new()
        {
            FormatVersion = 1,
            Roles = content.InitialRoles.Values.Select(role => { PalBattleRole native = NativeBattleRoleAdapter.Write(role);
                native.PanelDeclared = content.PanelDeclaredRoles.Contains(role.RoleId); return native; }).ToArray(),
            Players = content.Players.Select(WritePlayer).ToArray(),
            Growth = content.GrowthByRole.Select(x => new PalBattleGrowth { RoleId = x.Key, LevelUpExperience = x.Value.LevelUpExperience.ToArray(),
                LevelUpMagics = x.Value.LevelUpMagics.Select(row => new PalBattleLearnableMagic { Level = row.Level, MagicId = row.MagicId }).ToArray() }).ToArray(),
            Teams = content.EnemyTeams.Select(x => new PalBattleTeam { TeamId = x.Key, Enemies = x.Value.Select(WriteEnemy).ToArray() }).ToArray(),
            RoleEquipment = content.RoleEquipment.SelectMany(row => row).ToArray(),
            Equipments = content.EquipmentObjects.Select(x => new PalBattleEquipment { ObjectId = x.Key,
                Effects = x.Value.Effects.Select(effect => new PalBattleEquipmentEffect { Attack = effect.Attack, Defense = effect.Defense,
                    Dexterity = effect.Dexterity, BattleSprite = effect.BattleSprite, Statuses = effect.Statuses.ToArray() }).ToArray() }).ToArray()
        };
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
                row.Dexterity, row.BattleSprite, Copy(row.Statuses, "Equipments.Effects.Statuses"))).ToArray()));
        }
        return objects;
    }

    private static ClassicBattlePlayer ReadPlayer(PalBattlePlayer x) => new(x.RoleId, x.Hp, x.MaxHp, x.Attack, x.Defense, x.Dexterity)
    {
        Equipment = Copy(x.Equipment, "Player.Equipment"), Statuses = Copy(x.Statuses, "Player.Statuses"), Poisons = Copy(x.Poisons, "Player.Poisons"),
        DyingScript = x.DyingScript, FriendDeathScript = x.FriendDeathScript,
        CoveredByRoleId = x.HasCoveredByRoleId ? x.CoveredByRoleId : null, AttackAll = x.AttackAll
    };

    private static PalBattlePlayer WritePlayer(ClassicBattlePlayer x) => new()
    {
        RoleId = x.RoleId, Hp = x.Hp, MaxHp = x.MaxHp, Attack = x.Attack, Defense = x.Defense, Dexterity = x.Dexterity,
        Equipment = x.Equipment.ToArray(), Statuses = x.Statuses.ToArray(), Poisons = x.Poisons.ToArray(),
        DyingScript = x.DyingScript, FriendDeathScript = x.FriendDeathScript,
        HasCoveredByRoleId = x.CoveredByRoleId.HasValue, CoveredByRoleId = x.CoveredByRoleId ?? 0, AttackAll = x.AttackAll
    };

    private static ClassicBattleEnemy ReadEnemy(PalBattleEnemy x) => new(x.Id, x.Level, x.Hp, x.Attack, x.Defense, x.Dexterity, x.PhysicalResistance)
    {
        Statuses = Copy(x.Statuses, "Enemy.Statuses"), Poisons = Copy(x.Poisons, "Enemy.Poisons"),
        TurnStartScript = x.TurnStartScript, ReadyScript = x.ReadyScript, BattleEndScript = x.BattleEndScript,
        MagicId = x.MagicId, DualMove = x.DualMove, AttackEquivItem = x.AttackEquivItem, AttackEquivItemRate = x.AttackEquivItemRate,
        Experience = x.Experience, Cash = x.Cash
    };

    private static PalBattleEnemy WriteEnemy(ClassicBattleEnemy x) => new()
    {
        Id = x.Id, Level = x.Level, Hp = x.Hp, Attack = x.Attack, Defense = x.Defense, Dexterity = x.Dexterity, PhysicalResistance = x.PhysicalResistance,
        Statuses = x.Statuses.ToArray(), Poisons = x.Poisons.ToArray(),
        TurnStartScript = x.TurnStartScript, ReadyScript = x.ReadyScript, BattleEndScript = x.BattleEndScript,
        MagicId = x.MagicId, DualMove = x.DualMove, AttackEquivItem = x.AttackEquivItem, AttackEquivItemRate = x.AttackEquivItemRate,
        Experience = x.Experience, Cash = x.Cash
    };
}
