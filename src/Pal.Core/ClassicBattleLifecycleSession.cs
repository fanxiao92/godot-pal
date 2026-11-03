using System.Collections.ObjectModel;

namespace Pal.Core;

/// <summary>PAL_StartBattle's battle lifecycle around the single classic combat owner, including
/// the formal PAL_BattleWon victory settlement (growth, magic learning and recovery).</summary>
public sealed class ClassicBattleLifecycleSession
{
    private const int MaxLevels = 99;
    private static readonly string[] HiddenNames = ["health", "magic", "attack", "magicPower", "defense", "dexterity", "flee"];
    private readonly Dictionary<int, BattleRoleState> roles;
    private readonly IReadOnlyDictionary<int, ClassicBattleGrowthData> growth;
    private readonly IBattleRandom random;
    private readonly ClassicBattleEndScript[] battleEndScripts;
    private int battleEndScriptIndex = -1;
    private ClassicBattleSession? activeCombat;
    private int experienceGranted, cashGranted;
    public ClassicBattleLifecycleView Current { get; private set; }
    public CompletionReceipt? Completion { get; private set; }
    /// <summary>The enemy post-victory script awaiting host execution (battle.c 1334-1337); non-null
    /// only between the growth settlement and the automatic recovery of a Won outcome.</summary>
    public ClassicBattleEndScript? PendingBattleEndScript =>
        battleEndScriptIndex >= 0 && battleEndScriptIndex < battleEndScripts.Length ? battleEndScripts[battleEndScriptIndex] : null;
    /// <summary>The victory settlement facts once growth has settled; null before a Won settlement.</summary>
    public ClassicBattleVictorySettlement? VictorySettlement { get; private set; }

    public ClassicBattleLifecycleSession(BattleRoleSnapshot role, ClassicBattlePlayer player, ClassicBattleEnemy enemy,
        ClassicBattleGrowthData growth, IBattleRandom random, BattleEquipmentState? equipment = null)
        : this([role], [player], [enemy], new Dictionary<int, ClassicBattleGrowthData> { [role.RoleId] = growth }, random, equipment) { }

    public ClassicBattleLifecycleSession(IReadOnlyList<BattleRoleSnapshot> roles, IReadOnlyList<ClassicBattlePlayer> players,
        IReadOnlyList<ClassicBattleEnemy> enemies, IReadOnlyDictionary<int, ClassicBattleGrowthData> growth, IBattleRandom random,
        BattleEquipmentState? equipment = null, bool runEnemyBattleEndScripts = false, bool isBossBattle = false)
    {
        ArgumentNullException.ThrowIfNull(random);
        Validate(roles, players, enemies, growth, equipment);
        // A declared battle-end script is only executable through its capable host (the trigger
        // battle executor that preflighted the closure); direct construction still rejects it.
        battleEndScripts = enemies.Select((enemy, slot) => (enemy, slot)).Where(x => x.enemy.BattleEndScript != 0)
            .Select(x => new ClassicBattleEndScript(x.slot, x.enemy.Id, x.enemy.BattleEndScript)).ToArray();
        if (battleEndScripts.Length > 0 && !runEnemyBattleEndScripts)
            throw new InvalidDataException("Classic combat requires the capable script executor to run a declared enemy battle-end script.");
        var byId = roles.ToDictionary(x => x.RoleId);
        var combat = new ClassicBattleSession(new(players.Select(x => x with { Hp = Math.Max(1, byId[x.RoleId].Hp) }).ToArray(), enemies), random, equipment, isBossBattle);
        this.roles = byId.ToDictionary(x => x.Key, x => new BattleRoleState(x.Value));
        this.growth = growth.ToDictionary(x => x.Key, x => new ClassicBattleGrowthData(
            Array.AsReadOnly(x.Value.LevelUpExperience.ToArray()), Array.AsReadOnly(x.Value.LevelUpMagics.ToArray())));
        this.random = random;
        foreach (var role in this.roles.Values)
        {
            role.Hp = Math.Max(1, role.Hp);
            if (equipment is { Declared: true })
            {
                // PAL_UpdateEquipments re-runs the equip scripts (global.c:1352): the persistent good
                // statuses re-enter the role state by the max rule, so neither a repeated battle nor
                // a fresh session ever stacks an old effect on top of the declared closure.
                IReadOnlyList<int> overlay = equipment.ApplyStatuses(role.RoleId, role.Statuses);
                for (int status = 0; status < role.Statuses.Length; status++) role.Statuses[status] = overlay[status];
            }
            foreach (BattleExperienceState xp in role.HiddenExperience.Values) xp.Count = 0;
        }
        activeCombat = combat;
        Current = View(combat.Current);
    }

