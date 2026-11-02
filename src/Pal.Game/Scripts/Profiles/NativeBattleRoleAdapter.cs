using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pal.Core;
using Pal.Game.Resources;

namespace Pal.Game.Profiles;

internal static class NativeBattleRoleAdapter
{
    internal static BattleRoleSnapshot Read(PalBattleRole x)
    {
        T Need<T>(T? value, string field) where T : class => value ?? throw new InvalidDataException(field + " is null");
        int[] Ints(int[]? value, string field) => value ?? throw new InvalidDataException(field + " is null");
        BattleExperienceSnapshot Exp(PalBattleExperience value) => new(value.Value, value.Level, value.Count);
        PalBattleHiddenExperience h = Need(x.HiddenExperience, "Role.HiddenExperience");
        var hidden = new Dictionary<string, BattleExperienceSnapshot>(StringComparer.Ordinal)
        {
            ["health"] = Exp(Need(h.Health, "Hidden.Health")),
            ["magic"] = Exp(Need(h.Magic, "Hidden.Magic")),
            ["attack"] = Exp(Need(h.Attack, "Hidden.Attack")),
            ["magicPower"] = Exp(Need(h.MagicPower, "Hidden.MagicPower")),
            ["defense"] = Exp(Need(h.Defense, "Hidden.Defense")),
            ["dexterity"] = Exp(Need(h.Dexterity, "Hidden.Dexterity")),
            ["flee"] = Exp(Need(h.Flee, "Hidden.Flee")),
        };
        return new(x.RoleId, x.Level, x.Hp, x.MaxHp, x.Mp, x.MaxMp,
            Exp(Need(x.PrimaryExperience, "Role.PrimaryExperience")), hidden,
            Array.AsReadOnly(Ints(x.Equipment, "Role.Equipment").ToArray()),
            Array.AsReadOnly(Ints(x.Magics, "Role.Magics").ToArray()),
            Array.AsReadOnly(Ints(x.Statuses, "Role.Statuses").ToArray()), x.CoveredByRoleId);
    }

    internal static PalBattleRole Write(BattleRoleSnapshot x)
    {
        PalBattleExperience Exp(BattleExperienceSnapshot value) => new() { Value = value.Value, Level = value.Level, Count = value.Count };
        return new PalBattleRole{RoleId=x.RoleId,Level=x.Level,Hp=x.Hp,MaxHp=x.MaxHp,Mp=x.Mp,MaxMp=x.MaxMp,PrimaryExperience=Exp(x.PrimaryExperience),HiddenExperience=new(){Health=Exp(x.HiddenExperience["health"]),Magic=Exp(x.HiddenExperience["magic"]),Attack=Exp(x.HiddenExperience["attack"]),MagicPower=Exp(x.HiddenExperience["magicPower"]),Defense=Exp(x.HiddenExperience["defense"]),Dexterity=Exp(x.HiddenExperience["dexterity"]),Flee=Exp(x.HiddenExperience["flee"])},Equipment=x.Equipment.ToArray(),Magics=x.Magics.ToArray(),Statuses=x.Statuses.ToArray(),CoveredByRoleId=x.CoveredByRoleId};
    }
}
