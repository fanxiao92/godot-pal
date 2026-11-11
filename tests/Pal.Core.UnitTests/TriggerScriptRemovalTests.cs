using System.Text.Json.Nodes;
using Xunit;

namespace Pal.Core.Tests;

public sealed class TriggerScriptRemovalTests
{
    [Fact]
    public void Modern_removal_branches_preserve_script_success_and_share_current_stock_gear_roles_and_poisons()
    {
        // script.c:977-1025: stock2 + hand1 satisfies count3; source operands are unrelated.
        var seed = Seed();
        var fixture = Fixture();
        Remove(fixture, 1, 7, 3, "L0006");
        Set(fixture, 2, 0xffff, """{"op":"showDialog","messageId":61,"descriptionLineWord":0}""");
        Battle(fixture, 3);
        Set(fixture, 6, 0x41, """{"op":"markFailed"}""");
        var script = Bind(fixture, seed);
        var oldRole = seed.Roles.Roles[1];
        var oldStock = seed.Stock.Slots[0];
        var oldGear = seed.Gear.Equipment(1);
        Assert.Single(script.GetCurrentEntryRequirements("shared", "npc", 60).BattleTeamIds); // Designated RED.
        Assert.Same(seed.Stock, script.InventoryLiveState);
        Assert.Same(seed.Gear, script.EquipmentState);
        Assert.Same(seed.Poisons, script.PartyPoisons);
        Assert.Same(oldRole, script.BattleRoles![1]);
        Assert.Equal(Guid.Empty, script.CallId);
        Assert.Equal(0, seed.Random.Draws);
        Assert.Equal(2, seed.Stock.GetAmount(7));
        Assert.Equal(20, seed.Gear.AttackBonus(1));
        script.Start("shared", "npc", 60);
        var firstCall = script.CallId;
        Assert.Equal(0, seed.Stock.GetAmount(7));
        Assert.Equal(new InventorySlot(7, 0, 9), seed.Stock.Slots[0]);
        Assert.Equal(new[] { 0, 0, 0, 0, 0, 7 }, seed.Gear.Equipment(1));
        Assert.Equal(new[] { 0, 0, 0, 0, 0, 7 }, seed.Roles.Roles[1].Equipment);
        Assert.Equal(0, seed.Roles.Roles[1].Statuses[8]);
        Assert.Equal(0, seed.Gear.StatusOverlay(1)[8]);
        Assert.Equal(10, seed.Gear.AttackBonus(1));
        Assert.Equal(new PoisonSlot(10, 1001), seed.Poisons.Slots(1)[0]); // Wear survives.
        Assert.Equal(new[] { 0, 0, 0, 7, 0, 7 }, oldGear);
        Assert.Equal(new[] { 0, 0, 0, 7, 0, 7 }, oldRole.Equipment);
        Assert.Equal(new InventorySlot(7, 2, 9), oldStock);
        Assert.True(script.ScriptSuccess);
        var display = Assert.IsType<TriggerDialogueRequest>(script.PendingDisplay);
        Assert.False(script.AcknowledgeDisplay(display.Id));
        Assert.False(script.AcknowledgeDisplay(Guid.NewGuid()));
        Assert.Throws<InvalidOperationException>(() => script.Start("shared", "npc", 60));
        display.Display.Update(100, false, true);
        display.Display.Update(100, true, false);
        Assert.True(script.AcknowledgeDisplay(display.Id));
        Assert.False(script.AcknowledgeDisplay(display.Id));
        var battle = Assert.IsType<TriggerBattleRequest>(script.PendingBattle);
        Assert.Equal(50, battle.Lifecycle.Current.Combat.Players[0].Attack);
        Assert.Equal(0, battle.Lifecycle.Current.Role.Statuses[8]);
        Assert.False(script.AcknowledgeBattle(battle.Id, null));
        Win(battle);
        Assert.False(script.AcknowledgeBattle(Guid.NewGuid(), battle.Lifecycle.Completion));
        Assert.True(script.AcknowledgeBattle(battle.Id, battle.Lifecycle.Completion));
        Assert.False(script.AcknowledgeBattle(battle.Id, battle.Lifecycle.Completion));
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
        Assert.True(script.ScriptSuccess);
        Assert.Equal(40, seed.Roles.Roles[1].Attack);
        Assert.Equal(7, seed.Gear.Equipment(3)[0]);
        Assert.Equal(7, seed.Gear.Equipment(5)[0]); // Not in current party.
        // A separate explicit Start consumes the current wear, including poison/script pair pruning.
        var nextFixture = Fixture();
        Remove(nextFixture, 1, 7, 0, null);
        var next = Bind(nextFixture, seed);
        next.Start("shared", "npc", 60);
        Assert.NotEqual(firstCall, next.CallId);
        Assert.False(next.AcknowledgeDisplay(display.Id));
        Assert.False(next.AcknowledgeBattle(battle.Id, battle.Lifecycle.Completion));
        Assert.Equal(new int[6], seed.Gear.Equipment(1));
        Assert.Equal(new int[6], seed.Roles.Roles[1].Equipment);
        Assert.Equal(0, seed.Gear.AttackBonus(1));
        Assert.Equal(new PoisonSlot(11, 65535), seed.Poisons.Slots(1)[0]);
        Assert.All(seed.Poisons.Slots(1).Skip(1), slot => Assert.Equal(new PoisonSlot(0, 0), slot));
        next.Start("shared", "npc", 60); // A new operation, not an automatically deduplicated request.
        Assert.Equal(new int[6], seed.Gear.Equipment(3));
        Assert.Equal(7, seed.Gear.Equipment(5)[0]);
        Assert.Equal(7, seed.Content.InitialRole.Equipment[3]);
        Assert.Equal(7, seed.Content.RoleEquipment[1][5]);
        var isolated = Seed();
        Assert.Equal(2, isolated.Stock.GetAmount(7));
        Assert.Equal(20, isolated.Gear.AttackBonus(1));

        // Quantity is unsigned WORD; a target is a count gate, not a ScriptSuccess assignment.
        foreach (var (stock, item, count, target, failedBefore, expectedStock, expectedHand, expectedSuccess) in
            new (int, int, int, string?, bool, int, int, bool)[]
            {
                (2, 7, 1, "L0006", false, 1, 7, true),
                (2, 7, 0, "L0006", false, 1, 7, true),
                (2, 7, 6, "L0006", false, 2, 7, false), // Five total, jump to markFailed.
                (2, 7, 6, null, false, 0, 0, true), // Exhaust five, missing sixth is not refunded.
                (2, 7, 65535, null, false, 0, 0, true),
                (2, 65535, 65535, null, false, 2, 7, true), // Absent WORD ID, not SHORT -1.
                (2, 0, 0, "L0006", false, 2, 7, false),
                (2, 7, 1, null, true, 1, 7, false),
                (2, 7, 6, "L0007", true, 2, 7, false), // Shortage cannot restore a false flag.
                (0, 7, 1, "L0006", false, 0, 0, true), // Equipment satisfies CountItem.
            })
        {
            var data = Seed(stock);
            var flow = Fixture();
            if (failedBefore) Set(flow, 1, 0x41, """{"op":"markFailed"}""");
            else Set(flow, 1, 0x78, """{"op":"noop"}""");
            Remove(flow, 2, item, count, target);
            Set(flow, 6, 0x41, """{"op":"markFailed"}""");
            var call = Bind(flow, data);
            call.GetCurrentEntryRequirements("shared", "npc", 60);
            Assert.Equal(stock, data.Stock.GetAmount(7));
            Assert.Equal(0, data.Random.Draws);
            call.Start("shared", "npc", 60);
            Assert.Equal(expectedStock, data.Stock.GetAmount(7));
            Assert.Equal(expectedHand, data.Gear.Equipment(1)[3]);
            Assert.Equal(expectedSuccess, call.ScriptSuccess);
            Assert.Equal(TriggerScriptPhase.Completed, call.Phase);
        }
        // No-target literal item0 matches empty gear slots; reaching empty hand clears actual dualAttack.
        var zero = Seed();
        zero = zero with { Roles = new RoleLiveState(zero.Roles.Roles.Values.Select(role => role.RoleId == 3
            ? role with { Statuses = [0, 0, 0, 0, 0, 0, 0, 0, 3] } : role).ToArray()) };
        var zeros = Fixture();
        Remove(zeros, 1, 0, 8, null); // Four empties role1, then role3 slots1..4 includes hand3.
        Bind(zeros, zero).Start("shared", "npc", 60);
        Assert.Equal(0, zero.Roles.Roles[3].Statuses[8]);
        Assert.Equal(7, zero.Gear.Equipment(1)[3]);
        Assert.Equal(2, zero.Stock.GetAmount(7));
        Assert.Equal(new PoisonSlot(10, 1001), zero.Poisons.Slots(1)[0]);

        // 0058 remains signed inventory-only even when 0020 sees equipped copies.
        var querySeed = Seed(0);
        var query = Fixture();
        Set(query, 1, 0x58, """{"op":"itemShortageJump","itemId":7,"count":1,"to":"L0006"}""");
        Remove(query, 2, 7, 1, null);
        Set(query, 6, 0x41, """{"op":"markFailed"}""");
        var queried = Bind(query, querySeed);
        queried.Start("shared", "npc", 60);
        Assert.False(queried.ScriptSuccess);
        Assert.Equal(7, querySeed.Gear.Equipment(1)[3]);
        Set(query, 1, 0x58, """{"op":"itemShortageJump","itemId":7,"count":-1,"to":"L0006"}""");
        var signed = Bind(query, querySeed);
        signed.Start("shared", "npc", 60);
        Assert.True(signed.ScriptSuccess);
        Assert.Equal(0, querySeed.Gear.Equipment(1)[3]);

        // Cross-file semantic shortage target, not the retained [123,456,789] words.
        var cross = Fixture();
        Remove(cross, 1, 7, 65535, "targets#L0008");
        Set(cross, 2, 0x41, """{"op":"markFailed"}""");
        cross.Index["files"] = new JsonArray("shared", "targets");
        var last = cross.Commands[8]!.DeepClone();
        cross.Commands.RemoveAt(8);
        cross.Commands[7]!["next"] = "targets#L0008";
        cross.Files["targets"] = new JsonObject { ["formatVersion"] = 1, ["profile"] = "faithful", ["fileId"] = "targets",
            ["entries"] = new JsonArray(), ["commands"] = new JsonArray(last) };
        var crossSeed = Seed();
        var crossCall = Bind(cross, crossSeed);
        crossCall.Start("shared", "npc", 60);
        Assert.True(crossCall.ScriptSuccess);
        Assert.Equal(new TriggerPosition("targets", "L0008", 8), crossCall.CurrentPosition);
        Assert.Equal(2, crossSeed.Stock.GetAmount(7));
        foreach (string invalid in new[]
        {
            """{"op":"removeItem","itemId":7,"count":-1,"shortageTarget":null}""",
            """{"op":"removeItem","itemId":7,"count":65536,"shortageTarget":null}""",
            """{"op":"removeItem","itemId":65536,"count":1,"shortageTarget":null}""",
            """{"op":"removeItem","itemId":7,"count":1,"shortageTarget":"L0000"}""",
            """{"op":"removeItem","itemId":7,"count":1,"shortageTarget":"missing#L0006"}""",
            """{"op":"removeItem","itemId":7,"count":1}"""
        })
        {
            var bad = Fixture();
            Set(bad, 2, 0x20, invalid);
            Assert.Contains("@0002", Assert.Throws<InvalidDataException>(() => bad.Load()).Message);
        }

        // Missing owners are rejected even for stock-only success or a guaranteed shortage.
        var removal = Fixture();
        Remove(removal, 1, 7, 1, null);
        var legacySeed = Seed();
        foreach (var call in new[]
        {
            new TriggerScriptSession(removal.Load(), Language()),
            new TriggerScriptSession(removal.Load(), Language(), inventoryLiveState: legacySeed.Stock),
            TriggerScriptSession.ForSharedRoles(removal.Load(), Language(), legacySeed.Content, legacySeed.Random, legacySeed.Roles,
                equipmentState: legacySeed.Gear, partyPoisons: legacySeed.Poisons),
            TriggerScriptSession.ForSharedRoles(removal.Load(), Language(), legacySeed.Content, legacySeed.Random, legacySeed.Roles,
                inventoryLiveState: legacySeed.Stock, partyPoisons: legacySeed.Poisons),
            TriggerScriptSession.ForSharedRoles(removal.Load(), Language(), legacySeed.Content, legacySeed.Random, legacySeed.Roles,
                inventoryLiveState: legacySeed.Stock, equipmentState: legacySeed.Gear)
        }) Reject(call, legacySeed, "removeItem");
        Assert.Contains("removeItem", Assert.Throws<InvalidDataException>(() => TriggerScriptSession.ValidateEntry(
            removal.Load(), Language(), legacySeed.Content, "shared", "npc", inventoryLiveState: legacySeed.Stock)).Message);

        // Both removal arms must be supported before the earlier add can commit.
        foreach (int badAddress in new[] { 3, 6 })
        {
            var branchSeed = Seed();
            var branch = Fixture();
            Set(branch, 1, 0x1f, """{"op":"addItem","itemId":7,"count":1}""");
            Remove(branch, 2, 7, 0, "L0006");
            Set(branch, badAddress, 0x4, """{"op":"call","to":"L0008","eventObjectId":60}""");
            Reject(Bind(branch, branchSeed), branchSeed, "call");
        }
        // Non-selected remove commands do not demand any new capability.
        var unselected = Fixture();
        Remove(unselected, 6, 7, 1, null);
        new TriggerScriptSession(unselected.Load(), Language()).Start("shared", "npc", 60);

        // A bound world must agree with the fixed poison party. Poison member migration is not supported.
        var worldSeed = Seed();
        var world = World(worldSeed.Roles, [1, 3]);
        var worldCall = Bind(removal, worldSeed, world);
        worldCall.GetCurrentEntryRequirements("shared", "npc", 60);
        worldCall.Start("shared", "npc", 60);
        Assert.Equal(1, worldSeed.Stock.GetAmount(7));
        var mismatchSeed = Seed();
        Reject(Bind(removal, mismatchSeed, World(mismatchSeed.Roles, [3, 1])), mismatchSeed, "party");
        foreach (bool before in new[] { false, true })
        {
            var partySeed = Seed();
            var party = Fixture();
            Remove(party, before ? 2 : 1, 7, 1, null);
            Set(party, before ? 1 : 2, 0x75, """{"op":"setParty","roleId0":2,"roleId1":4,"roleId2":0}""");
            Reject(Bind(party, partySeed, World(partySeed.Roles, [1, 3])), partySeed, "setParty");
        }
        // Current roles can be malformed relative to the grid: primitive refuses before partial stock debit.
        var mismatch = Seed();
        mismatch = mismatch with { Roles = new RoleLiveState(mismatch.Roles.Roles.Values.Select(role => role.RoleId == 1
            ? role with { Equipment = new int[6] } : role).ToArray()) };
        var badRole = Fixture();
        Remove(badRole, 1, 7, 3, null);
        Assert.Throws<InvalidDataException>(() => Bind(badRole, mismatch).Start("shared", "npc", 60));
        Assert.Equal(2, mismatch.Stock.GetAmount(7));
        Assert.Equal(20, mismatch.Gear.AttackBonus(1));

        // No mutation of an active battle's shared source behind its local projection, including end scripts.
        foreach (bool roleHook in new[] { false, true })
        {
            var active = Seed(roleHook: roleHook, endScript: !roleHook);
            var hooks = Fixture();
            Set(hooks, 1, 0x1f, """{"op":"addItem","itemId":7,"count":1}""");
            Battle(hooks, 2);
            Remove(hooks, 6, 7, 1, null);
            Reject(Bind(hooks, active), active, "removeItem");
        }
    }

