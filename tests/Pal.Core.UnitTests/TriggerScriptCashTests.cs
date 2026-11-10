using System.Text.Json.Nodes;
using Pal.Core;
using Xunit;

namespace Pal.Core.Tests;

public class TriggerScriptCashTests
{
    [Fact]
    public void Explicit_shared_cash_commands_preserve_reference_flow_and_preflight_before_mutation()
    {
        // script.c:952-968: semantic amount, not retained source WORDs; DWORD addition.
        var add = Fixture();
        Set(add, 1, 0x001E, """{"op":"changeCash","amount":7,"to":"L0000"}""");
        var owner = new CashLiveState(10);
        var script = new TriggerScriptSession(add.Load(), Language(), cashLiveState: owner);
        script.Start("shared", "npc", 60); // Specified RED: content-only changeCash at @0001.
        Assert.Equal(17u, owner.Cash);
        Assert.True(script.ScriptSuccess);
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
        Assert.Same(owner, script.CashLiveState);
        var firstCall = script.CallId;
        script.Start("shared", "npc", 60);
        Assert.NotEqual(firstCall, script.CallId);
        Assert.Equal(24u, owner.Cash);
        var second = new TriggerScriptSession(add.Load(), Language(), cashLiveState: owner);
        second.Start("shared", "npc", 60);
        Assert.Equal(31u, owner.Cash);
        var isolated = new CashLiveState(0);
        new TriggerScriptSession(add.Load(), Language(), cashLiveState: isolated).Start("shared", "npc", 60);
        Assert.Equal(7u, isolated.Cash);
        Assert.Equal(31u, owner.Cash);

        // Independent reference literals: exact/shortfall, signed WORD endpoints, uint wrap/high cash.
        foreach (var (initial, amount, expected, shortage) in new (uint, int, uint, bool)[]
        {
            (0, -1, 0, true), (5, -5, 0, false), (4, -5, 4, true),
            (32768, -32768, 0, false), (32767, -32768, 32767, true),
            (4294967295, 1, 0, false), (2147483648, -32768, 2147450880, false),
            (0, 32767, 32767, false), (0, 0, 0, false)
        })
        {
            var fixture = Fixture();
            Set(fixture, 1, 0x001E, $$"""{"op":"changeCash","amount":{{amount}},"to":"L0003"}""");
            Set(fixture, 3, 0x0041, """{"op":"markFailed"}""");
            var cash = new CashLiveState(initial);
            var call = new TriggerScriptSession(fixture.Load(), Language(), cashLiveState: cash);
            call.Start("shared", "npc", 60);
            Assert.Equal(expected, cash.Cash);
            Assert.Equal(!shortage, call.ScriptSuccess); // Only the reached markFailed clears it.
        }

        // A shortage to address zero terminates; it does not set g_fScriptSuccess or execute raw @0.
        var zero = Fixture();
        Set(zero, 1, 0x001E, """{"op":"changeCash","amount":-5,"to":"L0000"}""");
        var poor = new CashLiveState(4);
        var shortCall = new TriggerScriptSession(zero.Load(), Language(), cashLiveState: poor);
        shortCall.Start("shared", "npc", 60);
        Assert.Equal(4u, poor.Cash);
        Assert.True(shortCall.ScriptSuccess);
        Assert.Equal(TriggerScriptPhase.Completed, shortCall.Phase);
        Assert.Equal(0, shortCall.CurrentPosition!.SourceAddress);

        foreach (uint initial in new uint[] { 10, 4 })
        {
            var fixture = Fixture();
            Set(fixture, 1, 0x0041, """{"op":"markFailed"}""");
            Set(fixture, 2, 0x001E, """{"op":"changeCash","amount":-5,"to":"L0000"}""");
            Set(fixture, 3, 0x008F, """{"op":"halveCash"}""");
            var cash = new CashLiveState(initial);
            var call = new TriggerScriptSession(fixture.Load(), Language(), cashLiveState: cash);
            call.Start("shared", "npc", 60);
            Assert.Equal(initial == 10 ? 2u : 4u, cash.Cash);
            Assert.False(call.ScriptSuccess); // Neither accepted debit nor shortage resets a previous failure.
        }
        var half = Fixture();
        Set(half, 1, 0x008F, """{"op":"halveCash"}""");
        var odd = new CashLiveState(4294967295);
        new TriggerScriptSession(half.Load(), Language(), cashLiveState: odd).Start("shared", "npc", 60);
        Assert.Equal(2147483647u, odd.Cash);

        // Query and Start have the same explicit capability, no balance-dependent pruning or effects.
        var role = new BattleRoleSnapshot(1, 2, 100, 150, 20, 50, new(5, 2, 17),
            new Dictionary<string, BattleExperienceSnapshot>(), new int[6], new int[32], new int[9], 0);
        var content = new TriggerBattleContent(role, new(1, 100, 150, 40, 20, 100), new([], []),
            new Dictionary<int, ClassicBattleEnemy>());
        var random = new CountingRandom();
        var bound = new TriggerScriptSession(add.Load(), Language(), content, random, cashLiveState: owner);
        Assert.Empty(bound.GetCurrentEntryRequirements("shared", "npc", 60).BattleTeamIds);
        Assert.Empty(TriggerScriptSession.ValidateEntry(add.Load(), Language(), content, "shared", "npc", cashLiveState: owner).BattleTeamIds);
        Assert.Equal(31u, owner.Cash);
        Assert.Equal(Guid.Empty, bound.CallId);
        bound.Start("shared", "npc", 60);
        Assert.Equal(38u, owner.Cash);
        foreach (var fixture in new[] { add, half })
        {
            var legacy = new TriggerScriptSession(fixture.Load(), Language());
            Assert.Null(legacy.CashLiveState);
            Assert.Contains("cash owner", Assert.Throws<InvalidDataException>(() => legacy.GetCurrentEntryRequirements("shared", "npc", 60)).Message);
            Assert.Contains("cash owner", Assert.Throws<InvalidDataException>(() => legacy.Start("shared", "npc", 60)).Message);
            Assert.Contains("cash owner", Assert.Throws<InvalidDataException>(() =>
                TriggerScriptSession.ValidateEntry(fixture.Load(), Language(), content, "shared", "npc")).Message);
            Assert.Equal(Guid.Empty, legacy.CallId);
            Assert.Equal(TriggerScriptPhase.Ready, legacy.Phase);
        }
        foreach (int unsupportedAddress in new[] { 2, 3 })
        {
            var fixture = Fixture();
            Set(fixture, 1, 0x001E, """{"op":"changeCash","amount":7,"to":"L0003"}""");
            Set(fixture, unsupportedAddress, 0x0020, """{"op":"removeItem","itemId":76,"count":1,"shortageTarget":null}""");
            var call = new TriggerScriptSession(fixture.Load(), Language(), content, random, cashLiveState: owner);
            Assert.Contains("removeItem", Assert.Throws<InvalidDataException>(() => call.GetCurrentEntryRequirements("shared", "npc", 60)).Message);
            Assert.Contains("removeItem", Assert.Throws<InvalidDataException>(() => call.Start("shared", "npc", 60)).Message);
            Assert.Contains("removeItem", Assert.Throws<InvalidDataException>(() =>
                TriggerScriptSession.ValidateEntry(fixture.Load(), Language(), content, "shared", "npc", cashLiveState: owner)).Message);
            Assert.Equal(38u, owner.Cash);
            Assert.Equal(Guid.Empty, call.CallId);
            Assert.Equal(TriggerScriptPhase.Ready, call.Phase);
            Assert.Null(call.CurrentPosition);
        }
        Assert.Equal(0, random.Draws);

        // A real display boundary parks the same executor; receipts never reapply preceding cash.
        var displayFixture = Fixture();
        Set(displayFixture, 1, 0x001E, """{"op":"changeCash","amount":7,"to":"L0000"}""");
        Set(displayFixture, 2, 0xFFFF, """{"op":"showDialog","messageId":61,"descriptionLineWord":0}""");
        var displayedCash = new CashLiveState(10);
        var displayed = new TriggerScriptSession(displayFixture.Load(), Language(), cashLiveState: displayedCash);
        displayed.Start("shared", "npc", 60);
        var request = Assert.IsType<TriggerDialogueRequest>(displayed.PendingDisplay);
        Assert.Equal(17u, displayedCash.Cash);
        Assert.False(displayed.AcknowledgeDisplay(request.Id));
        Assert.False(displayed.AcknowledgeDisplay(Guid.NewGuid()));
        Assert.Throws<InvalidOperationException>(() => displayed.Start("shared", "npc", 60));
        Assert.Equal(17u, displayedCash.Cash);
        request.Display.Update(100, false, true);
        request.Display.Update(100, true, false);
        Assert.True(displayed.AcknowledgeDisplay(request.Id));
        Assert.False(displayed.AcknowledgeDisplay(request.Id));
        Assert.Equal(17u, displayedCash.Cash);
        displayed.Start("shared", "npc", 60);
        Assert.False(displayed.AcknowledgeDisplay(request.Id));
        Assert.Equal(24u, displayedCash.Cash);
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
        command["next"] = $"L{address + 1:X4}";
        // Deliberately unrelated: the modern fields, not source operands, drive execution.
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
