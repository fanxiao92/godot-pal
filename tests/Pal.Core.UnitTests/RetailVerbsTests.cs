using System.Text.Json.Nodes;
using Xunit;

namespace Pal.Core.Tests;

/// <summary>#53 unit 3a: the eleven retail verbs (script.c 0x0013/0x0014/0x001D/0x0022/0x0024/
/// 0x0025/0x004A/0x0075/0x0078/0x009A) execute against the dynamic world.</summary>
public sealed class RetailVerbsTests
{
    [Fact]
    public void RoleLiveState_freezes_six_role_records_and_rejects_duplicate_or_invalid_seed()
    {
        var equipment = new int[6];
        var magics = new int[32];
        var statuses = new int[9];
        var hidden = new Dictionary<string, BattleExperienceSnapshot> { ["attack"] = new(17, 3, 4) };
        var role = Role(5) with { Equipment = equipment, Magics = magics, Statuses = statuses, HiddenExperience = hidden };
        var seeds = Enumerable.Range(0, 5).Select(id => Role(id)).Append(role).ToArray();
        var owner = new RoleLiveState(seeds);
        equipment[0] = 81; magics[0] = 82; statuses[0] = 83; hidden["attack"] = new(99, 9, 9);
        seeds[5] = Role(5) with { Hp = 0 };

        Assert.Equal(6, owner.Roles.Count);
        Assert.Equal(100, owner.Roles[5].Hp);
        Assert.Equal(0, owner.Roles[5].Equipment[0]);
        Assert.Equal(0, owner.Roles[5].Magics[0]);
        Assert.Equal(0, owner.Roles[5].Statuses[0]);
        Assert.Equal(new BattleExperienceSnapshot(17, 3, 4), owner.Roles[5].HiddenExperience["attack"]);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<int, BattleRoleSnapshot>)owner.Roles).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<int>)owner.Roles[5].Equipment)[0] = 3);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, BattleExperienceSnapshot>)owner.Roles[5].HiddenExperience).Clear());
        Assert.Throws<InvalidDataException>(() => new RoleLiveState([Role(1), Role(1)]));
        Assert.Throws<InvalidDataException>(() => new RoleLiveState([Role(-1)]));
        Assert.Throws<InvalidDataException>(() => new RoleLiveState([Role(6)]));
        Assert.Throws<InvalidDataException>(() => new RoleLiveState([Role(1) with { Hp = 65536 }]));
        Assert.Throws<InvalidDataException>(() => new RoleLiveState([Role(1) with { Mp = -1 }]));
        Assert.Throws<InvalidDataException>(() => new RoleLiveState([null!]));
        Assert.Throws<ArgumentNullException>(() => new RoleLiveState(null!));
    }

    [Fact]
    public void Shared_world_scripts_write_the_owner_and_retained_views_without_reseeding()
    {
        var owner = new RoleLiveState([Role(0), Role(1) with { Hp = 28, Mp = 10, MaxHp = 240, MaxMp = 240 }, Role(5)]);
        var world = DynamicEventWorld.ForHost(Rules(), Leader(), 2, 0, 0, owner);
        var view = world.PartyVitals;
        var vitals = view[0];
        var records = owner.Roles;
        Assert.Same(owner, world.RoleLiveState);
        Assert.Throws<InvalidOperationException>(() => world.InitializePartyVitals(0, 1, 150, 1, 50));
        Assert.Throws<NotSupportedException>(() => ((IDictionary<int, DynamicRoleVitals>)view).Clear());
        var fixture = Fixture(false);
        fixture.EventFile["entries"]![0]!["target"] = "L0006";
        fixture.EventFile["entries"]![0]!["sourceAddress"] = 6;
        var script = new TriggerScriptSession(fixture.Load(), Language(), world);
        script.Start("story", "owner28", 0);
        Assert.Equal((150, 50), (records[0].Hp, records[0].Mp));
        Assert.Equal((150, 50), (vitals.Hp, vitals.Mp));
        Assert.Equal((240, 240), (view[1].Hp, view[1].Mp));
        Assert.Equal((100, 20), (records[5].Hp, records[5].Mp));
        Assert.Equal(new BattleExperienceSnapshot(5, 2, 17), records[0].PrimaryExperience);
        Assert.Throws<InvalidOperationException>(() => world.InitializePartyVitals(0, 1, 150, 1, 50));

        var drain = VitalScript(world, scope: 0, amount: -9999);
        drain.Start("story", "owner28", 0);
        Assert.Equal((0, 0), (vitals.Hp, vitals.Mp));
        var revive = VitalScript(world, scope: 0, amount: 2, revive: true);
        revive.Start("story", "owner28", 0);
        Assert.Equal((30, 0), (vitals.Hp, vitals.Mp));
        Assert.Equal(30, records[0].Hp);
    }

    // Independent literals from global.c PAL_IncreaseHPMP: WORD write precedes signed SHORT
    // testing (not mathematical clamp); success compares the resulting two words.
    [Theory]
    [InlineData(100, 150, 20, 50, 10, 110, 30, true)]
    [InlineData(100, 150, 20, 50, 0, 100, 20, false)]
    [InlineData(100, 150, 20, 50, -20, 80, 0, true)]
    [InlineData(150, 150, 50, 50, 10, 150, 50, false)]
    [InlineData(0, 150, 20, 50, 10, 0, 20, false)]
    [InlineData(1, 150, 20, 50, -1, 0, 19, true)]
    [InlineData(32767, 65535, 32767, 65535, 1, 0, 0, true)]
    [InlineData(65535, 65535, 65535, 65535, 1, 0, 0, true)]
    [InlineData(65530, 65535, 65530, 65535, 10, 4, 4, true)]
    [InlineData(1, 65535, 0, 65535, -32768, 0, 0, true)]
    [InlineData(32768, 65535, 65535, 65535, -32768, 0, 32767, true)]
    [InlineData(40000, 65535, 50000, 65535, -32768, 7232, 17232, true)]
    [InlineData(1, 65535, 0, 65535, 32767, 0, 32767, true)]
    [InlineData(1, 150, 0, 50, -2, 0, 0, true)]
    [InlineData(0, 0, 0, 0, -1, 0, 0, false)]
    public void ChangeHpMp_matches_WORD_SHORT_alive_and_actual_change_rules_in_shared_and_legacy_worlds(
        int hp, int maxHp, int mp, int maxMp, int amount, int expectedHp, int expectedMp, bool success)
    {
        foreach (bool shared in new[] { false, true })
        {
            var owner = new RoleLiveState([Role(5) with { Hp = hp, MaxHp = maxHp, Mp = mp, MaxMp = maxMp }, Role(0)]);
            var world = shared ? DynamicEventWorld.ForHost(Rules(), Leader(), 2, 0, 0, owner)
                : DynamicEventWorld.ForHost(Rules(), Leader(), 2, 0, 0);
            if (!shared) world.InitializePartyVitals(5, hp, maxHp, mp, maxMp);
            var script = VitalScript(world, 0, amount);
            script.Start("story", "owner28", 5); // caller is RoleId, not party index 0
            Assert.Equal((expectedHp, expectedMp), (world.PartyVitals[5].Hp, world.PartyVitals[5].Mp));
            Assert.Equal(success, script.ScriptSuccess);
            if (shared)
            {
                Assert.Equal((expectedHp, expectedMp), (owner.Roles[5].Hp, owner.Roles[5].Mp));
                Assert.Equal((100, 20), (owner.Roles[0].Hp, owner.Roles[0].Mp));
            }
        }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(150, false)]
    [InlineData(100, false)]
    [InlineData(0, true)]
    [InlineData(150, true)]
    [InlineData(100, true)]
    public void ChangeHpMp_party_scope_preserves_success_flag_even_without_actual_changes(int hp, bool failedBefore)
    {
        var owner = new RoleLiveState([Role(0) with { Hp = hp, Mp = 50 }, Role(1) with { Hp = hp, Mp = 50 }, Role(5)]);
        var world = DynamicEventWorld.ForHost(Rules(), Leader(), 2, 0, 0, owner);
        var fixture = Fixture(false);
        fixture.EventFile["entries"]![0]!["target"] = "L0006";
        fixture.EventFile["entries"]![0]!["sourceAddress"] = 6;
        if (failedBefore)
        {
            fixture.EventFile["commands"]![8]!["op"] = "markFailed";
            fixture.EventFile["commands"]![8]!["source"]!["opcode"] = 0x41;
        }
        fixture.EventFile["commands"]![9]!["scope"] = 65535;
        var script = new TriggerScriptSession(fixture.Load(), Language(), world);
        script.Start("story", "owner28", 5);
        Assert.Equal(!failedBefore, script.ScriptSuccess); // party branch ignores helper result
        Assert.Equal(hp == 0 ? 0 : 150, owner.Roles[0].Hp);
        Assert.Equal(hp == 0 ? 0 : 150, owner.Roles[1].Hp);
        Assert.Equal((100, 20), (owner.Roles[5].Hp, owner.Roles[5].Mp));
    }

    [Fact]
    public void ChangeHpMp_success_does_not_reset_an_earlier_failure_or_roll_back_applied_words()
    {
        var owner = new RoleLiveState([Role(0)]);
        var world = DynamicEventWorld.ForHost(Rules(), Leader(), 2, 0, 0, owner);
        var fixture = Fixture(false);
        fixture.EventFile["entries"]![0]!["target"] = "L0008";
        fixture.EventFile["entries"]![0]!["sourceAddress"] = 8;
        fixture.EventFile["commands"]![8]!["op"] = "markFailed";
        fixture.EventFile["commands"]![8]!["source"]!["opcode"] = 0x41;
        fixture.EventFile["commands"]![9]!["scope"] = 0;
        fixture.EventFile["commands"]![9]!["amount"] = 10;
        var script = new TriggerScriptSession(fixture.Load(), Language(), world);
        script.Start("story", "owner28", 0);
        Assert.False(script.ScriptSuccess);
        Assert.Equal((110, 30), (owner.Roles[0].Hp, owner.Roles[0].Mp));
    }

    private static TriggerScriptSession VitalScript(DynamicEventWorld world, int scope, int amount, bool revive = false)
    {
        var fixture = Fixture(false);
        fixture.EventFile["entries"]![0]!["target"] = "L0009";
        fixture.EventFile["entries"]![0]!["sourceAddress"] = 9;
        var command = fixture.EventFile["commands"]![9]!;
        command["scope"] = scope;
        if (revive)
        {
            command["op"] = "revivePlayer";
            command.AsObject().Remove("amount");
            command["healthTenths"] = amount;
            command["source"]!["opcode"] = 0x22;
        }
        else command["amount"] = amount;
        return new(fixture.Load(), Language(), world);
    }

    private static BattleRoleSnapshot Role(int id) => new(id, 2, 100, 150, 20, 50, new(5, 2, 17),
        new Dictionary<string, BattleExperienceSnapshot> { ["attack"] = new(17, 3, 4) },
        new int[6], new int[32], new int[9], 0);

    [Fact]
    public void World_verbs_apply_entries_positions_states_party_battlefield_and_vitals()
    {
        DynamicEventSession session = Session();
        RunTrigger(session);

        var owner = session.World.Objects.Single(o => o.Id == 28);
        Assert.Equal(11, owner.AutoEntry);                                        // 0x0024
        Assert.Equal(2500, session.World.Objects.Single(o => o.Id == 29).TriggerEntry); // 0x0025
        Assert.Equal((1280, 256), (owner.X, owner.Y));                           // 0x0013
        Assert.Equal(0, owner.Direction);                                        // 0x0014 south + frame 3
        Assert.Equal(3, owner.CurrentFrame);
        Assert.Equal((2, 2), (session.World.Objects.Single(o => o.Id == 29).State,
            session.World.Objects.Single(o => o.Id == 30).State));               // 0x009A range 29..30
        Assert.Equal(new[] { 0, 1 }, session.World.PartyRoles);                   // 0x0075 words 1,2
        Assert.Equal(24, session.World.Battlefield);                              // 0x004A
        Assert.Equal((150, 100), (session.World.PartyVitals[0].Hp, session.World.PartyVitals[0].Mp)); // clamped at max
        Assert.Equal((240, 240), (session.World.PartyVitals[1].Hp, session.World.PartyVitals[1].Mp));
    }

    [Fact]
    public void Revive_returns_dead_roles_at_maxhp_tenths_without_touching_mp()
    {
        DynamicEventSession session = Session(revive: true);
        RunTrigger(session);
        Assert.Equal(DynamicEventPhase.Exploring, session.Phase);
        Assert.Equal(15, session.World.PartyVitals[0].Hp);  // 150 * 1 / 10
        Assert.Equal(24, session.World.PartyVitals[1].Hp);  // 240 * 1 / 10
        Assert.Equal((0, 0), (session.World.PartyVitals[0].Mp, session.World.PartyVitals[1].Mp)); // still drained
    }

    [Fact]
    public void Party_vital_verbs_reject_when_the_host_did_not_supply_the_profile()
    {
        var events = Fixture(revive: false).Load();
        var session = new DynamicEventSession(Rules(), Leader(), events, Language(), Entry(events), 2, 0, 0);
        session.PressSearch();
        Assert.True(session.Update(0));
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => session.StartTrigger(session.PendingTrigger!.Id));
        Assert.Contains("vitals for role", error.Message, StringComparison.Ordinal);
    }

    private static void RunTrigger(DynamicEventSession session)
    {
        session.PressSearch();
        Assert.True(session.Update(0));
        Assert.NotNull(session.PendingTrigger);
        session.StartTrigger(session.PendingTrigger.Id);
        // The verbs execute synchronously; the return phase then settles back to exploring.
        for (uint tick = 100; session.Phase != DynamicEventPhase.Exploring && tick <= 1000; tick += 100)
            Assert.True(session.Update(tick));
        Assert.Equal(DynamicEventPhase.Exploring, session.Phase);
    }

    private static DynamicEventSession Session(bool revive = false)
    {
        var events = Fixture(revive).Load();
        var session = new DynamicEventSession(Rules(), Leader(), events, Language(), Entry(events), 2, 0, 0);
        session.InitializePartyVitals(0, 1, 150, 100, 100);
        session.InitializePartyVitals(1, 28, 240, 10, 240);
        return session;
    }

    private static StaticSceneRules Rules() => new(
        new SceneMap(2, new uint[16384], [new SceneFrame(0, 32, 15)]),
    [
        new SceneObject(28, 304, 328, 2, 0, 0, 210, 0, 0, 0) { TriggerMode = 1, TriggerEntry = 1 },
        new SceneObject(29, 300, 300, 1, 0, 0, 211, 0, 0, 0),
        new SceneObject(30, 320, 300, 1, 0, 0, 212, 0, 0, 0),
    ]);

    private static StaticLeader Leader() => new(320, 320, 0, 0, 0, 3,
        new SceneSprite(17, Enumerable.Range(0, 12).Select(i => new SceneFrame(i, 20, 30))));

    private static DialogueLanguagePackage Language() => new("zh-Hans", []);

    private static TriggerEventEntry Entry(TriggerEventCollection events) =>
        events.Entries.Single(entry => entry.OwnerId == 28 && entry.Slot == "wTriggerScript");

    internal sealed class VerbsFixture
    {
        public JsonObject Index { get; } = new()
        {
            ["formatVersion"] = 1, ["profile"] = "faithful",
            ["source"] = new JsonObject { ["archive"] = "SSS.MKF", ["chunk"] = 4, ["byteOrder"] = "little",
                ["instructionCount"] = 24, ["byteLength"] = 192,
                ["sha256"] = "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd" },
            ["files"] = new JsonArray("story")
        };
        public JsonObject EventFile { get; } = Build();
        private static JsonObject Build()
        {
            var commands = new JsonArray();
            int address = 0;
            void Put(string op, int opcode, int a, int b, int c, params (string Key, JsonNode? Value)[] fields)
            {
                var command = new JsonObject { ["label"] = $"L{address:X4}", ["op"] = op,
                    ["next"] = address + 1 < 24 ? $"L{address + 1:X4}" : null };
                foreach (var (key, value) in fields) command[key] = value;
                command["source"] = new JsonObject { ["index"] = address, ["opcode"] = opcode, ["operands"] = new JsonArray(a, b, c) };
                commands.Add(command);
                address++;
            }
            Put("raw", 134, 0, 65535, 8); // unreached retained row so the entry address is nonzero
            Put("setTriggerEntry", 0x25, 29, 2500, 0, ("eventObjectId", 29), ("entry", 2500)); // not the caller: its entry word is restored on completion
            Put("setAutoEntry", 0x24, 28, 11, 0, ("eventObjectId", 28), ("entry", 11)); // a valid end address for the auto runner
            Put("setObjectPosition", 0x13, 28, 1280, 256, ("eventObjectId", 28), ("x", 1280), ("y", 256));
            Put("setObjectGesture", 0x14, 3, 0, 0, ("frame", 3));
            Put("setStateRange", 0x9a, 29, 30, 2, ("fromEventObjectId", 29), ("toEventObjectId", 30), ("state", 2));
            Put("setParty", 0x75, 1, 2, 0, ("roleId0", 1), ("roleId1", 2), ("roleId2", 0));
            Put("setBattlefield", 0x4a, 24, 0, 0, ("battlefieldId", 24));
            Put("noop", 0x78, 0, 0, 0);
            Put("changeHpMp", 0x1d, 1, 9999, 0, ("scope", 1), ("amount", 9999));
            Put("end", 0, 0, 0, 0, ("mode", "keep")); // explicit stop; the filler stays unreached
            Put("end", 0, 0, 0, 0, ("mode", "keep")); // second stop so the revive variant can chain
            while (address < 24) Put("raw", 134, 0, 65535, 8); // retained filler: dense address coverage
            return new JsonObject
            {
                ["formatVersion"] = 1, ["profile"] = "faithful", ["fileId"] = "story",
                ["entries"] = new JsonArray(new JsonObject { ["id"] = "owner28",
                    ["owner"] = new JsonObject { ["type"] = "eventObject", ["id"] = 28 },
                    ["slot"] = "wTriggerScript", ["kind"] = "trigger", ["target"] = "L0001", ["sourceAddress"] = 1 }),
                ["commands"] = commands
            };
        }
        public TriggerEventCollection Load() => TriggerEventCollection.Parse(Index.ToJsonString(),
            new Dictionary<string, string> { ["story"] = EventFile.ToJsonString() });
    }

    internal static VerbsFixture Fixture(bool revive)
    {
        var fixture = new VerbsFixture();
        if (revive)
        {
            var commands = fixture.EventFile["commands"]!.AsArray();
            var restore = (JsonObject)commands[9]!; // the changeHpMp row
            restore["amount"] = -9999; // drain first so revivePlayer has dead roles
            var stop = (JsonObject)commands[10]!; // the explicit end row becomes the revive step
            stop["op"] = "revivePlayer";
            stop["scope"] = 1;
            stop["healthTenths"] = 1;
            stop.Remove("mode");
            stop["next"] = "L000B"; // flows into the retained filler; the call stops at its next
            var source = (JsonObject)stop["source"]!;
            source["opcode"] = 34;
            source["operands"] = new JsonArray(1, 1, 0);
        }
        return fixture;
    }
}