    private sealed record Owners(TriggerBattleContent Content, BattleEquipmentState Gear, InventoryLiveState Stock,
        RoleLiveState Roles, PartyPoisonState Poisons, CountingRandom Random);

    private static Owners Seed(int stock = 2, bool roleHook = false, bool endScript = false)
    {
        int[] worn = [0, 0, 0, 7, 0, 7];
        var role = new BattleRoleSnapshot(1, 2, 100, 150, 20, 50, new(5, 2, 17),
            new Dictionary<string, BattleExperienceSnapshot> {
                ["health"] = new(7, 3, 55), ["magic"] = new(7, 3, 55), ["attack"] = new(7, 3, 55),
                ["magicPower"] = new(7, 3, 55), ["defense"] = new(7, 3, 55), ["dexterity"] = new(7, 3, 55), ["flee"] = new(7, 3, 55)
            }, worn, new int[32], new int[9], 0)
            { Attack = 40, MagicPower = 12, Defense = 20, Dexterity = 100, FleeRate = 6 };
        var grid = Enumerable.Range(0, 6).Select(id => (IReadOnlyList<int>)(id == 1 ? worn
            : id is 3 or 5 ? new[] { 7, 0, 0, 0, 0, 0 } : new int[6])).ToArray();
        var effects = Enumerable.Range(0, 6).Select(id => new BattleEquipmentEffect(10, 0, 0, 0,
            id == 1 ? new[] { 0, 0, 0, 0, 0, 0, 0, 0, 60000 } : new int[9])).ToArray();
        // The same item's wear also grants dualAttack: removing hand still clears it without recomputation.
        var player = new ClassicBattlePlayer(1, 100, 150, 40, 20, 100) { Equipment = worn, DyingScript = roleHook ? 6 : 0 };
        var content = new TriggerBattleContent([role], [player],
            new Dictionary<int, ClassicBattleGrowthData> { [1] = new(Enumerable.Repeat(100, 100).ToArray(), []) },
            new Dictionary<int, IReadOnlyList<ClassicBattleEnemy>> { [18] = new[] {
                new ClassicBattleEnemy(7, 0, 1, 0, 0, 0, 1) { BattleEndScript = endScript ? 6 : 0 } } },
            grid, [new(7, effects)], panelDeclaredRoles: [1]);
        var rows = new[] { Enumerable.Repeat(new PoisonSlot(0, 0), 16).ToArray(), Enumerable.Repeat(new PoisonSlot(0, 0), 16).ToArray() };
        rows[0][0] = new(10, 1001);
        rows[0][1] = new(11, 65535);
        return new(content, BattleEquipmentState.Initialize(content), new([new(7, stock, 9)]),
            new([role, role with { RoleId = 3, Equipment = grid[3] }, role with { RoleId = 5, Equipment = grid[5] }]),
            new([1, 3], rows, new Dictionary<int, int> { [10] = 99, [11] = 98 }), new());
    }

