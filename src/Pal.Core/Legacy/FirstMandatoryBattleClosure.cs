using Pal.Core.State;
using Pal.Core.Dialogue;
using Pal.Core.Battle;
using Pal.Core.Events;

namespace Pal.Core.Legacy;

/// <summary>Host-supplied identity of the actually prepared native battle images, verified against
/// the frozen battle-images closure. Values are declarations bound to real resources by the host,
/// never data read from raw archives at runtime.</summary>
public sealed record FirstMandatoryBattleImageClosure(int EnemyId, string EnemyArchiveEntry,
    IReadOnlyList<string> RoleSpriteIds, int BattlefieldId, string BattlefieldArchiveEntry, bool IncludesLayoutAndEffectFrames);

/// <summary>The @09C3 entry opened at the committed search update; the host owns its execution.</summary>
public sealed record FirstMandatoryBattleTriggerRequest(int EntryAddress, int OwnerId);

/// <summary>Session-owned proof that the real resource closure was verified for one exact battle
/// context. Constructed only by <see cref="FirstMandatoryBattleClosure.Verify"/>.</summary>
public sealed class FirstMandatoryBattleClosureReceipt
{
    public string ContextSha256 { get; }
    public int TriggerEntryAddress { get; }
    public IReadOnlyList<int> VerifiedBattleTeamIds { get; }
    /// <summary>Nonzero portrait WORD ids the verified entry requires in both languages.</summary>
    public IReadOnlyList<int> VerifiedPortraitIds { get; }
    /// <summary>The default-language selected-entry requirements the verification walked.</summary>
    public TriggerEntryRequirements VerifiedRequirements { get; }

    internal FirstMandatoryBattleClosureReceipt(string contextSha256, int triggerEntryAddress,
        IEnumerable<int> teamIds, IEnumerable<int> portraitIds, TriggerEntryRequirements requirements)
    {
        ContextSha256 = contextSha256;
        TriggerEntryAddress = triggerEntryAddress;
        VerifiedBattleTeamIds = Array.AsReadOnly(teamIds.Order().ToArray());
        VerifiedPortraitIds = Array.AsReadOnly(portraitIds.Order().ToArray());
        VerifiedRequirements = requirements;
    }
}

/// <summary>Pure capability precheck replacing the #45 contract declarations: verifies the complete
/// real resource closure for the first mandatory battle before the @09C3 entry can open. Loads no
/// files, starts no call, mutates no state and consumes no randomness; every gap rejects here,
/// before any display, object change or battle request.</summary>
public static class FirstMandatoryBattleClosure
{
    /// <summary>Closure statuses must already be rebound from declarations to real resources.</summary>
    public const string ContractOnlyDiagnostic =
        "closure entry '{0}' is still contract-only; bind real modern resources before @09C3 can open.";

    private const int FrozenBattleCommandAddress = 0x09fe;
    private static readonly int[] FrozenBattleCommandOperands = [0x0012, 0xa073, 0];
    private const int FrozenEnemyTeamId = 18;
    private const int FrozenEnemyCount = 2;
    private const int FrozenDyingRoleId = 1;
    private const int FrozenDyingStartAddress = 0xa96e;
    private const int FrozenDyingEndAddress = 0xa987;
    private const int FrozenFriendDeathRoleId = 0;
    private const int FrozenFriendDeathStartAddress = 0xa9b5;
    private const int FrozenFriendDeathEndAddress = 0xa9d1;
    private const int FrozenEquipmentRoleCount = 6;
    private const int FrozenEquipmentSlotCount = 6;
    private const int FrozenDualAttackObjectId = 170;
    private const int FrozenWonStartAddress = 0x09ff;
    private const int FrozenTriggerEndAddress = 0x0a32;
    private const int FrozenDynamicAutoStartAddress = 0x0a36;
    private const int FrozenDynamicAutoEndAddress = 0x0a3b;
    private const int FrozenLostEntryAddress = 0xa073;
    private const int FrozenImageEnemyId = 97;
    private const string FrozenImageEnemyArchiveEntry = "ABC97";
    private static readonly string[] FrozenImageRoleSpriteIds = ["F0", "F1"];
    private const int FrozenImageBattlefieldId = 24;
    private const string FrozenImageBattlefieldArchiveEntry = "FBP24";

    /// <summary>An explicitly supplied cash owner enables cash commands in both language walks.
    /// Only its presence is checked: the wallet is neither executed, retained nor supplied by
    /// the receipt to a later runtime session. Null means no cash capability, not a zero seed.</summary>
    public static FirstMandatoryBattleClosureReceipt Verify(FirstMandatoryBattleProfile profile,
        TriggerEventCollection events, DialogueLanguagePackage chinese, DialogueLanguagePackage english,
        TriggerBattleContent battleContent, FirstMandatoryBattleImageClosure images, CashLiveState? cashLiveState = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(chinese);
        ArgumentNullException.ThrowIfNull(english);
        ArgumentNullException.ThrowIfNull(battleContent);
        ArgumentNullException.ThrowIfNull(images);

        foreach (BattleClosureEntry entry in profile.Closure.Entries)
            if (entry.Status != "prepared")
                throw new InvalidDataException(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    ContractOnlyDiagnostic, entry.Id));

        TriggerEventEntry entry28 = events.Entries.SingleOrDefault(item =>
                item is { OwnerType: "eventObject", OwnerId: 28, Slot: "wTriggerScript", Kind: "trigger" }
                && item.SourceAddress == profile.Identity.TriggerAddress)
            ?? throw new InvalidDataException("real events: owner28 wTriggerScript entry @09C3 is missing.");

