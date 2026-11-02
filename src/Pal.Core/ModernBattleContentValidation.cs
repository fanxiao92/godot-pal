namespace Pal.Core;

public static partial class ModernBattleContent
{
    /// <summary>Validates numeric format only, not a selected trigger entry's supported capability closure.</summary>
    public static void Validate(TriggerBattleContent content)
    {
        if (content is null) throw Error(Root, "content is null");
        var r = content.InitialRole;
        const string rp = Root + ".roles[0]", pp = Root + ".players[0]", gp = Root + ".growth[0]";
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
        Range(r.RoleId, rp + ".roleId", 0, 5); Range(r.Level, rp + ".level");
        Range(r.MaxHp, rp + ".maxHp", 1); Range(r.Hp, rp + ".hp", 0, r.MaxHp);
        Range(r.MaxMp, rp + ".maxMp"); Range(r.Mp, rp + ".mp", 0, r.MaxMp);
        Range(r.CoveredByRoleId, rp + ".coveredByRoleId", 0, 5);
        Exp(r.PrimaryExperience, rp + ".primaryExperience");
        string[] names = ["health", "magic", "attack", "magicPower", "defense", "dexterity", "flee"];
        if (r.HiddenExperience is null || !r.HiddenExperience.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(names))
            throw Error(rp + ".hiddenExperience", "expected exactly seven Ordinal experience names");
        foreach (string name in names) Exp(r.HiddenExperience[name], rp + ".hiddenExperience." + name);
        Words(r.Equipment, rp + ".equipment", 6); Words(r.Magics, rp + ".magics", 32); Words(r.Statuses, rp + ".statuses", 9);
        var p = content.Player;
        if (r.RoleId != p.RoleId || r.Hp != p.Hp || r.MaxHp != p.MaxHp) throw Error(pp, "role, HP and maxHp mismatch");
        Range(p.Attack, pp + ".attack"); Range(p.Defense, pp + ".defense"); Range(p.Dexterity, pp + ".dexterity");
        Words(p.Equipment, pp + ".equipment", 6); Words(p.Statuses, pp + ".statuses", 9); Words(p.Poisons, pp + ".poisons");
        Range(p.DyingScript, pp + ".dyingScript"); Range(p.FriendDeathScript, pp + ".friendDeathScript");
        if (p.CoveredByRoleId is int cover) Range(cover, pp + ".coveredByRoleId", 0, 5);
        Words(content.Growth.LevelUpExperience, gp + ".levelUpExperience", 100);
        if (content.Growth.LevelUpMagics is null) throw Error(gp + ".levelUpMagics", "array is null");
        for (int i = 0; i < content.Growth.LevelUpMagics.Count; i++)
        {
            var row = content.Growth.LevelUpMagics[i];
            string path = gp + $".levelUpMagics[{i}]";
            if (row is null) throw Error(path, "row is null");
            Range(row.Level, path + ".level"); Range(row.MagicId, path + ".magicId");
        }
        if (content.Teams.Count == 0) throw Error(Root + ".teams", "expected at least one team");
        foreach (var (id, e) in content.Teams)
        {
            string path = Root + $".teams[{id}]";
            Range(id, path + ".teamId");
            Range(e.Id, path + ".enemies[0].id", 1); Range(e.Level, path + ".enemies[0].level"); Range(e.Hp, path + ".enemies[0].hp", 1);
            Range(e.Attack, path + ".enemies[0].attack", -32768, 32767);
            Range(e.Defense, path + ".enemies[0].defense", -32768, 32767);
            Range(e.Dexterity, path + ".enemies[0].dexterity", -32768, 32767);
            Range(e.PhysicalResistance, path + ".enemies[0].physicalResistance");
            Words(e.Statuses, path + ".enemies[0].statuses", 9); Words(e.Poisons, path + ".enemies[0].poisons");
            Range(e.TurnStartScript, path + ".enemies[0].turnStartScript"); Range(e.ReadyScript, path + ".enemies[0].readyScript");
            Range(e.BattleEndScript, path + ".enemies[0].battleEndScript"); Range(e.MagicId, path + ".enemies[0].magicId");
            Range(e.DualMove, path + ".enemies[0].dualMove"); Range(e.AttackEquivItem, path + ".enemies[0].attackEquivItem");
            Range(e.AttackEquivItemRate, path + ".enemies[0].attackEquivItemRate");
            Range(e.Experience, path + ".enemies[0].experience"); Range(e.Cash, path + ".enemies[0].cash");
        }
    }
}
