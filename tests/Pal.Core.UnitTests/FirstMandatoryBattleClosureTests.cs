using System.Text.Json.Nodes;
using Pal.TestFixtures;
using Xunit;

namespace Pal.Core.Tests;

/// <summary>Designated functional RED for #53 unit 1: the capability precheck must accept a real
/// resource closure and open @09C3, where base 7fb6af2 rejects the same search unconditionally
/// because the closure is only declared.</summary>
public sealed class FirstMandatoryBattleClosureTests
{
    [Fact]
    public void Real_closure_verifies_and_opens_the_owner28_search_without_side_effects()
    {
        (FirstMandatoryBattleProfile profile, RealClosureFixture fixture) = PreparedProfile();
        FirstMandatoryBattleClosureReceipt receipt = FirstMandatoryBattleClosure.Verify(profile,
            fixture.Events(), fixture.Chinese(), fixture.English(), fixture.Content(), Images());

        Assert.Equal(0x09c3, receipt.TriggerEntryAddress);
        Assert.Equal(profile.ContextSha256, receipt.ContextSha256);
        Assert.Equal(new[] { 18 }, receipt.VerifiedBattleTeamIds);

        var session = new FirstMandatoryBattleSession(profile, 2, Rules(), Leader(), receipt);
        session.Update(0);
        session.PressSearch();
        Assert.True(session.Update(100));
        Assert.Equal(new FirstMandatoryBattleTriggerRequest(0x09c3, 28), session.PendingTrigger);
        Assert.Equal(1264, session.State.World.Leader.X); // no side effect before the host consumes the trigger
        Assert.Equal(0x09c3, session.State.World.EventObjects.Single(item => item.OwnerId == 28).TriggerEntry);
    }

    [Fact]
    public void Search_opens_the_trigger_only_once_and_parks_exploration()
    {
        (FirstMandatoryBattleProfile profile, RealClosureFixture fixture) = PreparedProfile();
        FirstMandatoryBattleClosureReceipt receipt = FirstMandatoryBattleClosure.Verify(profile,
            fixture.Events(), fixture.Chinese(), fixture.English(), fixture.Content(), Images());
        var session = new FirstMandatoryBattleSession(profile, 2, Rules(), Leader(), receipt);
        session.Update(0);
        session.PressSearch();
        Assert.True(session.Update(100));
        session.PressSearch();
        session.Press(ExplorationKey.SouthPrimary);
        Assert.False(session.Update(200));
        Assert.Equal(new FirstMandatoryBattleTriggerRequest(0x09c3, 28), session.PendingTrigger);
    }

    [Fact]
    public void Receipt_from_another_context_does_not_open_this_entry()
    {
        (FirstMandatoryBattleProfile profile, RealClosureFixture fixture) = PreparedProfile();
        FirstMandatoryBattleClosureReceipt receipt = FirstMandatoryBattleClosure.Verify(profile,
            fixture.Events(), fixture.Chinese(), fixture.English(), fixture.Content(), Images());
        var otherContext = BattleContextTestFixture.Valid();
        otherContext.Document["snapshot"]!["world"]!["musicId"] = 32;
        FirstMandatoryBattleProfile other = Load(otherContext, prepared: true);
        Assert.NotEqual(profile.ContextSha256, other.ContextSha256);
        Assert.Throws<InvalidDataException>(() => new FirstMandatoryBattleSession(other, 2, Rules(), Leader(), receipt));
    }

