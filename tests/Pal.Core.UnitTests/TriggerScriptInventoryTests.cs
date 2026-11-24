using System.Text.Json.Nodes;
using Pal.Core;
using Xunit;

namespace Pal.Core.Tests;

public class TriggerScriptInventoryTests
{
    [Fact]
    public void Explicit_inventory_commands_preserve_reference_stock_flow_and_pure_preflight()
    {
        // script.c:970-975: signed semantic count, ignoring PAL_AddItemToInventory's return.
        var add = Fixture();
        Set(add, 1, 0x001F, """{"op":"addItem","itemId":7,"count":2}""");
        var owner = new InventoryLiveState([new(7, 3, 9)]);
        var script = new TriggerScriptSession(add.Load(), Language(), inventoryLiveState: owner);
        script.Start("shared", "npc", 60); // Specified RED: unsupported addItem at @0001.
        Assert.Equal(5, owner.GetAmount(7));
        Assert.True(script.ScriptSuccess);
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
        Assert.Same(owner, script.InventoryLiveState);
        var firstCall = script.CallId;
        script.Start("shared", "npc", 60);
        Assert.NotEqual(firstCall, script.CallId);
        Assert.Equal(7, owner.GetAmount(7));
        new TriggerScriptSession(add.Load(), Language(), inventoryLiveState: owner).Start("shared", "npc", 60);
        Assert.Equal(9, owner.GetAmount(7));
        Assert.Equal(9, owner.Slots[0].AmountInUse);
        var isolated = new InventoryLiveState([]);
        new TriggerScriptSession(add.Load(), Language(), inventoryLiveState: isolated).Start("shared", "npc", 60);
        Assert.Equal(2, isolated.GetAmount(7));
        Assert.Equal(9, owner.GetAmount(7));

        foreach (var (initial, count, expected) in new (int, int, int)[]
        {
            (3, 0, 4), (3, -2, 1), (3, -5, 0), (3, -32768, 0),
            (3, 32767, 99), (98, 2, 99), (3, -3, 0), (0, -1, 0)
        })
        {
            var fixture = Fixture();
            Set(fixture, 1, 0x001F, $$"""{"op":"addItem","itemId":65535,"count":{{count}}}""");
            var stock = new InventoryLiveState([new(65535, initial, 42)]);
            var call = new TriggerScriptSession(fixture.Load(), Language(), inventoryLiveState: stock);
            call.Start("shared", "npc", 60);
            Assert.Equal(expected, stock.GetAmount(65535));
            Assert.Equal(new InventorySlot(65535, expected, 42), stock.Slots[0]);
            Assert.True(call.ScriptSuccess); // No failure/rollback even for partial shortage.
        }
        foreach (int item in new[] { 0, 7 })
        {
            var fixture = Fixture();
            Set(fixture, 1, 0x001F, $$"""{"op":"addItem","itemId":{{item}},"count":-1}""");
            var stock = new InventoryLiveState([]);
            var call = new TriggerScriptSession(fixture.Load(), Language(), inventoryLiveState: stock);
            call.Start("shared", "npc", 60);
            Assert.True(call.ScriptSuccess);
            Assert.Equal(new InventorySlot(0, 0, 0), stock.Slots[0]);
        }
        var full = new InventoryLiveState(Enumerable.Range(1, 256).Select(id => new InventorySlot(id, 0, 8)).ToArray());
        var fullFixture = Fixture();
        Set(fullFixture, 1, 0x001F, """{"op":"addItem","itemId":65535,"count":1}""");
        var fullCall = new TriggerScriptSession(fullFixture.Load(), Language(), inventoryLiveState: full);
        fullCall.Start("shared", "npc", 60);
        Assert.Equal(0, full.GetAmount(65535));
        Assert.Equal(new InventorySlot(256, 0, 8), full.Slots[255]);
        Assert.True(fullCall.ScriptSuccess);
        Set(fullFixture, 1, 0x001F, """{"op":"addItem","itemId":256,"count":0}""");
        new TriggerScriptSession(fullFixture.Load(), Language(), inventoryLiveState: full).Start("shared", "npc", 60);
        Assert.Equal(new InventorySlot(256, 1, 8), full.Slots[255]);

        // script.c:1859-1868: quantity (not quantity-in-use/equipped count) < signed count.
        foreach (var (stockAmount, count, shortage) in new (int, int, bool)[]
        {
            (3, 2, false), (3, 3, false), (3, 4, true), (0, 0, false),
            (0, -1, false), (0, -32768, false), (99, 32767, true)
        })
        {
            var fixture = Fixture();
            Set(fixture, 1, 0x0058, $$"""{"op":"itemShortageJump","itemId":7,"count":{{count}},"to":"L0003"}""");
            Set(fixture, 3, 0x0041, """{"op":"markFailed"}""");
            var stock = new InventoryLiveState([new(7, stockAmount, 9)]);
            var call = new TriggerScriptSession(fixture.Load(), Language(), inventoryLiveState: stock);
            call.Start("shared", "npc", 60);
            Assert.Equal(!shortage, call.ScriptSuccess); // Only the reached markFailed clears it.
            Assert.Equal(new InventorySlot(7, stockAmount, 9), stock.Slots[0]);
        }
        var zero = Fixture();
        Set(zero, 1, 0x0058, """{"op":"itemShortageJump","itemId":0,"count":1,"to":"L0000"}""");
        var zeroCall = new TriggerScriptSession(zero.Load(), Language(), inventoryLiveState: owner);
        zeroCall.Start("shared", "npc", 60);
        Assert.True(zeroCall.ScriptSuccess);
        Assert.Equal(TriggerScriptPhase.Completed, zeroCall.Phase);
        Assert.Equal(0, zeroCall.CurrentPosition!.SourceAddress); // Archived raw @0 never executes.

        foreach (int count in new[] { -1, 32767 })
        {
            var fixture = Fixture();
            Set(fixture, 1, 0x0041, """{"op":"markFailed"}""");
            Set(fixture, 2, 0x001F, """{"op":"addItem","itemId":7,"count":-5}""");
            Set(fixture, 3, 0x0058, $$"""{"op":"itemShortageJump","itemId":7,"count":{{count}},"to":"L0000"}""");
            var stock = new InventoryLiveState([new(7, 3, 0)]);
            var call = new TriggerScriptSession(fixture.Load(), Language(), inventoryLiveState: stock);
            call.Start("shared", "npc", 60);
            Assert.False(call.ScriptSuccess);
            Assert.Equal(0, stock.GetAmount(7));
        }
        var cross = Fixture();
        cross.Index["files"] = new JsonArray("shared", "targets");
        var last = cross.Commands[4]!.DeepClone();
        cross.Commands.RemoveAt(4);
        cross.Commands[3]!["next"] = "targets#L0004";
        cross.Files["targets"] = new JsonObject { ["formatVersion"] = 1, ["profile"] = "faithful", ["fileId"] = "targets",
            ["entries"] = new JsonArray(), ["commands"] = new JsonArray(last) };
        Set(cross, 1, 0x0058, """{"op":"itemShortageJump","itemId":65535,"count":1,"to":"targets#L0004"}""");
        Set(cross, 2, 0x0041, """{"op":"markFailed"}""");
        var crossCall = new TriggerScriptSession(cross.Load(), Language(), inventoryLiveState: owner);
        crossCall.Start("shared", "npc", 60);
        Assert.True(crossCall.ScriptSuccess);
        Assert.Equal(new TriggerPosition("targets", "L0004", 4), crossCall.CurrentPosition);

        // An equipped 7 does not satisfy the inventory-only shortage query.
        var role = new BattleRoleSnapshot(1, 2, 100, 150, 20, 50, new(5, 2, 17),
            new Dictionary<string, BattleExperienceSnapshot>(), new[] { 7, 0, 0, 0, 0, 0 }, new int[32], new int[9], 0);
        var content = new TriggerBattleContent(role, new(1, 100, 150, 40, 20, 100), new([], []),
            new Dictionary<int, ClassicBattleEnemy>());
        var random = new CountingRandom();
        var noStock = new InventoryLiveState([]);
        var query = Fixture();
        Set(query, 1, 0x0058, """{"op":"itemShortageJump","itemId":7,"count":1,"to":"L0000"}""");
        var shared = TriggerScriptSession.ForSharedRoles(query.Load(), Language(), content, random, new RoleLiveState([role]),
            inventoryLiveState: noStock);
        Assert.Same(noStock, shared.InventoryLiveState);
        Assert.Empty(shared.GetCurrentEntryRequirements("shared", "npc", 60).BattleTeamIds);
        Assert.Empty(TriggerScriptSession.ValidateEntry(query.Load(), Language(), content, "shared", "npc", inventoryLiveState: noStock).BattleTeamIds);
        shared.Start("shared", "npc", 60);
        Assert.Equal(0, shared.CurrentPosition!.SourceAddress);
        Assert.True(shared.ScriptSuccess);
        var bound = new TriggerScriptSession(add.Load(), Language(), content, random, inventoryLiveState: owner);
        Assert.Empty(bound.GetCurrentEntryRequirements("shared", "npc", 60).BattleTeamIds);
        Assert.Empty(TriggerScriptSession.ValidateEntry(add.Load(), Language(), content, "shared", "npc", inventoryLiveState: owner).BattleTeamIds);
        Assert.Equal(9, owner.GetAmount(7));
        Assert.Equal(Guid.Empty, bound.CallId);
        bound.Start("shared", "npc", 60);
        Assert.Equal(11, owner.GetAmount(7));
        foreach (var fixture in new[] { add, query })
        {
            var legacy = new TriggerScriptSession(fixture.Load(), Language());
            Assert.Null(legacy.InventoryLiveState);
            Assert.Contains("inventory owner", Assert.Throws<InvalidDataException>(() => legacy.GetCurrentEntryRequirements("shared", "npc", 60)).Message);
            Assert.Contains("inventory owner", Assert.Throws<InvalidDataException>(() => legacy.Start("shared", "npc", 60)).Message);
            Assert.Contains("inventory owner", Assert.Throws<InvalidDataException>(() =>
                TriggerScriptSession.ValidateEntry(fixture.Load(), Language(), content, "shared", "npc")).Message);
            Assert.Equal(Guid.Empty, legacy.CallId);
            Assert.Equal(TriggerScriptPhase.Ready, legacy.Phase);
        }
        foreach (int unsupportedAddress in new[] { 3, 4 })
        {
            var fixture = Fixture();
            Set(fixture, 1, 0x001F, """{"op":"addItem","itemId":7,"count":2}""");
            Set(fixture, 2, 0x0058, """{"op":"itemShortageJump","itemId":7,"count":-1,"to":"L0004"}""");
            Set(fixture, unsupportedAddress, 0x0020, """{"op":"removeItem","itemId":7,"count":1,"shortageTarget":null}""");
            var call = new TriggerScriptSession(fixture.Load(), Language(), content, random, inventoryLiveState: owner);
            Assert.Contains("removeItem", Assert.Throws<InvalidDataException>(() => call.GetCurrentEntryRequirements("shared", "npc", 60)).Message);
            Assert.Contains("removeItem", Assert.Throws<InvalidDataException>(() => call.Start("shared", "npc", 60)).Message);
            Assert.Contains("removeItem", Assert.Throws<InvalidDataException>(() =>
                TriggerScriptSession.ValidateEntry(fixture.Load(), Language(), content, "shared", "npc", inventoryLiveState: owner)).Message);
            Assert.Equal(11, owner.GetAmount(7));
            Assert.Equal(Guid.Empty, call.CallId);
            Assert.Equal(TriggerScriptPhase.Ready, call.Phase);
            Assert.Null(call.CurrentPosition);
        }
        foreach (string invalid in new[]
        {
            """{"op":"addItem","itemId":7,"count":32768}""",
            """{"op":"itemShortageJump","itemId":7,"count":-32769,"to":"L0000"}""",
            """{"op":"itemShortageJump","itemId":65536,"count":1,"to":"L0000"}""",
            """{"op":"itemShortageJump","itemId":7,"count":1,"to":null}""",
            """{"op":"itemShortageJump","itemId":7,"count":1,"to":"missing#L0000"}"""
        })
        {
            var fixture = Fixture();
            Set(fixture, 1, 0x001F, """{"op":"addItem","itemId":7,"count":2}""");
            Set(fixture, 2, invalid.Contains("addItem", StringComparison.Ordinal) ? 0x001F : 0x0058, invalid);
            Assert.Contains("@0002", Assert.Throws<InvalidDataException>(() => fixture.Load()).Message);
            Assert.Equal(11, owner.GetAmount(7));
        }
        Assert.Equal(0, random.Draws);

        // Display ACKs only resume after the already-committed inventory command; no replay.
        var displayFixture = Fixture();
        Set(displayFixture, 1, 0x001F, """{"op":"addItem","itemId":7,"count":-2}""");
        Set(displayFixture, 2, 0xFFFF, """{"op":"showDialog","messageId":61,"descriptionLineWord":0}""");
        var displayedStock = new InventoryLiveState([new(7, 10, 0)]);
        var displayed = new TriggerScriptSession(displayFixture.Load(), Language(), inventoryLiveState: displayedStock);
        displayed.Start("shared", "npc", 60);
        var request = Assert.IsType<TriggerDialogueRequest>(displayed.PendingDisplay);
        Assert.Equal(8, displayedStock.GetAmount(7));
        Assert.False(displayed.AcknowledgeDisplay(request.Id));
        Assert.False(displayed.AcknowledgeDisplay(Guid.NewGuid()));
        Assert.Throws<InvalidOperationException>(() => displayed.Start("shared", "npc", 60));
        Assert.Equal(8, displayedStock.GetAmount(7));
        request.Display.Update(100, false, true);
        request.Display.Update(100, true, false);
        Assert.True(displayed.AcknowledgeDisplay(request.Id));
        Assert.False(displayed.AcknowledgeDisplay(request.Id));
        Assert.Equal(8, displayedStock.GetAmount(7));
        displayed.Start("shared", "npc", 60);
        Assert.False(displayed.AcknowledgeDisplay(request.Id));
        Assert.Equal(6, displayedStock.GetAmount(7));
    }

    private static EventFixture Fixture()
    {
        var fixture = new EventFixture();
        for (int i = 1; i <= 3; i++) Set(fixture, i, 0, """{"op":"end","mode":"keep"}""");
        return fixture;
    }

    private static void Set(EventFixture fixture, int address, int opcode, string semantic)
    {
        var command = JsonNode.Parse(semantic)!.AsObject();
        command["label"] = $"L{address:X4}";
        command["next"] = address == 4 ? null : $"L{address + 1:X4}";
        // Deliberately unrelated: the semantic modern fields, not archived operands, execute.
        command["source"] = new JsonObject { ["index"] = address, ["opcode"] = opcode, ["operands"] = new JsonArray(123, 456, 789) };
        fixture.Commands[address] = command;
    }

    private static DialogueLanguagePackage Language() => new("en", [new DialogueMessageBlock(61, 61, [DialogueMessagePart.FromText("A")])]);

    private sealed class CountingRandom : IBattleRandom
    {
        public int Draws { get; private set; }
        public int NextInclusive(int from, int to) { Draws++; return from; }
        public float NextInclusive(float from, float to) { Draws++; return from; }
    }
}
