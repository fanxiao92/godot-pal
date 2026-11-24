namespace Pal.Core;

/// <summary>One immutable POISONSTATUS value: poison object ID and current script entry WORDs.</summary>
public sealed record PoisonSlot(int PoisonId, int ScriptEntry);

/// <summary>Explicit current-party poison columns, seeded once. Owns live slots and the
/// explicit party-change clear boundary, not poison execution or complete setParty behavior.</summary>
public sealed class PartyPoisonState
{
    public const int Capacity = 16; // MAX_POISONS (palcommon.h)
    private readonly Dictionary<int, PoisonSlot[]> slots = new();
    private readonly Dictionary<int, IReadOnlyList<PoisonSlot>> views = new();
    private readonly Dictionary<int, int> levels = new();
    private readonly List<int> partyRoles = new();
    /// <summary>Read-only live membership order; reset never replaces this view.</summary>
    public IReadOnlyList<int> PartyRoleIds { get; }

    public PartyPoisonState(IReadOnlyList<int> partyRoleIds,
        IReadOnlyList<IReadOnlyList<PoisonSlot>> initialPoisons, IReadOnlyDictionary<int, int> poisonLevels)
    {
        ArgumentNullException.ThrowIfNull(partyRoleIds);
        ArgumentNullException.ThrowIfNull(initialPoisons);
        ArgumentNullException.ThrowIfNull(poisonLevels);
        // Exploration capacity, not permission for five combat participants.
        if (partyRoleIds.Count > 5 || initialPoisons.Count != partyRoleIds.Count
            || partyRoleIds.Any(id => id is < 0 or > 5) || partyRoleIds.Distinct().Count() != partyRoleIds.Count)
            throw new InvalidDataException("Poison seed requires distinct current-party RoleId 0..5, at most five roles and one row per role.");
        foreach (var (id, level) in poisonLevels)
        {
            if (id is < 1 or > 65535 || !Word(level))
                throw new InvalidDataException("Poison definitions require nonzero WORD IDs and WORD levels.");
            levels.Add(id, level);
        }
        partyRoles.AddRange(partyRoleIds);
        PartyRoleIds = partyRoles.AsReadOnly();
        for (int i = 0; i < partyRoleIds.Count; i++)
        {
            var row = initialPoisons[i];
            if (row is not { Count: Capacity } || row.Any(slot => slot is null || !Word(slot.PoisonId)
                || !Word(slot.ScriptEntry) || (slot.PoisonId != 0 && !levels.ContainsKey(slot.PoisonId))))
                throw new InvalidDataException("Each poison row requires 16 ID/script WORD pairs and definitions for all nonzero IDs, including stale suffix slots.");
            var copy = row.Select(slot => slot with { }).ToArray();
            slots.Add(partyRoleIds[i], copy);
            views.Add(partyRoleIds[i], Array.AsReadOnly(copy));
        }
    }

    /// <summary>Read-only live column for an explicitly declared current-party role.</summary>
    public IReadOnlyList<PoisonSlot> Slots(int roleId)
    {
        Role(roleId);
        return views.TryGetValue(roleId, out var row) ? row
            : throw new ArgumentException("Role is not in this current party.", nameof(roleId));
    }

    /// <summary>Poison-table clear boundary of script.c 0x0075: clear every old ID/script pair,
    /// including removed members and stale suffixes, then expose the resolved party with zero rows.
    /// Even unchanged membership clears. Does not update world, replay equipment or run scripts;
    /// the caller must coordinate those separately, outside active trigger/display/battle work.</summary>
    public void ResetForPartyChange(IReadOnlyList<int> partyRoleIds)
    {
        ArgumentNullException.ThrowIfNull(partyRoleIds);
        int[] next = partyRoleIds.ToArray(); // Supports passing the live membership view itself.
        if (next.Length > 5) throw new ArgumentException("Current poison party supports at most five roles.", nameof(partyRoleIds));
        foreach (int id in next) Role(id);
        if (next.Distinct().Count() != next.Length)
            throw new ArgumentException("Current poison party requires distinct roles.", nameof(partyRoleIds));

        // Prepare all new rows/views before mutation. Retained views stay live; removed views
        // are cleared below and detached, never silently rebound to a different or rejoining role.
        var nextRows = next.Select(id => slots.TryGetValue(id, out var row) ? row
            : Enumerable.Repeat(new PoisonSlot(0, 0), Capacity).ToArray()).ToArray();
        var nextViews = next.Select((id, i) => views.TryGetValue(id, out var view) ? view
            : Array.AsReadOnly(nextRows[i])).ToArray();
        foreach (var row in slots.Values) Array.Fill(row, new PoisonSlot(0, 0));
        slots.Clear();
        views.Clear();
        for (int i = 0; i < next.Length; i++)
        {
            slots.Add(next[i], nextRows[i]);
            views.Add(next[i], nextViews[i]);
        }
        partyRoles.Clear();
        partyRoles.AddRange(next);
    }

    /// <summary>Wear branch of PAL_RemoveEquipmentEffect (global.c:1413-1455): retain level&lt;99
    /// pairs before the first ID0, in order, then zero the entire tail. Nonparty roles are untouched.</summary>
    public void RemoveEquipmentPoisons(int roleId)
    {
        Role(roleId);
        if (!slots.TryGetValue(roleId, out var row)) return;
        int kept = 0;
        for (int i = 0; i < Capacity; i++)
        {
            PoisonSlot slot = row[i];
            if (slot.PoisonId == 0) break;
            if (levels[slot.PoisonId] < 99) row[kept++] = slot;
        }
        while (kept < Capacity) row[kept++] = new(0, 0);
    }

    private static void Role(int roleId)
    {
        if (roleId is < 0 or > 5) throw new ArgumentOutOfRangeException(nameof(roleId), roleId, "Role ID must be 0..5.");
    }
    private static bool Word(int value) => value is >= 0 and <= 65535;
}