    [Fact]
    public void Unverified_closure_still_rejects_the_search_before_side_effects()
    {
        (FirstMandatoryBattleProfile profile, _) = PreparedProfile();
        var session = new FirstMandatoryBattleSession(profile, 2, Rules(), Leader());
        session.Update(0);
        session.PressSearch();
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => session.Update(100));
        Assert.Equal(FirstMandatoryBattleSession.UnsupportedTriggerDiagnostic, error.Message);
        Assert.Null(session.PendingTrigger);
    }

    [Theory]
    [InlineData("wrong-operands")]
    [InlineData("single-enemy")]
    [InlineData("missing-dying-hook")]
    [InlineData("undeclared-equipment-grid")]
    [InlineData("missing-dual-attack-object")]
    [InlineData("wrong-images")]
    [InlineData("missing-english-message")]
    public void Every_closure_gap_rejects_before_the_entry_opens(string fault)
    {
        (FirstMandatoryBattleProfile profile, RealClosureFixture fixture) = PreparedProfile(fault);
        Assert.Throws<InvalidDataException>(() => FirstMandatoryBattleClosure.Verify(profile,
            fixture.Events(), fixture.Chinese(), fixture.English(), fixture.Content(), Images(fault == "wrong-images")));
    }

    [Fact]
    public void Contract_only_closure_reports_the_first_declared_entry()
    {
        (FirstMandatoryBattleProfile profile, RealClosureFixture fixture) = ContractOnlyProfile();
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => FirstMandatoryBattleClosure.Verify(profile,
            fixture.Events(), fixture.Chinese(), fixture.English(), fixture.Content(), Images()));
        Assert.Equal(string.Format(FirstMandatoryBattleClosure.ContractOnlyDiagnostic, profile.Closure.Entries[0].Id), error.Message);
    }

    private static (FirstMandatoryBattleProfile Profile, RealClosureFixture Fixture) PreparedProfile(string fault = "none") =>
        (Load(BattleContextTestFixture.Valid(), prepared: true), new RealClosureFixture(fault));

    private static (FirstMandatoryBattleProfile Profile, RealClosureFixture Fixture) ContractOnlyProfile() =>
        (Load(BattleContextTestFixture.Valid(), prepared: false), new RealClosureFixture("none"));

    private static FirstMandatoryBattleProfile Load(BattleContextTestFixture context, bool prepared)
    {
        context.Document["snapshot"]!["world"]!["leader"]!["x"] = 1264;
        context.Document["snapshot"]!["world"]!["leader"]!["y"] = 248;
        context.Document["snapshot"]!["world"]!["searchProof"]!["sampleIndex"] = 1;
        if (prepared)
            foreach (var node in context.Document["closure"]!["entries"]!.AsArray())
                node!["status"] = "prepared";
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(context.Document.ToJsonString());
        string hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
        byte[] index = System.Text.Encoding.UTF8.GetBytes(
            "{\"formatVersion\":1,\"profile\":\"first-mandatory-battle-pre-trigger-v1\",\"contextFile\":\"battle-context.json\",\"contextSha256\":\"" + hash + "\"}");
        return FirstMandatoryBattleProfile.Load(index, new Dictionary<string, byte[]> { ["battle-context.json"] = bytes });
    }

    internal static StaticSceneRules Rules()
    {
        var owner = new SceneObject(28, 1280, 256, 2, 0, 0, 210, 0, 0, 0) { TriggerMode = 1, TriggerEntry = 0x09c3 };
        return new StaticSceneRules(new SceneMap(12, new uint[64 * 128 * 2], [new SceneFrame(0, 1, 1)]), [owner]);
    }

    internal static StaticLeader Leader() => new(1264, 248, 3, 0, 0, 0,
        new SceneSprite(17, [new SceneFrame(0, 1, 1), new SceneFrame(9, 1, 1)]));

    internal static FirstMandatoryBattleImageClosure Images(bool wrong = false) => wrong
        ? new(96, "ABC96", ["F0", "F1"], 24, "FBP24", true)
        : new(97, "ABC97", ["F0", "F1"], 24, "FBP24", true);
}