    public void SelectAttack(int partyIndex, int enemySlot)
    {
        if (activeCombat is null) throw new InvalidOperationException("This battle lifecycle has finished.");
        activeCombat.SelectAttack(partyIndex, enemySlot);
        Current = View(activeCombat.Current);
    }

    public void SelectAttack(int targetId)
    {
        if (activeCombat is null) throw new InvalidOperationException("This battle lifecycle has finished.");
        activeCombat.SelectAttack(targetId);
        Current = View(activeCombat.Current);
    }

    /// <summary>The whole-party boss flee attempt (fight.c 4119-4173); only a boss battle supports it.</summary>
    public void SelectFlee(int partyIndex)
    {
        if (activeCombat is null) throw new InvalidOperationException("This battle lifecycle has finished.");
        activeCombat.SelectFlee(partyIndex);
        Current = View(activeCombat.Current);
    }

    public bool ResolveNextAction()
    {
        if (activeCombat is null || !activeCombat.ResolveNextAction()) return false;
        SyncPartyFromCombat();
        SettleCombatCompletion();
        return true;
    }

    /// <summary>Resumes the battle after its role hook completed (the script executor acknowledges the
    /// paused battle). Outcome settlement follows the same rules as a regular action resolution.</summary>
    public void AcknowledgeRoleHook()
    {
        if (activeCombat is null) throw new InvalidOperationException("This battle lifecycle has finished.");
        activeCombat.AcknowledgeRoleHook();
        SyncPartyFromCombat();
        SettleCombatCompletion();
    }

    private void SyncPartyFromCombat()
    {
        foreach (var player in activeCombat!.Current.Players)
        {
            var role = roles[player.RoleId];
            role.Hp = player.Hp;
            role.HiddenExperience["attack"].Count = player.Counts.Attack;
            role.HiddenExperience["health"].Count = player.Counts.Health;
            role.HiddenExperience["flee"].Count = player.Counts.Flee;
        }
    }

    private void SettleCombatCompletion()
    {
        ClassicBattleView combat = activeCombat!.Current;
        if (activeCombat.Completion is not null)
        {
            if (combat.Phase == ClassicBattlePhase.Won)
            {
                SettleGrowth(combat);
                // battle.c 1334-1337: every enemy's post-victory script runs between the growth
                // and the automatic recovery; the host executes one declared slot at a time.
                if (battleEndScripts.Length > 0)
                {
                    battleEndScriptIndex = 0;
                    Current = View(combat);
                    return;
                }
                FinishVictory(combat);
                return;
            }
            FinishSettlement(combat);
        }
        else Current = View(combat);
    }

    /// <summary>Host acknowledgement of the running enemy post-victory script: advances to the next
    /// declared slot, or finishes the settlement (recovery, cleanup, completion) after the last one.</summary>
    public void AcknowledgeBattleEndScript()
    {
        if (PendingBattleEndScript is null) throw new InvalidOperationException("No enemy battle-end script is pending.");
        battleEndScriptIndex++;
        if (battleEndScriptIndex >= battleEndScripts.Length)
        {
            battleEndScriptIndex = -1;
            FinishVictory(activeCombat!.Current);
        }
        else Current = View(activeCombat!.Current);
    }