    private static TriggerScriptSession Bind(EventFixture fixture, Owners seed, DynamicEventWorld? world = null) =>
        TriggerScriptSession.ForSharedRoles(fixture.Load(), Language(), seed.Content, seed.Random, seed.Roles, world,
            inventoryLiveState: seed.Stock, equipmentState: seed.Gear, partyPoisons: seed.Poisons);

    private static DynamicEventWorld World(RoleLiveState roles, IReadOnlyList<int> party) => DynamicEventWorld.ForHost(
        new StaticSceneRules(new SceneMap(2, new uint[16384], [new SceneFrame(0, 32, 15)]), []),
        new StaticLeader(320, 320, 0, 0, 0, 3,
            new SceneSprite(17, Enumerable.Range(0, 12).Select(i => new SceneFrame(i, 20, 30)))), 2, 0, 0, roles, party);

    private static void Reject(TriggerScriptSession call, Owners seed, string message)
    {
        var role = seed.Roles.Roles[1];
        Assert.Contains(message, Assert.Throws<InvalidDataException>(() => call.GetCurrentEntryRequirements("shared", "npc", 60)).Message);
        Assert.Contains(message, Assert.Throws<InvalidDataException>(() => call.Start("shared", "npc", 60)).Message);
        Assert.Equal(2, seed.Stock.GetAmount(7));
        Assert.Same(role, seed.Roles.Roles[1]);
        Assert.Equal(20, seed.Gear.AttackBonus(1));
        Assert.Equal(new PoisonSlot(10, 1001), seed.Poisons.Slots(1)[0]);
        Assert.Equal(0, seed.Random.Draws);
        Assert.Equal(Guid.Empty, call.CallId);
        Assert.Equal(TriggerScriptPhase.Ready, call.Phase);
        Assert.Null(call.CurrentPosition);
    }

