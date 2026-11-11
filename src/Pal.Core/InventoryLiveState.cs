namespace Pal.Core;

/// <summary>An immutable inventory slot value, including the reserved in-use WORD.</summary>
public sealed record InventorySlot(int ItemId, int Amount, int AmountInUse);

/// <summary>Host-owned inventory, explicitly seeded once. Occupied but depleted slots remain
/// occupied until a future explicit menu compaction; consumers own equipment and script flow.</summary>
public sealed class InventoryLiveState
{
    public const int Capacity = 256;
    private readonly InventorySlot[] slots = new InventorySlot[Capacity];
    public IReadOnlyList<InventorySlot> Slots { get; }

    public InventoryLiveState(IReadOnlyList<InventorySlot> initialItems)
    {
        ArgumentNullException.ThrowIfNull(initialItems);
        if (initialItems.Count > Capacity) throw new InvalidDataException("Inventory requires at most 256 occupied slots.");
        var ids = new HashSet<int>();
        for (int i = 0; i < initialItems.Count; i++)
        {
            var item = initialItems[i];
            if (item is null || item.ItemId is < 1 or > 65535 || !ids.Add(item.ItemId)
                || item.Amount is < 0 or > 99 || item.AmountInUse is < 0 or > 65535)
                throw new InvalidDataException("Inventory seed requires unique nonzero WORD item IDs, amounts 0..99 and in-use WORDs.");
            slots[i] = item with { };
        }
        for (int i = initialItems.Count; i < Capacity; i++) slots[i] = new(0, 0, 0);
        Slots = System.Array.AsReadOnly(slots);
    }

    public int GetAmount(int itemId)
    {
        ValidateItem(itemId);
        if (itemId == 0) return 0;
        int index = Find(itemId);
        return index < Capacity && slots[index].ItemId == itemId ? slots[index].Amount : 0;
    }

    /// <summary>Signed WORD add-command operand; zero means add one. Returns the reference
    /// 1/0/negative-shortage result, not a transaction-success boolean.</summary>
    public int AddItem(int itemId, int amount)
    {
        ValidateItem(itemId);
        if (amount is < -32768 or > 32767) throw new ArgumentOutOfRangeException(nameof(amount));
        return Change(itemId, amount == 0 ? 1 : amount);
    }

    /// <summary>Unsigned WORD remove-command operand; zero means remove one. Equipment fallback
    /// and the branch decision are the caller's responsibility.</summary>
    public int RemoveItem(int itemId, int amount)
    {
        ValidateItem(itemId);
        if (amount is < 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(amount));
        return Change(itemId, amount == 0 ? -1 : -amount);
    }

    private int Change(int itemId, int amount)
    {
        if (itemId == 0) return 0;
        int index = Find(itemId);
        if (index == Capacity) return 0;
        InventorySlot slot = slots[index];
        bool found = slot.ItemId == itemId;
        if (amount > 0)
        {
            // The supported signed-WORD add domain cannot overflow the reference USHORT
            // assignment before its 99 cap (at most 99 + 32767).
            slots[index] = slot with { ItemId = itemId, Amount = Math.Min(99, (found ? slot.Amount : 0) + amount) };
            return 1;
        }
        if (!found) return 0;
        int remaining = slot.Amount + amount;
        slots[index] = slot with { Amount = Math.Max(0, remaining) };
        return remaining < 0 ? remaining : 1;
    }

    private int Find(int itemId)
    {
        int index = 0;
        while (index < Capacity && slots[index].ItemId != 0 && slots[index].ItemId != itemId) index++;
        return index;
    }

    private static void ValidateItem(int itemId)
    {
        if (itemId is < 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(itemId));
    }
}