        // The frozen battle command identity must match the real event bytes at @09FE, not a
        // rebadged or simplified battle: exact opcode and exact source operands.
        TriggerCommandSource battleSource = Source(events, FrozenBattleCommandAddress);
        if (battleSource.Opcode != 7 || !battleSource.Operands.SequenceEqual(FrozenBattleCommandOperands))
            throw new InvalidDataException(
                $"real events: @{FrozenBattleCommandAddress:X4} is not the frozen startBattle (0012,A073,0000) source.");

        // Every reachable local path, role hook, battle-end script, dynamic verb and the reload
        // boundary are preflighted per language; unknown or raw commands and missing messages,
        // names or portraits reject here, before any side effect.
        TriggerEntryRequirements chineseRequirements = TriggerScriptSession.ValidateEntry(events, chinese, battleContent, entry28.FileId, entry28.Id,
            cashLiveState: cashLiveState);
        TriggerEntryRequirements englishRequirements = TriggerScriptSession.ValidateEntry(events, english, battleContent, entry28.FileId, entry28.Id,
            cashLiveState: cashLiveState);
        if (!chineseRequirements.BattleTeamIds.SequenceEqual(englishRequirements.BattleTeamIds)
            || !chineseRequirements.PortraitIds.SequenceEqual(englishRequirements.PortraitIds))
            throw new InvalidDataException("real closure: the selected entry requires different battles or portraits across languages.");
        if (!chineseRequirements.BattleTeamIds.ToHashSet().SetEquals([FrozenEnemyTeamId]))
            throw new InvalidDataException($"real closure: the entry must select exactly enemy team {FrozenEnemyTeamId}.");

        if (!battleContent.EnemyTeams.TryGetValue(FrozenEnemyTeamId, out IReadOnlyList<ClassicBattleEnemy>? team)
            || team.Count != FrozenEnemyCount)
            throw new InvalidDataException(
                $"real battle-data: team {FrozenEnemyTeamId} must contain exactly {FrozenEnemyCount} enemy slots.");

        ClassicBattlePlayer? dying = battleContent.Players.SingleOrDefault(item => item.RoleId == FrozenDyingRoleId);
        ClassicBattlePlayer? friendDeath = battleContent.Players.SingleOrDefault(item => item.RoleId == FrozenFriendDeathRoleId);
        if (dying is null || dying.DyingScript < FrozenDyingStartAddress || dying.DyingScript > FrozenDyingEndAddress)
            throw new InvalidDataException(
                $"real battle-data: role {FrozenDyingRoleId} dying script is missing or outside @A96E-@A987.");
        if (friendDeath is null || friendDeath.FriendDeathScript < FrozenFriendDeathStartAddress || friendDeath.FriendDeathScript > FrozenFriendDeathEndAddress)
            throw new InvalidDataException(
                $"real battle-data: role {FrozenFriendDeathRoleId} friend-death script is missing or outside @A9B5-@A9D1.");

        if (battleContent.RoleEquipment.Count != FrozenEquipmentRoleCount || !BattleEquipmentState.Initialize(battleContent).Declared)
            throw new InvalidDataException(
                $"real battle-data: the {FrozenEquipmentRoleCount}x{FrozenEquipmentSlotCount} role equipment grid must be declared for the equipment closure.");
        // Formal equipment rules produce the effective panels and battle sprite above; a nonzero
        // grid whose object scripts are undeclared already rejected inside Initialize.
        if (!battleContent.EquipmentObjects.ContainsKey(FrozenDualAttackObjectId))
            throw new InvalidDataException($"real battle-data: dual-attack equipment object {FrozenDualAttackObjectId} is not declared.");

        if (!battleContent.GrowthByRole.Keys.ToHashSet().SetEquals(battleContent.Players.Select(item => item.RoleId)))
            throw new InvalidDataException("real battle-data: growth must cover exactly the declared party roles.");

        // The Won continuation, the dynamic auto range and the lost entry must exist in the real
        // events; the capability walk above validated their reachable semantics.
        _ = Source(events, FrozenWonStartAddress);
        _ = Source(events, FrozenTriggerEndAddress);
        _ = Source(events, FrozenDynamicAutoStartAddress);
        _ = Source(events, FrozenDynamicAutoEndAddress);
        _ = Source(events, FrozenLostEntryAddress);

        if (images.EnemyId != FrozenImageEnemyId || images.EnemyArchiveEntry != FrozenImageEnemyArchiveEntry
            || !images.RoleSpriteIds.SequenceEqual(FrozenImageRoleSpriteIds)
            || images.BattlefieldId != FrozenImageBattlefieldId || images.BattlefieldArchiveEntry != FrozenImageBattlefieldArchiveEntry
            || !images.IncludesLayoutAndEffectFrames)
            throw new InvalidDataException("real battle-images: the prepared image set does not match the frozen enemy97/ABC97/F0/F1/24/FBP24 identity.");

        return new(profile.ContextSha256, entry28.SourceAddress, chineseRequirements.BattleTeamIds, chineseRequirements.PortraitIds, chineseRequirements);
    }

    private static TriggerCommandSource Source(TriggerEventCollection events, int address)
    {
        try { return events.GetSource(address); }
        catch (ArgumentOutOfRangeException exception)
        { throw new InvalidDataException($"real events: no command exists at @{address:X4}.", exception); }
    }
}
