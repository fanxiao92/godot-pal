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
            var role = Single(native.Roles, "Roles");
            var player = Single(native.Players, "Players");
            var growth = Single(native.Growth, "Growth");
            if (growth.RoleId != role.RoleId) throw new InvalidDataException("Growth.RoleId must match Roles[0].RoleId");
            // Absent cover has one canonical encoding; present zero still means role0.
            if (!player.HasCoveredByRoleId && player.CoveredByRoleId != 0)
                throw new InvalidDataException("Player.CoveredByRoleId must be 0 when HasCoveredByRoleId is false");
            var teams = new Dictionary<int, ClassicBattleEnemy>();
            foreach (var team in Items(native.Teams, "Teams"))
            {
                var enemy = Single(team.Enemies, "Team.Enemies");
                if (!teams.TryAdd(team.TeamId, ReadEnemy(enemy))) throw new InvalidDataException("duplicate TeamId");
            }
            var content = new TriggerBattleContent(NativeBattleRoleAdapter.Read(role), ReadPlayer(player),
                new(Copy(growth.LevelUpExperience, "Growth.LevelUpExperience"),
                    Items(growth.LevelUpMagics, "Growth.LevelUpMagics").Select(x => new BattleLevelUpMagic(x.Level, x.MagicId)).ToArray()), teams);
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
            Roles = [NativeBattleRoleAdapter.Write(content.InitialRole)],
            Players = [WritePlayer(content.Player)],
            Growth = [new() { RoleId = content.InitialRole.RoleId, LevelUpExperience = content.Growth.LevelUpExperience.ToArray(),
                LevelUpMagics = content.Growth.LevelUpMagics.Select(x => new PalBattleLearnableMagic { Level = x.Level, MagicId = x.MagicId }).ToArray() }],
            Teams = content.Teams.Select(x => new PalBattleTeam { TeamId = x.Key, Enemies = [WriteEnemy(x.Value)] }).ToArray()
        };
    }

    private static T[] Items<T>(T[]? values, string field) where T : class =>
        values is not null && values.All(x => x is not null) ? values : throw new InvalidDataException(field + " contains null");
    private static T Single<T>(T[]? values, string field) where T : class =>
        Items(values, field).Length == 1 ? values![0] : throw new InvalidDataException(field + " requires exactly one item");
    private static int[] Copy(int[]? values, string field) => values?.ToArray() ?? throw new InvalidDataException(field + " is null");

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