    private static void Win(TriggerBattleRequest request)
    {
        request.Lifecycle.SelectAttack(7);
        Assert.True(request.Lifecycle.ResolveNextAction());
        Assert.Equal(ClassicBattlePhase.Won, request.Lifecycle.Completion!.Outcome);
    }
    private static DialogueLanguagePackage Language() => new("en", [new DialogueMessageBlock(61, 61, [DialogueMessagePart.FromText("A")])]);
    private static EventFixture Fixture()
    {
        var fixture = new EventFixture();
        fixture.Index["source"]!["instructionCount"] = 9;
        fixture.Index["source"]!["byteLength"] = 72;
        while (fixture.Commands.Count < 9) fixture.Commands.Add(new JsonObject());
        for (int i = 1; i < 9; i++) Set(fixture, i, 0, """{"op":"end","mode":"keep"}""");
        return fixture;
    }
    private static void Remove(EventFixture fixture, int address, int item, int count, string? target) => Set(fixture, address, 0x20,
        new JsonObject { ["op"] = "removeItem", ["itemId"] = item, ["count"] = count, ["shortageTarget"] = target }.ToJsonString());
    private static void Battle(EventFixture fixture, int address) => Set(fixture, address, 7,
        """{"op":"startBattle","enemyTeamId":18,"lossTarget":null,"isBoss":true,"fleeTarget":null}""");
    private static void Set(EventFixture fixture, int address, int opcode, string semantic)
    {
        var command = JsonNode.Parse(semantic)!.AsObject();
        command["label"] = $"L{address:X4}";
        command["next"] = address == 8 ? null : $"L{address + 1:X4}";
        command["source"] = new JsonObject { ["index"] = address, ["opcode"] = opcode, ["operands"] = new JsonArray(123, 456, 789) };
        fixture.Commands[address] = command;
    }
    private sealed class CountingRandom : IBattleRandom
    {
        public int Draws { get; private set; }
        public int NextInclusive(int from, int to) { Draws++; return from == 0 && to == 5 ? 1 : from; }
        public float NextInclusive(float from, float to) { Draws++; return 1; }
    }
}
