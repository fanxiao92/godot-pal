using Pal.Core.State;
using Pal.Core.Battle;

namespace Pal.Core.Save;

/// <summary>The prepared-content closure one role-domain restore is validated against
/// (M6-C leaf #225; parent #148). The frozen #221 contract and the #223 codec validate shape
/// domains only — reference closure against the actually rebuildable context happens here, at
/// candidate construction, fail-closed per the matrix's compatibility rule: an id outside the
/// prepared content can never correspond to a real capture and rejects with a field-locatable
/// diagnostic instead of a partially applied candidate. The host derives every set from the
/// prepared content the save's <see cref="PlayerSaveV1.ContentIdentity"/> names.</summary>
public sealed record PlayerSaveRestoreContent(
    IReadOnlyDictionary<int, BattleEquipmentObject> EquipmentObjects,
    IReadOnlySet<int> MagicObjects,
    IReadOnlySet<int> ItemObjects,
    IReadOnlyDictionary<int, int> PoisonLevels);

/// <summary>The role-domain restore candidate: freshly constructed A/B live owners seeded from
/// one decoded v1 snapshot. Roles (statuses cleared), the replayed equipment state, the party's
/// zeroed poison owner, the bag and the wallet are all built through the owners' public
/// construction chains — never reflection, never a mutation of an old session's owners. The
/// world domain (matrix D10–D12) and the candidate commit/switch are later leaves' boundaries:
/// this record is a plan's output, not a live session swap.</summary>
public sealed record PlayerSaveRestoreCandidate(
    RoleLiveState Roles,
    BattleEquipmentState Equipment,
    InventoryLiveState Inventory,
    CashLiveState Cash,
    PartyPoisonState Poisons,
    IReadOnlyList<int> PartyRoleIds);

/// <summary>M6-C leaf #225 (parent #148): the role/party/inventory/economy restore plan.
/// <see cref="Build"/> rebuilds the A/B live owners from one decoded <see cref="PlayerSaveV1"/>
/// following the frozen #221 matrix restore order for the role domain: six role records
/// reseeded with the nine status words cleared (D2/D7; reference PAL_InitGameData
/// global.c:915 <c>memset(rgPlayerStatus)</c>), the saved party reseeding a zero-row poison
/// owner (D5/D8; PAL_LoadGame_Common global.c:630 <c>memset(rgPoisonStatus)</c>), the saved
/// equipment grid driving exactly one <see cref="BattleEquipmentState.Initialize"/> replay of
/// the declared effect rows and persistent good statuses (D3/D9; PAL_UpdateEquipments
/// global.c:1333 — registers start at zero so a repeated restore can never stack), then the
/// occupied inventory prefix (D6) and the wallet (D7). Poison is a clear domain: restore never
/// revives poison, and the poison definitions come from the prepared content, not the file.
/// Fail-closed and never partial: every owner is a local until the complete candidate record
/// is returned, so any rejection — unknown content references included — leaves no half-built
/// state anywhere (#148 Stories 50/54; Decision 19's no-clamping rule holds because the frozen
/// contract already rejected every out-of-domain word before this plan ran). Candidate
/// construction never advances scripts, runs a world tick or draws gameplay randomness
/// (Decision 24); it owns no FileAccess and no file system (Decision 8).</summary>
public static class PlayerSaveRestorePlan
{
    /// <summary>Builds the complete role-domain candidate from one decoded v1 snapshot. Throws
    /// <see cref="InvalidDataException"/> with a <c>$.</c>-prefixed locatable diagnostic when
    /// the snapshot references content the supplied closure does not declare; nothing is
    /// applied anywhere before the full candidate exists.</summary>
    public static PlayerSaveRestoreCandidate Build(PlayerSaveV1 save, PlayerSaveRestoreContent content)
    {
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(content);
        ValidateContentClosures(save, content);

        // Frozen restore order, role domain (docs/milestones/m6/m6c-save-matrix.md).
        var ordered = save.Roles.OrderBy(role => role.RoleId).ToArray();

        // D2 reseed, D7 clear: every permanent word restores verbatim; the nine status words
        // load cleared — persistent equipment statuses return only through the D9 overlay below.
        var roles = new RoleLiveState(ordered.Select(RoleRecord).ToArray());

        // D3 grid + D9 recompute: the saved grid is the PAL_UpdateEquipments input, so the
        // declared effect rows and persistent good statuses install exactly once from a zero
        // register base; an undeclared equipment object rejects here with its own closure
        // diagnostic instead of loading a silent zero effect.
        var equipment = BattleEquipmentState.Initialize(
            ordered.Select(role => role.Equipment).ToArray(), content.EquipmentObjects);

        // D5 party reseed + D8 clear: the saved order reseeds the poison owner with zero
        // columns; the definitions themselves rebuild from the prepared content identity.
        var zeroRow = Enumerable.Repeat(new PoisonSlot(0, 0), PlayerSaveContract.PoisonCapacity).ToArray();
        var poisons = new PartyPoisonState(save.PartyRoleIds,
            save.PartyRoleIds.Select(_ => (IReadOnlyList<PoisonSlot>)zeroRow).ToArray(),
            content.PoisonLevels);

        // D6/D7: the occupied inventory prefix (depleted rows keep their ids — the frozen modern
        // deviation from PAL_CompressInventory) and the wallet DWORD.
        var inventory = new InventoryLiveState(save.Inventory
            .Select(slot => new InventorySlot(slot.ItemId, slot.Amount, slot.AmountInUse)).ToArray());
        var cash = new CashLiveState(save.Cash);

        return new(roles, equipment, inventory, cash, poisons,
            Array.AsReadOnly(save.PartyRoleIds.ToArray()));
    }

    /// <summary>Reference-closure preflight (matrix compatibility rule 2): every nonzero magic
    /// word and every occupied inventory id must exist in the prepared content before any
    /// owner is constructed. Equipment closure is enforced by the Initialize replay itself.</summary>
    private static void ValidateContentClosures(PlayerSaveV1 save, PlayerSaveRestoreContent content)
    {
        foreach (var role in save.Roles)
            foreach (int magic in role.Magics)
                if (magic != 0 && !content.MagicObjects.Contains(magic))
                    throw new InvalidDataException($"$.roles.magics: magic object {magic} is not in the prepared magic closure.");
        foreach (var slot in save.Inventory)
            if (!content.ItemObjects.Contains(slot.ItemId))
                throw new InvalidDataException($"$.inventory.itemId: item object {slot.ItemId} is not in the prepared item closure.");
    }

    /// <summary>One saved role record back into a live-owner snapshot: every permanent word
    /// verbatim, the nine status words cleared, all collections defensively copied.</summary>
    private static BattleRoleSnapshot RoleRecord(PlayerSaveRole role) => new(
        role.RoleId, role.Level, role.Hp, role.MaxHp, role.Mp, role.MaxMp,
        new BattleExperienceSnapshot(role.PrimaryExperience.Value, role.PrimaryExperience.Level, role.PrimaryExperience.Count),
        role.HiddenExperience.ToDictionary(pair => pair.Key,
            pair => new BattleExperienceSnapshot(pair.Value.Value, pair.Value.Level, pair.Value.Count), StringComparer.Ordinal),
        role.Equipment, role.Magics, new int[PlayerSaveContract.RoleStatusCount], role.CoveredByRoleId,
        role.Attack, role.MagicPower, role.Defense, role.Dexterity, role.FleeRate);
}