/// <summary>Original synthetic stream carrying the frozen real identities (owner28/@09C3, @09FE
/// team18 operands (0012,A073,0000), Won @09FF, dynamic auto @0A36/@0A3B, lost @A073→0x004E,
/// role1 dying @A96E-@A987 and role0 friend-death @A9B5-@A9D1 with the retail four-band random
/// structure). Original text and values; not retail data.</summary>
internal sealed class RealClosureFixture(string fault)
{
    private const int Entry = 0x09c3, PreDialog = 0x09c4, Battle = 0x09fe, Won = 0x09ff, WonEnd = 0x0a32,
        AutoStart = 0x0a36, AutoEnd = 0x0a3b, Lost = 0xa073, LostDialog = 0xa074,
        Dying = 0xa96e, FriendDeath = 0xa9b5;
    private const int InstructionCount = 0xa9d2;

    public TriggerEventCollection Events() => TriggerEventCollection.Parse(Index.ToJsonString(),
        new Dictionary<string, string> { ["story"] = EventFile.ToJsonString() });

    public DialogueLanguagePackage Chinese() => Language("zh-Hans", includeFirst: true);
    public DialogueLanguagePackage English() => Language("en", includeFirst: fault != "missing-english-message");

    private static DialogueLanguagePackage Language(string id, bool includeFirst)
    {
        // Single-message blocks plus the consecutive-show pair blocks the dying bands read.
        var singles = Enumerable.Range(10, 15).Where(message => includeFirst || message != 10)
            .Select(message => new DialogueMessageBlock(message, message, [DialogueMessagePart.FromText(id + message)]));
        var pairs = Enumerable.Range(0, 4).Select(band => new DialogueMessageBlock(13 + band * 2, 14 + band * 2,
            [DialogueMessagePart.FromText(id + "p" + band)]));
        return new(id, singles.Concat(pairs));
    }

    public TriggerBattleContent Content()
    {
        int[] role0 = [196, 225, 208, 166, 235, 249], role1 = [197, 0, 210, 170, 237, 248];
        BattleRoleSnapshot[] roles = [Role(0, 1, 150, 150, 100, 100, 2, role0), Role(1, 5, 28, 240, 10, 240, 0, role1)];
        ClassicBattlePlayer[] players =
        [
            new(0, 150, 150, 40, 20, 100) { Equipment = role0, Statuses = new int[9], Poisons = [0],
                FriendDeathScript = 0xA9B5, CoveredByRoleId = 2 },
            new(1, 28, 240, 20, 0, 100) { Equipment = role1, Statuses = new int[9], Poisons = [0],
                DyingScript = fault == "missing-dying-hook" ? 0 : 0xA96E, CoveredByRoleId = 0 }
        ];
        var growth = new Dictionary<int, ClassicBattleGrowthData>
        {
            [0] = new(Enumerable.Repeat(100, 100).ToArray(), []),
            [1] = new(Enumerable.Repeat(100, 100).ToArray(), [])
        };
        var enemy = new ClassicBattleEnemy(7, 0, 43, 8, 0, 500, 1) { Statuses = new int[9], Poisons = [0] };
        List<ClassicBattleEnemy> enemies = fault == "single-enemy" ? [enemy] : [enemy, enemy with { Id = 8 }];
        List<IReadOnlyList<int>>? grid = fault == "undeclared-equipment-grid" ? null
            : [role0, role1, new int[6], new int[6], new int[6], new int[6]];
        List<BattleEquipmentObject> objects = grid is null ? [] : role0.Concat(role1)
            .Where(id => id != 0 && (fault != "missing-dual-attack-object" || id != 170)).Distinct()
            .Select(id => new BattleEquipmentObject(id, Effects(id))).ToList();
        return new TriggerBattleContent(roles, players, growth,
            new Dictionary<int, IReadOnlyList<ClassicBattleEnemy>> { [18] = enemies }, grid, objects);
    }

    private static IReadOnlyList<BattleEquipmentEffect> Effects(int objectId) => Enumerable.Range(0, 6).Select(role =>
        objectId == 170 && role == 1
            ? new BattleEquipmentEffect(0, 0, 0, 0, [0, 0, 0, 0, 0, 0, 0, 0, 32760])
            : new BattleEquipmentEffect(0, 0, 0, 0, new int[9])).ToList();

