using System.Collections.ObjectModel;

namespace Pal.Core;

/// <summary>PAL_StartBattle's zero-growth baseline around the single classic combat owner.</summary>
public sealed class ClassicBattleLifecycleSession
{
    private static readonly string[] HiddenNames = ["health", "magic", "attack", "magicPower", "defense", "dexterity", "flee"];
    private readonly Dictionary<int, BattleRoleState> roles;
    private ClassicBattleSession? activeCombat;
    public ClassicBattleLifecycleView Current { get; private set; }
    public CompletionReceipt? Completion { get; private set; }

    public ClassicBattleLifecycleSession(BattleRoleSnapshot role, ClassicBattlePlayer player, ClassicBattleEnemy enemy,
        ClassicBattleGrowthData growth, IBattleRandom random, BattleEquipmentState? equipment = null)
        : this([role], [player], [enemy], new Dictionary<int, ClassicBattleGrowthData> { [role.RoleId] = growth }, random, equipment) { }

    public ClassicBattleLifecycleSession(IReadOnlyList<BattleRoleSnapshot> roles, IReadOnlyList<ClassicBattlePlayer> players,
        IReadOnlyList<ClassicBattleEnemy> enemies, IReadOnlyDictionary<int, ClassicBattleGrowthData> growth, IBattleRandom random,
        BattleEquipmentState? equipment = null)
    {
        ArgumentNullException.ThrowIfNull(random);
        Validate(roles, players, enemies, growth, equipment);
        var byId = roles.ToDictionary(x => x.RoleId);
        var combat = new ClassicBattleSession(new(players.Select(x => x with { Hp = Math.Max(1, byId[x.RoleId].Hp) }).ToArray(), enemies), random, equipment);
        this.roles = byId.ToDictionary(x => x.Key, x => new BattleRoleState(x.Value));
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

    public bool ResolveNextAction()
    {
        if (activeCombat is null || !activeCombat.ResolveNextAction()) return false;
        ClassicBattleView combat = activeCombat.Current;
        foreach (var player in combat.Players)
        {
            var role = roles[player.RoleId];
            role.Hp = player.Hp;
            role.HiddenExperience["attack"].Count = player.Counts.Attack;
            role.HiddenExperience["health"].Count = player.Counts.Health;
        }
        if (activeCombat.Completion is not null)
        {
            // Preflight proved primary/hidden XP remain below their real thresholds and no magic can be added.
            // Actual attack weights are retained even though zero EXP distributes zero to each hidden track.
            foreach (var role in roles.Values)
            {
                if (combat.Phase == ClassicBattlePhase.Won)
                {
                    role.Hp += (role.MaxHp - role.Hp) / 2;
                    role.Mp += (role.MaxMp - role.Mp) / 2;
                }
                for (int i = 0; i < role.Statuses.Length; i++)
                    if (role.Statuses[i] <= 999) role.Statuses[i] = 0;
            }
            // No inventory, poison or extra-slot effects exist in this supported baseline.
            // Release the action owner before publishing a lifecycle (not raw combat) completion.
            activeCombat = null;
            Current = View(combat);
            Completion = new(this);
        }
        else Current = View(combat);
        return true;
    }

    public bool OwnsCompletion(CompletionReceipt? receipt) => Completion is not null && ReferenceEquals(Completion, receipt);

    private ClassicBattleLifecycleView View(ClassicBattleView combat) => new(activeCombat is not null, new ReadOnlyDictionary<int, BattleRoleSnapshot>(roles.ToDictionary(x => x.Key, x => Snapshot(x.Value))), combat);

    private static BattleRoleSnapshot Snapshot(BattleRoleState role) => new(role.RoleId, role.Level, role.Hp, role.MaxHp, role.Mp, role.MaxMp,
        new(role.PrimaryExperience.Value, role.PrimaryExperience.Level, role.PrimaryExperience.Count),
        new ReadOnlyDictionary<string, BattleExperienceSnapshot>(role.HiddenExperience.ToDictionary(x => x.Key,
            x => new BattleExperienceSnapshot(x.Value.Value, x.Value.Level, x.Value.Count), StringComparer.Ordinal)),
        Array.AsReadOnly(role.Equipment.ToArray()), Array.AsReadOnly(role.Magics.ToArray()), Array.AsReadOnly(role.Statuses.ToArray()), role.CoveredByRoleId);

    internal static void Validate(IReadOnlyList<BattleRoleSnapshot> roles, IReadOnlyList<ClassicBattlePlayer> players,
        IReadOnlyList<ClassicBattleEnemy> enemies, IReadOnlyDictionary<int, ClassicBattleGrowthData> growth,
        BattleEquipmentState? equipment = null)
    {
        var content = new TriggerBattleContent(roles, players, growth,
            new Dictionary<int, IReadOnlyList<ClassicBattleEnemy>> { [0] = enemies });
        foreach (var player in content.Players)
            ValidateNoGrowth(content.InitialRoles[player.RoleId], player, content.GrowthByRole[player.RoleId], equipment);
        ClassicBattleSession.Validate(new(content.Players.Select(x => x with { Hp = Math.Max(1, x.Hp) }).ToArray(), enemies), equipment);
    }

    private static void ValidateNoGrowth(BattleRoleSnapshot role, ClassicBattlePlayer player, ClassicBattleGrowthData growth,
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
            || role.CoveredByRoleId is < 0 or > 5 || !equipmentAgrees || !statusesAgree
            || role.Magics is not { Count: 32 } || role.Magics.Any(x => !Word(x)))
            throw new InvalidDataException("Invalid or unsupported baseline role state or mismatched combat player.");
        // Primary EXP uses the role's current level, not the EXP record's independently stored level.
        if (growth.LevelUpExperience is not { Count: 100 } || growth.LevelUpExperience.Any(x => x is < 1 or > 65535)
            || !ValidExperience(role.PrimaryExperience) || role.PrimaryExperience.Value >= growth.LevelUpExperience[role.Level]
            || role.HiddenExperience is not { Count: 7 }
            || role.HiddenExperience.Keys.Any(name => !HiddenNames.Contains(name, StringComparer.Ordinal))
            || HiddenNames.Any(name => !role.HiddenExperience.TryGetValue(name, out var xp) || !ValidExperience(xp)
                || xp.Level > 99 || xp.Value >= growth.LevelUpExperience[xp.Level]))
            throw new InvalidDataException("Baseline requires complete valid EXP data below every applicable growth threshold.");
        if (growth.LevelUpMagics is null || growth.LevelUpMagics.Any(row => row is null || !Word(row.Level) || !Word(row.MagicId)
            || (row.MagicId != 0 && row.Level <= role.Level && !role.Magics.Contains(row.MagicId) && role.Magics.Contains(0))))
            throw new InvalidDataException("Baseline does not support learning a new magic after battle.");
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

    /// <summary>Only published after this session's real KO, settlement and release of its combat owner.</summary>
    public sealed class CompletionReceipt
    {
        public ClassicBattlePhase Outcome { get; }
        internal CompletionReceipt(ClassicBattleLifecycleSession session) => Outcome = session.Current.Combat.Phase;
    }
}
