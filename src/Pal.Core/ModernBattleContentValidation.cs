namespace Pal.Core;

public static partial class ModernBattleContent
{
    /// <summary>Validates one physical battle without an event host, session, RNG or input mutation.
    /// Full numeric format is checked; only the selected team's capability closure is checked.</summary>
    public static void ValidatePhysicalBattle(TriggerBattleContent content, int enemyTeamId)
    {
        Validate(content);
        string teamPath = Root + $".teams[{enemyTeamId}]";
        if (enemyTeamId is < 0 or > 65535)
            throw Error(teamPath, "expected team ID in 0..65535");
        if (!content.EnemyTeams.TryGetValue(enemyTeamId, out var enemies))
            throw Error(teamPath, "missing selected battle team");
        try { content.Validate(enemyTeamId, content.InitialRoles); }
        catch (InvalidDataException error) { throw new InvalidDataException($"{teamPath}: {error.Message}", error); }

        // The lifecycle/combat gates support these hooks with a capable host, but numeric
        // content alone cannot prove their event closure. Never silently drop their seeds.
        foreach (var (player, index) in content.Players.Select((player, index) => (player, index)))
        {
            if (player.DyingScript != 0)
                throw Error(Root + $".players[{index}].dyingScript", "physical battle requires an event host for this hook");
            if (player.FriendDeathScript != 0)
                throw Error(Root + $".players[{index}].friendDeathScript", "physical battle requires an event host for this hook");
        }
        foreach (var (enemy, slot) in enemies.Select((enemy, slot) => (enemy, slot)))
            if (enemy.BattleEndScript != 0)
                throw Error(teamPath + $".enemies[{slot}].battleEndScript", "physical battle requires an event host for this hook");
    }

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
        if (content.Players.Any(p => p.NameObjectId.HasValue) && content.Players.Any(p => !p.NameObjectId.HasValue))
            throw Error(Root + ".players", "nameObjectId must be declared together for all players");
        var nameSeeds = new Dictionary<int, (int Dying, int FriendDeath)>();
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
            Range(p.MagicPower, pp + ".magicPower"); Range(p.FleeRate, pp + ".fleeRate");
            Words(p.Equipment, pp + ".equipment", 6); Words(p.Statuses, pp + ".statuses", 9); Words(p.Poisons, pp + ".poisons");
            Range(p.DyingScript, pp + ".dyingScript"); Range(p.FriendDeathScript, pp + ".friendDeathScript");
            if (p.NameObjectId is int nameObjectId)
            {
                Range(nameObjectId, pp + ".nameObjectId");
                if (nameSeeds.TryGetValue(nameObjectId, out var seeds))
                {
                    if (p.DyingScript != seeds.Dying)
                        throw Error(pp + ".dyingScript", "players with the same nameObjectId must declare identical dyingScript seeds");
                    if (p.FriendDeathScript != seeds.FriendDeath)
                        throw Error(pp + ".friendDeathScript", "players with the same nameObjectId must declare identical friendDeathScript seeds");
                }
                else nameSeeds.Add(nameObjectId, (p.DyingScript, p.FriendDeathScript));
            }
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
                string ep = path + $".enemies[{slot}]";
                Words(e.Statuses, ep + ".statuses", 9); Words(e.Poisons, ep + ".poisons");
                if (e.IsEmpty)
                {
                    // #140: the zero source row never reads the enemy table; the numeric contract
                    // admits exactly the canonical all-zero spelling of an empty runtime slot.
                    if (e.Id != 0 || e.Level != 0 || e.Hp != 0 || e.Attack != 0 || e.Defense != 0 || e.Dexterity != 0
                        || e.PhysicalResistance != 0 || e.TurnStartScript != 0 || e.ReadyScript != 0 || e.BattleEndScript != 0
                        || e.MagicId != 0 || e.DualMove != 0 || e.AttackEquivItem != 0 || e.AttackEquivItemRate != 0
                        || e.Experience != 0 || e.Cash != 0 || e.Statuses.Any(status => status != 0) || e.Poisons.Any(poison => poison != 0))
                        throw Error(ep + ".isEmpty", "an empty enemy slot requires canonical id 0, hp 0 and all-zero abilities, hooks and rewards");
                    continue;
                }
                Range(e.Id, ep + ".id", 1); Range(e.Level, ep + ".level"); Range(e.Hp, ep + ".hp", 1);
                Range(e.Attack, ep + ".attack", -32768, 32767);
                Range(e.Defense, ep + ".defense", -32768, 32767);
                Range(e.Dexterity, ep + ".dexterity", -32768, 32767);
                Range(e.PhysicalResistance, ep + ".physicalResistance");
                Range(e.TurnStartScript, ep + ".turnStartScript"); Range(e.ReadyScript, ep + ".readyScript");
                Range(e.BattleEndScript, ep + ".battleEndScript"); Range(e.MagicId, ep + ".magicId");
                Range(e.DualMove, ep + ".dualMove"); Range(e.AttackEquivItem, ep + ".attackEquivItem");
                Range(e.AttackEquivItemRate, ep + ".attackEquivItemRate");
                Range(e.Experience, ep + ".experience"); Range(e.Cash, ep + ".cash");
            }
        }
        if (content.RoleEquipment.Count != 0 || content.EquipmentObjects.Count != 0)
            BattleEquipmentState.ValidateEquipmentClosure(content.RoleEquipment,
                content.EquipmentObjects.Values.ToList(), content.InitialRoles.Values);
    }
}