    private static BattleRoleSnapshot Role(int id, int level, int hp, int maxHp, int mp, int maxMp, int coveredBy, int[] equipment) => new(
        id, level, hp, maxHp, mp, maxMp, new(0, level, 0),
        new Dictionary<string, BattleExperienceSnapshot>
        {
            ["health"] = new(0, level, 0), ["magic"] = new(0, level, 0), ["attack"] = new(0, level, 0),
            ["magicPower"] = new(0, level, 0), ["defense"] = new(0, level, 0), ["dexterity"] = new(0, level, 0), ["flee"] = new(0, level, 0)
        }, equipment, new int[32], new int[9], coveredBy);

    public JsonObject Index { get; } = new()
    {
        ["formatVersion"] = 1, ["profile"] = "faithful",
        ["source"] = new JsonObject { ["archive"] = "SSS.MKF", ["chunk"] = 4, ["byteOrder"] = "little",
            ["instructionCount"] = InstructionCount, ["byteLength"] = InstructionCount * 8,
            ["sha256"] = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc" },
        ["files"] = new JsonArray("story")
    };

    private JsonObject? file;
    public JsonObject EventFile => file ??= BuildFile();

    private static void Put(JsonArray commands, int address, string op, JsonNode? next, int opcode, JsonArray operands,
        params (string Key, JsonNode? Value)[] fields)
    {
        var command = new JsonObject { ["label"] = $"L{address:X4}", ["op"] = op, ["next"] = next };
        foreach (var (key, value) in fields) command[key] = value;
        command["source"] = new JsonObject { ["index"] = address, ["opcode"] = opcode, ["operands"] = operands.DeepClone() };
        commands[address] = command;
    }

    private static JsonNode? NextOf(int address) => address + 1 < InstructionCount ? $"L{address + 1:X4}" : null;

    private JsonObject BuildFile()
    {
        // The strict collection covers every source address exactly once and every next is the
        // WORD-order successor; the unused retail-range rows stay retained raw data, like the
        // real export's unverified opcodes.
        var commands = new JsonArray();
        for (int address = 0; address < InstructionCount; address++)
            commands.Add(new JsonObject { ["label"] = $"L{address:X4}", ["op"] = "raw", ["next"] = NextOf(address),
                ["source"] = new JsonObject { ["index"] = address, ["opcode"] = 134, ["operands"] = new JsonArray(0, 65535, 8) } });
        JsonArray Words(int a, int b, int c) => new(a, b, c);
        (int team, int loss, int boss) = fault == "wrong-operands" ? (18, 0, 0) : (0x0012, 0xa073, 0);
        void Idle(int from, int to)
        {
            for (int address = from; address <= to; address++)
                Put(commands, address, "face", NextOf(address), 15, Words(0, 0, 0), ("direction", 0), ("frame", 0));
        }

        Put(commands, Entry, "setDialog", NextOf(Entry), 60, Words(0, 0, 0),
            ("box", "upper"), ("portraitId", 0), ("color", 0), ("rngWord", 0));
        Put(commands, PreDialog, "showDialog", NextOf(PreDialog), 65535, Words(10, 0, 0),
            ("messageId", 10), ("descriptionLineWord", 0));
        Idle(PreDialog + 1, Battle - 1);
        Put(commands, Battle, "startBattle", NextOf(Battle), 7, Words(team, loss, boss),
            ("teamId", 18), ("lossTarget", $"L{Lost:X4}"), ("isBoss", true), ("fleeTarget", null));
        Put(commands, Won, "showDialog", NextOf(Won), 65535, Words(11, 0, 0),
            ("messageId", 11), ("descriptionLineWord", 0));
        Idle(Won + 1, WonEnd - 1);
        Put(commands, WonEnd, "end", NextOf(WonEnd), 0, Words(0, 0, 0), ("mode", "keep"));
        if (fault != "missing-dynamic-auto")
        {
            Put(commands, AutoStart, "face", NextOf(AutoStart), 15, Words(1, 0, 0), ("direction", 1), ("frame", 0));
            Put(commands, AutoEnd, "walkStep", NextOf(AutoEnd), 11, Words(0, 0, 0), ("direction", 0));
        }
        Put(commands, Lost, "setDialog", NextOf(Lost), 61, Words(0, 0, 0),
            ("box", "lower"), ("portraitId", 0), ("color", 0), ("rngWord", 0));
        Put(commands, LostDialog, "showDialog", NextOf(LostDialog), 65535, Words(12, 0, 0),
            ("messageId", 12), ("descriptionLineWord", 0));
        Put(commands, LostDialog + 1, "requestReload", NextOf(LostDialog + 1), 0x4e, Words(0, 0, 0));

        Hook(commands, Dying, 13, dying: true);
        Hook(commands, FriendDeath, 17, dying: false);
        return new JsonObject
        {
            ["formatVersion"] = 1, ["profile"] = "faithful", ["fileId"] = "story",
            ["entries"] = new JsonArray(new JsonObject { ["id"] = "owner28",
                ["owner"] = new JsonObject { ["type"] = "eventObject", ["id"] = 28 },
                ["slot"] = "wTriggerScript", ["kind"] = "trigger", ["target"] = $"L{Entry:X4}", ["sourceAddress"] = Entry }),
            ["commands"] = commands
        };
    }

    /// <summary>Four random bands inside the frozen WORD range: the dying hook shows dialogue, the
    /// friend-death hook restores or boosts the caller (role-hook-only verbs).</summary>
    private static void Hook(JsonArray commands, int start, int firstMessage, bool dying)
    {
        int[] sizes = dying ? [4, 4, 4, 4] : [4, 4, 5, 5];
        int[] bandStart = new int[4];
        int address = start + 3;
        for (int band = 0; band < 4; band++) { bandStart[band] = address; address += sizes[band]; }
        JsonArray Words(int a, int b, int c) => new(a, b, c);

        Put(commands, start, "randomJump", NextOf(start), 6, Words(75, bandStart[3], 0), ("rate", 75), ("to", $"L{bandStart[3]:X4}"));
        Put(commands, start + 1, "randomJump", NextOf(start + 1), 6, Words(66, bandStart[2], 0), ("rate", 66), ("to", $"L{bandStart[2]:X4}"));
        Put(commands, start + 2, "randomJump", NextOf(start + 2), 6, Words(50, bandStart[1], 0), ("rate", 50), ("to", $"L{bandStart[1]:X4}"));

        for (int band = 0; band < 4; band++)
        {
            int at = bandStart[band], message = firstMessage + band * 2, size = sizes[band];
            Put(commands, at, "setDialog", NextOf(at), band % 2 == 0 ? 60 : 61, Words(0, 0, 0),
                ("box", band % 2 == 0 ? "upper" : "lower"), ("portraitId", 0), ("color", 0), ("rngWord", 0));
            Put(commands, at + 1, "showDialog", NextOf(at + 1), 65535, Words(message, 0, 0),
                ("messageId", message), ("descriptionLineWord", 0));
            if (dying)
                Put(commands, at + 2, "showDialog", NextOf(at + 2), 65535, Words(message + 1, 0, 0),
                    ("messageId", message + 1), ("descriptionLineWord", 0));
            else
                Put(commands, at + 2, band % 2 == 0 ? "changeHp" : "changeMp", NextOf(at + 2), band % 2 == 0 ? 27 : 28,
                    Words(0, 9999, 0), ("scope", 0), ("amount", 9999));
            if (!dying && band >= 2)
                Put(commands, at + 3, "boostStat", NextOf(at + 3), 48, Words(17, 25, 0),
                    ("field", 17), ("percent", 25), ("roleOperand", 0));
            Put(commands, at + size - 1, "end", NextOf(at + size - 1), 0, Words(0, 0, 0), ("mode", "keep"));
        }
    }
}
