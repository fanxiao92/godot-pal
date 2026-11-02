using System.Collections.ObjectModel;

namespace Pal.Core;

/// <summary>PAL_StartBattle's zero-growth baseline around the single classic combat owner.</summary>
public sealed class ClassicBattleLifecycleSession
{
    private static readonly string[] HiddenNames = ["health", "magic", "attack", "magicPower", "defense", "dexterity", "flee"];
    private readonly BattleRoleState role;
    private ClassicBattleSession? activeCombat;
    public ClassicBattleLifecycleView Current { get; private set; }
    public CompletionReceipt? Completion { get; private set; }

    public ClassicBattleLifecycleSession(BattleRoleSnapshot role, ClassicBattlePlayer player, ClassicBattleEnemy enemy,
        ClassicBattleGrowthData growth, IBattleRandom random)
    {
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(enemy);
        ArgumentNullException.ThrowIfNull(growth);
        ArgumentNullException.ThrowIfNull(random);
        ValidateNoGrowth(role, player, growth);
        // Combat validates all remaining capabilities before any lifecycle state is changed or RNG is consumed.
        // HP0 revival belongs to StartBattle, not the combat numeric contract.
        var combat = new ClassicBattleSession(new([player with { Hp = Math.Max(1, role.Hp) }], [enemy]), random);
        this.role = new BattleRoleState(role);
        this.role.Hp = Math.Max(1, role.Hp);
        foreach (BattleExperienceState xp in this.role.HiddenExperience.Values) xp.Count = 0;
        activeCombat = combat;
        Current = View(combat.Current);
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
        role.Hp = combat.PlayerHp;
        role.HiddenExperience["attack"].Count = combat.Counts.Attack;
        role.HiddenExperience["health"].Count = combat.Counts.Health;
        if (activeCombat.Completion is not null)
        {
            // Preflight proved primary/hidden XP remain below their real thresholds and no magic can be added.
            // Actual attack weights are retained even though zero EXP distributes zero to each hidden track.
            if (combat.Phase == ClassicBattlePhase.Won)
            {
                role.Hp += (role.MaxHp - role.Hp) / 2;
                role.Mp += (role.MaxMp - role.Mp) / 2;
            }
            for (int i = 0; i < role.Statuses.Length; i++)
                if (role.Statuses[i] <= 999) role.Statuses[i] = 0;
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

    private ClassicBattleLifecycleView View(ClassicBattleView combat) => new(activeCombat is not null, Snapshot(), combat);

    private BattleRoleSnapshot Snapshot() => new(role.RoleId, role.Level, role.Hp, role.MaxHp, role.Mp, role.MaxMp,
        new(role.PrimaryExperience.Value, role.PrimaryExperience.Level, role.PrimaryExperience.Count),
        new ReadOnlyDictionary<string, BattleExperienceSnapshot>(role.HiddenExperience.ToDictionary(x => x.Key,
            x => new BattleExperienceSnapshot(x.Value.Value, x.Value.Level, x.Value.Count), StringComparer.Ordinal)),
        Array.AsReadOnly(role.Equipment.ToArray()), Array.AsReadOnly(role.Magics.ToArray()), Array.AsReadOnly(role.Statuses.ToArray()), role.CoveredByRoleId);

    private static void ValidateNoGrowth(BattleRoleSnapshot role, ClassicBattlePlayer player, ClassicBattleGrowthData growth)
    {
        if (role.RoleId != player.RoleId || role.Hp != player.Hp || role.MaxHp != player.MaxHp
            || role.Hp is < 0 or > 32767 || role.MaxHp is < 1 or > 65535 || role.Hp > role.MaxHp
            || !Word(role.Mp) || !Word(role.MaxMp) || role.Mp > role.MaxMp || role.Level is < 0 or > 99
            || role.CoveredByRoleId != 0 || role.Equipment is not { Count: 6 } || role.Equipment.Any(x => x != 0)
            || role.Statuses is not { Count: 9 } || role.Statuses.Any(x => x != 0)
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

    /// <summary>Only published after this session's real KO, settlement and release of its combat owner.</summary>
    public sealed class CompletionReceipt
    {
        public ClassicBattlePhase Outcome { get; }
        internal CompletionReceipt(ClassicBattleLifecycleSession session) => Outcome = session.Current.Combat.Phase;
    }
}