    /// <summary>PAL_BattleWon (battle.c 1003-1330) growth half: grant the KO-accumulated rewards and
    /// grow every surviving member (primary levels, hidden tracks, magic scan). The recovery half
    /// (battle.c 1332-1349) only runs after the enemy post-victory scripts.</summary>
    private void SettleGrowth(ClassicBattleView combat)
    {
        experienceGranted = activeCombat!.ExperienceGained;
        cashGranted = activeCombat.CashGained;
        var growthReports = new List<ClassicBattleRoleGrowth>();
        foreach (var player in combat.Players)
        {
            BattleRoleState role = roles[player.RoleId];
            IReadOnlyList<int> table = growth[player.RoleId].LevelUpExperience;
            int levelBefore = role.Level;
            var learnedMagics = new List<int>();
            if (role.Hp == 0)
            {
                // battle.c 1094: dead members skip every growth step but still report for display.
                growthReports.Add(new(role.RoleId, levelBefore, role.Level, Array.Empty<int>()));
                continue;
            }

            // Primary track: the full battle experience (never a share) crosses level thresholds
            // in a loop; each formal level restores HP/MP to the grown maxima (battle.c 1098-1120).
            if (role.Level > MaxLevels) role.Level = MaxLevels;
            int exp = role.PrimaryExperience.Value + experienceGranted;
            bool leveled = false;
            while (exp >= table[role.Level])
            {
                exp -= table[role.Level];
                if (role.Level < MaxLevels)
                {
                    leveled = true;
                    LevelUp(role);
                    role.Hp = role.MaxHp;
                    role.Mp = role.MaxMp;
                }
            }
            role.PrimaryExperience.Value = exp; // (WORD)dwExp: the remainder is always below the threshold
            if (leveled) role.PrimaryExperience.Level = role.Level;

            // Hidden tracks (battle.c 1240-1262): (exp*count/total)*2 with the reference's integer
            // division order, one 1..2 stat draw per crossed threshold, fixed stat order.
            int total = role.HiddenExperience.Values.Sum(track => track.Count);
            if (total > 0)
            {
                foreach (string name in HiddenNames)
                {
                    BattleExperienceState track = role.HiddenExperience[name];
                    int hidden = (int)((long)experienceGranted * track.Count / total) * 2 + track.Value;
                    if (track.Level > MaxLevels) track.Level = MaxLevels;
                    while (hidden >= table[track.Level])
                    {
                        hidden -= table[track.Level];
                        GrowHiddenStat(role, name, random.NextInclusive(1, 2));
                        if (track.Level < MaxLevels) track.Level++;
                    }
                    track.Value = hidden;
                }
                if (leveled)
                {
                    // battle.c 1319-1323: the grown maxima re-sync the level-up recovery.
                    role.Hp = role.MaxHp;
                    role.Mp = role.MaxMp;
                }
            }

            // Learn every magic of the (possibly new) current level (battle.c 1327-1373 via
            // PAL_AddMagic: first free slot, dedup, full capacity is a silent skip).
            foreach (var row in growth[player.RoleId].LevelUpMagics)
            {
                if (row.MagicId == 0 || row.Level > role.Level || role.Magics.Contains(row.MagicId)) continue;
                int slot = Array.IndexOf(role.Magics, 0);
                if (slot < 0) continue;
                role.Magics[slot] = row.MagicId;
                learnedMagics.Add(row.MagicId);
            }
            growthReports.Add(new(role.RoleId, levelBefore, role.Level, learnedMagics));
        }
        VictorySettlement = new(experienceGranted, cashGranted, growthReports);
    }

    /// <summary>battle.c 1332-1349: every party member, dead included, recovers half the missing
    /// HP/MP after all growth and post-battle scripts; a dead member revives at half its maximum.</summary>
    private void FinishVictory(ClassicBattleView combat)
    {
        foreach (var player in combat.Players)
        {
            BattleRoleState role = roles[player.RoleId];
            role.Hp += (role.MaxHp - role.Hp) / 2;
            role.Mp += (role.MaxMp - role.Mp) / 2;
        }
        FinishSettlement(combat);
    }

    /// <summary>PAL_BattleEnd cleanup (battle.c 1815-1830 through global.c 2297): timed statuses
    /// (at most 999) clear while persistent equipment statuses survive. No inventory, poison or
    /// extra-slot effects exist in this supported baseline. The action owner is released before
    /// publishing a lifecycle (not raw combat) completion.</summary>
    private void FinishSettlement(ClassicBattleView combat)
    {
        foreach (var role in roles.Values)
            for (int i = 0; i < role.Statuses.Length; i++)
                if (role.Statuses[i] <= 999) role.Statuses[i] = 0;
        activeCombat = null;
        Current = View(combat);
        Completion = new(this);
    }

    /// <summary>PAL_PlayerLevelUp (global.c 2347) for one level: the fixed draw order is
    /// maxHP 10+0..7, maxMP 8+0..5, attack/magicPower 4+0..1, defense/dexterity 2+0..1,
    /// flee +2, then every grown word caps at 999. WORD sums wrap before the cap.</summary>
    private void LevelUp(BattleRoleState role)
    {
        role.Level = Math.Min(MaxLevels, role.Level + 1);
        role.MaxHp = StatLimit(WordAdd(role.MaxHp, 10 + random.NextInclusive(0, 7)));
        role.MaxMp = StatLimit(WordAdd(role.MaxMp, 8 + random.NextInclusive(0, 5)));
        role.Attack = StatLimit(WordAdd(role.Attack, 4 + random.NextInclusive(0, 1)));
        role.MagicPower = StatLimit(WordAdd(role.MagicPower, 4 + random.NextInclusive(0, 1)));
        role.Defense = StatLimit(WordAdd(role.Defense, 2 + random.NextInclusive(0, 1)));
        role.Dexterity = StatLimit(WordAdd(role.Dexterity, 2 + random.NextInclusive(0, 1)));
        role.FleeRate = StatLimit(WordAdd(role.FleeRate, 2));
    }

