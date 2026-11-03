namespace Pal.Core;

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

    internal FirstMandatoryBattleClosureReceipt(string contextSha256, int triggerEntryAddress, IEnumerable<int> teamIds)
    {
        ContextSha256 = contextSha256;
        TriggerEntryAddress = triggerEntryAddress;
        VerifiedBattleTeamIds = Array.AsReadOnly(teamIds.Order().ToArray());
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

    public static FirstMandatoryBattleClosureReceipt Verify(FirstMandatoryBattleProfile profile,
        TriggerEventCollection events, DialogueLanguagePackage chinese, DialogueLanguagePackage english,
        TriggerBattleContent battleContent, FirstMandatoryBattleImageClosure images)
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

        BattleCommandIdentity command = Identity<BattleCommandIdentity>(profile, "battle-team-18");
        BattleRoleHooksIdentity hooks = Identity<BattleRoleHooksIdentity>(profile, "role-hooks-random");
        BattleEquipmentLayoutIdentity equipment = Identity<BattleEquipmentLayoutIdentity>(profile, "equipment-six-role");
        BattleEventRangeIdentity triggerRange = Identity<BattleEventRangeIdentity>(profile, "trigger-09c3-0a32");
        BattleDynamicAutoIdentity dynamicAuto = Identity<BattleDynamicAutoIdentity>(profile, "dynamic-auto-0a36-0a3b");
        BattleFailureBoundaryIdentity failure = Identity<BattleFailureBoundaryIdentity>(profile, "failure-reload-todo");
        BattleImageSetIdentity imageIdentity = Identity<BattleImageSetIdentity>(profile, "battle-images");

        TriggerEventEntry entry28 = events.Entries.SingleOrDefault(item =>
                item is { OwnerType: "eventObject", OwnerId: 28, Slot: "wTriggerScript", Kind: "trigger" }
                && item.SourceAddress == profile.Identity.TriggerAddress)
            ?? throw new InvalidDataException("real events: owner28 wTriggerScript entry @09C3 is missing.");

        // The frozen battle command identity must match the real event bytes at @09FE, not a
        // rebadged or simplified battle: exact opcode and exact source operands.
        TriggerCommandSource battleSource = Source(events, command.CommandAddress);
        if (battleSource.Opcode != 7 || !battleSource.Operands.SequenceEqual(command.Operands))
            throw new InvalidDataException(
                $"real events: @{command.CommandAddress:X4} is not the frozen startBattle (0012,A073,0000) source.");

        // Every reachable local path, role hook, battle-end script, dynamic verb and the reload
        // boundary are preflighted per language; unknown or raw commands and missing messages,
        // names or portraits reject here, before any side effect.
        TriggerEntryRequirements chineseRequirements = TriggerScriptSession.ValidateEntry(events, chinese, battleContent, entry28.FileId, entry28.Id);
        TriggerEntryRequirements englishRequirements = TriggerScriptSession.ValidateEntry(events, english, battleContent, entry28.FileId, entry28.Id);
        if (!chineseRequirements.BattleTeamIds.SequenceEqual(englishRequirements.BattleTeamIds)
            || !chineseRequirements.PortraitIds.SequenceEqual(englishRequirements.PortraitIds))
            throw new InvalidDataException("real closure: the selected entry requires different battles or portraits across languages.");
        if (!chineseRequirements.BattleTeamIds.ToHashSet().SetEquals([command.EnemyTeamId]))
            throw new InvalidDataException($"real closure: the entry must select exactly enemy team {command.EnemyTeamId}.");

        if (!battleContent.EnemyTeams.TryGetValue(command.EnemyTeamId, out IReadOnlyList<ClassicBattleEnemy>? team)
            || team.Count != command.EnemyCount)
            throw new InvalidDataException(
                $"real battle-data: team {command.EnemyTeamId} must contain exactly {command.EnemyCount} enemy slots.");

        ClassicBattlePlayer? dying = battleContent.Players.SingleOrDefault(item => item.RoleId == hooks.DyingRoleId);
        ClassicBattlePlayer? friendDeath = battleContent.Players.SingleOrDefault(item => item.RoleId == hooks.FriendDeathRoleId);
        if (dying is null || dying.DyingScript < hooks.DyingStartAddress || dying.DyingScript > hooks.DyingEndAddress)
            throw new InvalidDataException(
                $"real battle-data: role {hooks.DyingRoleId} dying script is missing or outside @A96E-@A987.");
        if (friendDeath is null || friendDeath.FriendDeathScript < hooks.FriendDeathStartAddress || friendDeath.FriendDeathScript > hooks.FriendDeathEndAddress)
            throw new InvalidDataException(
                $"real battle-data: role {hooks.FriendDeathRoleId} friend-death script is missing or outside @A9B5-@A9D1.");

        if (battleContent.RoleEquipment.Count != equipment.RoleCount || !BattleEquipmentState.Initialize(battleContent).Declared)
            throw new InvalidDataException(
                $"real battle-data: the {equipment.RoleCount}x{equipment.SlotCount} role equipment grid must be declared for the equipment closure.");
        // Formal equipment rules produce the effective panels and battle sprite above; a nonzero
        // grid whose object scripts are undeclared already rejected inside Initialize.
        if (!battleContent.EquipmentObjects.ContainsKey(equipment.DualAttackObjectId))
            throw new InvalidDataException($"real battle-data: dual-attack equipment object {equipment.DualAttackObjectId} is not declared.");

        if (!battleContent.GrowthByRole.Keys.ToHashSet().SetEquals(battleContent.Players.Select(item => item.RoleId)))
            throw new InvalidDataException("real battle-data: growth must cover exactly the declared party roles.");

        // The Won continuation, the dynamic auto range and the lost entry must exist in the real
        // events; the capability walk above validated their reachable semantics.
        _ = Source(events, triggerRange.WonStartAddress);
        _ = Source(events, triggerRange.EndAddress);
        _ = Source(events, dynamicAuto.StartAddress);
        _ = Source(events, dynamicAuto.EndAddress);
        _ = Source(events, failure.LostEntryAddress);

        if (images.EnemyId != imageIdentity.EnemyId || images.EnemyArchiveEntry != imageIdentity.EnemyArchiveEntry
            || !images.RoleSpriteIds.SequenceEqual(imageIdentity.RoleSpriteIds)
            || images.BattlefieldId != imageIdentity.BattlefieldId || images.BattlefieldArchiveEntry != imageIdentity.BattlefieldArchiveEntry
            || images.IncludesLayoutAndEffectFrames != imageIdentity.IncludesLayoutAndEffectFrames)
            throw new InvalidDataException("real battle-images: the prepared image set does not match the frozen enemy97/ABC97/F0/F1/24/FBP24 identity.");

        return new(profile.ContextSha256, entry28.SourceAddress, chineseRequirements.BattleTeamIds);
    }

    private static T Identity<T>(FirstMandatoryBattleProfile profile, string id) where T : BattleClosureIdentity =>
        profile.Closure.Entries.Single(item => item.Id == id).Identity as T
            ?? throw new InvalidDataException($"closure entry '{id}' does not carry its frozen {typeof(T).Name}.");

    private static TriggerCommandSource Source(TriggerEventCollection events, int address)
    {
        try { return events.GetSource(address); }
        catch (ArgumentOutOfRangeException exception)
        { throw new InvalidDataException($"real events: no command exists at @{address:X4}.", exception); }
    }
}
