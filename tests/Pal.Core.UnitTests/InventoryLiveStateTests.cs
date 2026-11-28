using Xunit;
using Pal.Core.State;

namespace Pal.Core.Tests;

public sealed class InventoryLiveStateTests
{
    // Literal oracles: global.c PAL_AddItemToInventory/GetItemAmount; script.c 001F/0020.
    [Fact]
    public void Explicit_inventory_preserves_reference_stock_slots_and_shortage_results()
    {
        var seed = new[] { new InventorySlot(7, 3, 2), new InventorySlot(65535, 0, 65535) };
        var inventory = new InventoryLiveState(seed);
        Assert.Equal(3, inventory.GetAmount(7));
        Assert.Equal(1, inventory.AddItem(7, 2));
        Assert.Equal(5, inventory.GetAmount(7)); // Frozen RED: seed-only shell leaves actual3.
        Assert.Equal(new InventorySlot(7, 5, 2), inventory.Slots[0]);
        Assert.Equal(256, inventory.Slots.Count);
        Assert.Equal(new InventorySlot(65535, 0, 65535), inventory.Slots[1]);
        Assert.Equal(new InventorySlot(0, 0, 0), inventory.Slots[2]);
        seed[0] = new(8, 99, 0);
        Assert.Equal(5, inventory.GetAmount(7));
        Assert.Equal(0, inventory.GetAmount(8));
        Assert.Throws<NotSupportedException>(() => ((IList<InventorySlot>)inventory.Slots)[0] = new(8, 99, 0));

        Assert.Equal(1, inventory.AddItem(7, 0));
        Assert.Equal(6, inventory.GetAmount(7));
        Assert.Equal(1, inventory.AddItem(7, 32767));
        Assert.Equal(99, inventory.GetAmount(7));
        Assert.Equal(1, inventory.AddItem(7, 1));
        Assert.Equal(99, inventory.GetAmount(7));
        Assert.Equal(1, inventory.AddItem(9, 32767));
        Assert.Equal(new InventorySlot(9, 99, 0), inventory.Slots[2]);
        Assert.Equal(1, inventory.AddItem(10, 0));
        Assert.Equal(new InventorySlot(10, 1, 0), inventory.Slots[3]);
        Assert.Equal(1, inventory.AddItem(65535, 0));
        Assert.Equal(new InventorySlot(65535, 1, 65535), inventory.Slots[1]);

        Assert.Equal(1, inventory.AddItem(7, -98));
        Assert.Equal(1, inventory.GetAmount(7));
        Assert.Equal(1, inventory.RemoveItem(7, 0));
        Assert.Equal(new InventorySlot(7, 0, 2), inventory.Slots[0]);
        Assert.Equal(-1, inventory.RemoveItem(7, 0)); // occupied zero differs from absent
        Assert.Equal(0, inventory.RemoveItem(600, 5));
        Assert.Equal(0, inventory.AddItem(600, -1));
        Assert.Equal(0, inventory.GetAmount(600));
        Assert.Equal(-2, inventory.RemoveItem(10, 3));
        Assert.Equal(new InventorySlot(10, 0, 0), inventory.Slots[3]);
        Assert.Equal(-32669, inventory.AddItem(9, -32768));
        Assert.Equal(new InventorySlot(9, 0, 0), inventory.Slots[2]);
        Assert.Equal(-65534, inventory.RemoveItem(65535, 65535));
        Assert.Equal(new InventorySlot(65535, 0, 65535), inventory.Slots[1]);
        Assert.Equal(1, inventory.AddItem(7, 3));
        Assert.Equal(1, inventory.RemoveItem(7, 3));
        Assert.Equal(new InventorySlot(7, 0, 2), inventory.Slots[0]);
        Assert.Equal(1, inventory.AddItem(11, 2));
        Assert.Equal(new InventorySlot(11, 2, 0), inventory.Slots[4]); // no implicit compression
        Assert.Equal(2, inventory.GetAmount(11));
        Assert.Equal(0, inventory.GetAmount(0));
        Assert.Equal(0, inventory.AddItem(0, 5));
        Assert.Equal(0, inventory.RemoveItem(0, 0));
        Assert.Equal(new InventorySlot(0, 0, 0), inventory.Slots[5]);

        var empty = new InventoryLiveState([]);
        Assert.Equal(256, empty.Slots.Count);
        Assert.All(empty.Slots, slot => Assert.Equal(new InventorySlot(0, 0, 0), slot));
        Assert.Equal(0, empty.RemoveItem(1, 0));
        Assert.Equal(1, empty.AddItem(1, 0));
        Assert.Equal(1, empty.GetAmount(1));
        Assert.Equal(2, inventory.GetAmount(11)); // different owner, no hidden singleton
        var alias = inventory;
        Assert.Equal(1, alias.AddItem(11, 2));
        Assert.Equal(4, inventory.GetAmount(11)); // subsequent consumer shares the same owner

        var full = new InventoryLiveState(Enumerable.Range(1, 256).Select(id => new InventorySlot(id, 1, 0)).ToArray());
        Assert.Equal(0, full.AddItem(257, 1));
        Assert.Equal(0, full.GetAmount(257));
        Assert.Equal(1, full.AddItem(256, 2));
        Assert.Equal(new InventorySlot(256, 3, 0), full.Slots[255]);
        Assert.Equal(1, full.RemoveItem(1, 1));
        Assert.Equal(new InventorySlot(1, 0, 0), full.Slots[0]);
        Assert.Equal(0, full.AddItem(257, 1)); // depleted occupied slot is not free
        Assert.Equal(1, full.AddItem(1, 2));
        Assert.Equal(new InventorySlot(1, 2, 0), full.Slots[0]);

        // All malformed API inputs reject before mutation; no truncation into WORD/SHORT.
        var before = inventory.Slots.ToArray();
        foreach (int invalid in new[] { -1, 65536, int.MinValue, int.MaxValue })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => inventory.GetAmount(invalid));
            Assert.Throws<ArgumentOutOfRangeException>(() => inventory.AddItem(invalid, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => inventory.RemoveItem(invalid, 1));
        }
        foreach (int invalid in new[] { -32769, 32768, int.MinValue, int.MaxValue })
            Assert.Throws<ArgumentOutOfRangeException>(() => inventory.AddItem(11, invalid));
        foreach (int invalid in new[] { -1, 65536, int.MinValue, int.MaxValue })
            Assert.Throws<ArgumentOutOfRangeException>(() => inventory.RemoveItem(11, invalid));
        Assert.Equal(before, inventory.Slots);
        Assert.Throws<ArgumentNullException>(() => new InventoryLiveState(null!));
        Assert.Throws<InvalidDataException>(() => new InventoryLiveState(Enumerable.Range(1, 257).Select(id => new InventorySlot(id, 1, 0)).ToArray()));
        Assert.Throws<InvalidDataException>(() => new InventoryLiveState([new(7, 3, 0), new(7, 4, 0)]));
        Assert.Throws<InvalidDataException>(() => new InventoryLiveState([null!]));
        foreach (var invalid in new InventorySlot[] { new(0, 0, 0), new(-1, 1, 0), new(65536, 1, 0),
            new(1, -1, 0), new(1, 100, 0), new(1, 1, -1), new(1, 1, 65536) })
            Assert.Throws<InvalidDataException>(() => new InventoryLiveState([invalid]));
        Assert.Equal(before, inventory.Slots);
        Assert.Equal(1, empty.GetAmount(1));
        Assert.Equal(2, full.GetAmount(1));
    }
}
