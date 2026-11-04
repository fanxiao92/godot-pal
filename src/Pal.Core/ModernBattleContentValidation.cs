namespace Pal.Core;

public static partial class ModernBattleContent
{
    /// <summary>Validates numeric format only, not a selected trigger entry's supported capability closure.</summary>
    public static void Validate(TriggerBattleContent content)
    {
        if (content is null) throw Error(Root, "content is null");

        void Range(int n, string p, int min = 0, int max = 65535)
        { if (n < min || n > max) throw Error(p, $"expected integer in {min}..{max}"); }
        void Words(IReadOnlyList<int>? values, string p, int? count = null)
        {
            if (values is null || (count.HasValue && values.Count != count.Value)) throw Error(p, $"expected {(count.HasValue ? count.Value : "an array")} items");
            for (int i = 0; i < values.Count; i++) Range(values[i], p + $"[{i}]");
        }
        void Exp(BattleExperienceSnapshot? x, string p)
        {
            if (x is null) throw Error(p, "experience is null");
            Range(x.Value, p + ".value"); Range(x.Level, p + ".level"); Range(x.Count, p + ".count");
        }
        foreach (var (p, index) in content.Players.Select((x, i) => (x, i)))
        {
            var r = content.InitialRoles[p.RoleId];
            var growth = content.GrowthByRole[p.RoleId];
            string rp = Root + $".roles[{index}]", pp = Root + $".players[{index}]", gp = Root + $".growth[{index}]";
            Range(r.RoleId, rp + ".roleId", 0, 5); Range(r.Level, rp + ".level");
            Range(r.MaxHp, rp + ".maxHp", 1); Range(r.Hp, rp + ".hp", 0, r.MaxHp);
            Range(r.MaxMp, rp + ".maxMp"); Range(r.Mp, rp + ".mp", 0, r.MaxMp);
            Range(r.CoveredByRoleId, rp + ".coveredByRoleId", 0, 5);
            Range(r.Attack, rp + ".attack"); Range(r.MagicPower, rp + ".magicPower"); Range(r.Defense, rp + ".defense");
            Range(r.Dexterity, rp + ".dexterity"); Range(r.FleeRate, rp + ".fleeRate");
            Exp(r.PrimaryExperience, rp + ".primaryExperience");
            string[] names = ["health", "magic", "attack", "magicPower", "defense", "dexterity", "flee"];
            if (r.HiddenExperience is null || !r.HiddenExperience.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(names))
                throw Error(rp + ".hiddenExperience", "expected exactly seven Ordinal experience names");
            foreach (string name in names) Exp(r.HiddenExperience[name], rp + ".hiddenExperience." + name);
            Words(r.Equipment, rp + ".equipment", 6); Words(r.Magics, rp + ".magics", 32); Words(r.Statuses, rp + ".statuses", 9);
            if (r.RoleId != p.RoleId || r.Hp != p.Hp || r.MaxHp != p.MaxHp) throw Error(pp, "role, HP and maxHp mismatch");
            Range(p.Attack, pp + ".attack"); Range(p.Defense, pp + ".defense"); Range(p.Dexterity, pp + ".dexterity");
            Words(p.Equipment, pp + ".equipment", 6); Words(p.Statuses, pp + ".statuses", 9); Words(p.Poisons, pp + ".poisons");
            Range(p.DyingScript, pp + ".dyingScript"); Range(p.FriendDeathScript, pp + ".friendDeathScript");
            if (p.CoveredByRoleId is int cover) Range(cover, pp + ".coveredByRoleId", 0, 5);
            Words(growth.LevelUpExperience, gp + ".levelUpExperience", 100);
            if (growth.LevelUpMagics is null) throw Error(gp + ".levelUpMagics", "array is null");
            for (int i = 0; i < growth.LevelUpMagics.Count; i++)
            {
                var row = growth.LevelUpMagics[i];
                string path = gp + $".levelUpMagics[{i}]";
                if (row is null) throw Error(path, "row is null");
                Range(row.Level, path + ".level"); Range(row.MagicId, path + ".magicId");
            }
        }
        if (content.EnemyTeams.Count == 0) throw Error(Root + ".teams", "expected at least one team");
        foreach (var (id, enemies) in content.EnemyTeams)
        {
            string path = Root + $".teams[{id}]";
            Range(id, path + ".teamId");
            foreach (var (e, slot) in enemies.Select((x, i) => (x, i)))
            {
                Range(e.Id, path + $".enemies[{slot}].id", 1); Range(e.Level, path + $".enemies[{slot}].level"); Range(e.Hp, path + $".enemies[{slot}].hp", 1);
                Range(e.Attack, path + $".enemies[{slot}].attack", -32768, 32767);
                Range(e.Defense, path + $".enemies[{slot}].defense", -32768, 32767);
                Range(e.Dexterity, path + $".enemies[{slot}].dexterity", -32768, 32767);
                Range(e.PhysicalResistance, path + $".enemies[{slot}].physicalResistance");
                Words(e.Statuses, path + $".enemies[{slot}].statuses", 9); Words(e.Poisons, path + $".enemies[{slot}].poisons");
                Range(e.TurnStartScript, path + $".enemies[{slot}].turnStartScript"); Range(e.ReadyScript, path + $".enemies[{slot}].readyScript");
                Range(e.BattleEndScript, path + $".enemies[{slot}].battleEndScript"); Range(e.MagicId, path + $".enemies[{slot}].magicId");
                Range(e.DualMove, path + $".enemies[{slot}].dualMove"); Range(e.AttackEquivItem, path + $".enemies[{slot}].attackEquivItem");
                Range(e.AttackEquivItemRate, path + $".enemies[{slot}].attackEquivItemRate");
                Range(e.Experience, path + $".enemies[{slot}].experience"); Range(e.Cash, path + $".enemies[{slot}].cash");
            }
        }
        if (content.RoleEquipment.Count != 0 || content.EquipmentObjects.Count != 0)
            BattleEquipmentState.ValidateEquipmentClosure(content.RoleEquipment,
                content.EquipmentObjects.Values.ToList(), content.InitialRoles.Values);
    }
}