    /// <summary>CHECK_HIDDEN_EXP's stat increment (battle.c 1258): a plain WORD sum with no cap.</summary>
    private static void GrowHiddenStat(BattleRoleState role, string name, int delta) =>
        _ = name switch
        {
            "health" => role.MaxHp = WordAdd(role.MaxHp, delta),
            "magic" => role.MaxMp = WordAdd(role.MaxMp, delta),
            "attack" => role.Attack = WordAdd(role.Attack, delta),
            "magicPower" => role.MagicPower = WordAdd(role.MagicPower, delta),
            "defense" => role.Defense = WordAdd(role.Defense, delta),
            "dexterity" => role.Dexterity = WordAdd(role.Dexterity, delta),
            "flee" => role.FleeRate = WordAdd(role.FleeRate, delta),
            _ => throw new InvalidOperationException($"Unknown hidden experience track '{name}'.")
        };

    private static int WordAdd(int value, int delta) => unchecked((ushort)(value + delta));
    private static int StatLimit(int value) => Math.Min(value, 999);

    /// <summary>Role-hook side effects (script.c 0x001B/0x001C/0x0030) land on the live battle state;
    /// only a running hook's caller context may route them, never a host input.</summary>
    internal void ApplyRoleHookHp(int partyIndex, int delta)
    {
        if (activeCombat is null) throw new InvalidOperationException("This battle lifecycle has finished.");
        activeCombat.HealRoleHookHp(partyIndex, delta);
        SyncPartyFromCombat();
        Current = View(activeCombat.Current);
    }

    internal void ApplyRoleHookMp(int partyIndex, int delta)
    {
        if (activeCombat is null) throw new InvalidOperationException("This battle lifecycle has finished.");
        var player = activeCombat.Current.Players[partyIndex];
        var role = roles[player.RoleId];
        // PAL_IncreaseHPMP only changes alive players (global.c:1272).
        if (role.Hp > 0)
        {
            int mp = unchecked((ushort)(role.Mp + delta));
            if ((short)mp < 0) mp = 0;
            role.Mp = Math.Min(mp, role.MaxMp);
        }
        Current = View(activeCombat.Current);
    }

    internal void ApplyRoleHookStatBoost(int partyIndex, int statField, int percent)
    {
        if (activeCombat is null) throw new InvalidOperationException("This battle lifecycle has finished.");
        activeCombat.BoostRoleHookStat(partyIndex, statField, percent);
        Current = View(activeCombat.Current);
    }

    public bool OwnsCompletion(CompletionReceipt? receipt) => Completion is not null && ReferenceEquals(Completion, receipt);

    private ClassicBattleLifecycleView View(ClassicBattleView combat) => new(activeCombat is not null, new ReadOnlyDictionary<int, BattleRoleSnapshot>(roles.ToDictionary(x => x.Key, x => Snapshot(x.Value))), combat);

    private static BattleRoleSnapshot Snapshot(BattleRoleState role) => new(role.RoleId, role.Level, role.Hp, role.MaxHp, role.Mp, role.MaxMp,
        new(role.PrimaryExperience.Value, role.PrimaryExperience.Level, role.PrimaryExperience.Count),
        new ReadOnlyDictionary<string, BattleExperienceSnapshot>(role.HiddenExperience.ToDictionary(x => x.Key,
            x => new BattleExperienceSnapshot(x.Value.Value, x.Value.Level, x.Value.Count), StringComparer.Ordinal)),
        Array.AsReadOnly(role.Equipment.ToArray()), Array.AsReadOnly(role.Magics.ToArray()), Array.AsReadOnly(role.Statuses.ToArray()), role.CoveredByRoleId,
        role.Attack, role.MagicPower, role.Defense, role.Dexterity, role.FleeRate);

    internal static void Validate(IReadOnlyList<BattleRoleSnapshot> roles, IReadOnlyList<ClassicBattlePlayer> players,
        IReadOnlyList<ClassicBattleEnemy> enemies, IReadOnlyDictionary<int, ClassicBattleGrowthData> growth,
        BattleEquipmentState? equipment = null)
    {
        var content = new TriggerBattleContent(roles, players, growth,
            new Dictionary<int, IReadOnlyList<ClassicBattleEnemy>> { [0] = enemies });
        foreach (var player in content.Players)
            ValidateGrowthDomain(content.InitialRoles[player.RoleId], player, content.GrowthByRole[player.RoleId], equipment);
        ClassicBattleSession.Validate(new(content.Players.Select(x => x with { Hp = Math.Max(1, x.Hp) }).ToArray(), enemies), equipment);
    }

    private static void ValidateGrowthDomain(BattleRoleSnapshot role, ClassicBattlePlayer player, ClassicBattleGrowthData growth,
        BattleEquipmentState? equipment)
    {
        // With a declared closure the worn objects are the formal equipment semantics; without one,
        // zero equipment stays the supported domain. Settled statuses may only carry the closure's
        // persistent good statuses at or below their declared value, so stale stacking is rejected.
        bool equipmentAgrees = equipment is not { Declared: true }
            ? role.Equipment is { Count: 6 } && role.Equipment.All(x => x == 0)
            : role.Equipment is { Count: 6 } && role.Equipment.SequenceEqual(equipment.Equipment(role.RoleId));
        bool statusesAgree = equipment is not { Declared: true }
            ? role.Statuses is { Count: 9 } && role.Statuses.All(x => x == 0)
            : role.Statuses is { Count: 9 } && SettledStatusesAgree(equipment.StatusOverlay(role.RoleId), role.Statuses);
        if (role.RoleId != player.RoleId || role.Hp != player.Hp || role.MaxHp != player.MaxHp
            || role.Hp is < 0 or > 32767 || role.MaxHp is < 1 or > 65535 || role.Hp > role.MaxHp
            || !Word(role.Mp) || !Word(role.MaxMp) || role.Mp > role.MaxMp || role.Level is < 0 or > 99
            || !Word(role.Attack) || !Word(role.MagicPower) || !Word(role.Defense) || !Word(role.Dexterity) || !Word(role.FleeRate)
            || role.CoveredByRoleId is < 0 or > 5 || !equipmentAgrees || !statusesAgree
            || role.Magics is not { Count: 32 } || role.Magics.Any(x => !Word(x)))
            throw new InvalidDataException("Invalid or unsupported baseline role state or mismatched combat player.");
        // The settlement consumes any primary/hidden value against the shared threshold table
        // (battle.c 1098/1252); a zero threshold would loop forever and stays unsupported, as do
        // hidden levels above MAX_LEVELS. Primary uses the role's current level, not the record's.
        if (growth.LevelUpExperience is not { Count: 100 } || growth.LevelUpExperience.Any(x => x is < 1 or > 65535)
            || !ValidExperience(role.PrimaryExperience) || role.HiddenExperience is not { Count: 7 }
            || role.HiddenExperience.Keys.Any(name => !HiddenNames.Contains(name, StringComparer.Ordinal))
            || HiddenNames.Any(name => !role.HiddenExperience.TryGetValue(name, out var xp) || !ValidExperience(xp)
                || xp.Level > 99))
            throw new InvalidDataException("Baseline requires complete valid EXP data and growth tables.");
        if (growth.LevelUpMagics is null || growth.LevelUpMagics.Any(row => row is null || !Word(row.Level) || !Word(row.MagicId)))
            throw new InvalidDataException("Baseline requires valid learnable-magic rows.");
    }

    private static bool Word(int value) => value is >= 0 and <= 65535;
    private static bool ValidExperience(BattleExperienceSnapshot? xp) => xp is not null && Word(xp.Value) && Word(xp.Level) && Word(xp.Count);

    /// <summary>Timed statuses (1..999) and extra temporary effects are not part of this domain; the settled
    /// state may only carry persistent equipment statuses (global.c:2255 good branch, >999 preserved by
    /// the battle-end cleanup of global.c:2304) at or below the declared overlay value.</summary>
    private static bool SettledStatusesAgree(IReadOnlyList<int> overlay, IReadOnlyList<int> statuses)
    {
        for (int status = 0; status < BattleEquipmentState.StatusCount; status++)
        {
            int value = statuses[status];
            if (status < BattleEquipmentState.GoodStatusFirstIndex)
            {
                if (value != 0) return false;
            }
            else if (value != 0 && (value < BattleEquipmentState.PersistentStatusFloor || value > overlay[status]))
                return false;
        }
        return true;
    }

    /// <summary>Only published after this session's real KO, settlement and release of its combat owner.
    /// The granted totals mirror gpGlobals->dwCash/iExpGained at PAL_BattleWon: they are the
    /// KO-accumulated rewards on a Won outcome and zero for every other result.</summary>
    public sealed class CompletionReceipt
    {
        public ClassicBattlePhase Outcome { get; }
        public int ExperienceGranted { get; }
        public int CashGranted { get; }
        internal CompletionReceipt(ClassicBattleLifecycleSession session) =>
            (Outcome, ExperienceGranted, CashGranted) =
            (session.Current.Combat.Phase, session.experienceGranted, session.cashGranted);
    }
}
