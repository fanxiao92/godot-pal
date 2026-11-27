using System.Text.Json.Nodes;
using Pal.Core.Dialogue;
using Pal.Core.Events;
using Pal.Core.Exploration;
using Pal.Core.Tests;
using Xunit;

namespace Pal.Core.UnitTests;

public sealed class OpcodeCapabilityClosureTests
{
    private static DialogueLanguagePackage MinimalLanguage() =>
        new("en", [new DialogueMessageBlock(61, 61, [DialogueMessagePart.FromText("A")])]);

    private static TriggerEventCollection BuildSingleCommandCollection(string commandJson, bool isAutoEntry = false)
    {
        string targetLabel = isAutoEntry ? "L0002" : "L0001";
        int targetAddr = isAutoEntry ? 2 : 1;
        string sharedJson = $$$"""
        {
          "formatVersion": 1,
          "profile": "faithful",
          "fileId": "shared",
          "entries": [
            {"id":"testEntry","owner":{"type":"eventObject","id":5},"slot":"wTriggerScript","kind":"trigger","target":"{{{targetLabel}}}","sourceAddress":{{{targetAddr}}}}
          ],
          "commands": [
            {"label":"L0000","op":"raw","next":"L0001","source":{"index":0,"opcode":134,"operands":[0,65535,8]}},
            {{{commandJson}}},
            {"label":"L0002","op":"end","mode":"keep","next":null,"source":{"index":2,"opcode":0,"operands":[0,0,0]}}
          ]
        }
        """;
        const string indexJson = """
        {
          "formatVersion": 1,
          "profile": "faithful",
          "source": {"archive":"SSS.MKF","chunk":4,"byteOrder":"little","instructionCount":3,"byteLength":24,"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},
          "files": ["shared"]
        }
        """;
        return TriggerEventCollection.Parse(indexJson, new Dictionary<string, string> { ["shared"] = sharedJson });
    }

    private static DynamicEventSession CreateAutoSession(TriggerEventCollection events)
    {
        var sprite = new SceneSprite(42, Enumerable.Range(0, 12).Select(i => new SceneFrame(i, 16, 24)));
        var rules = new StaticSceneRules(
            new SceneMap(1, new uint[16384], [new SceneFrame(0, 32, 15)]),
            [
                new SceneObject(5, 304, 328, 2, 0, 0, 42, 3, 0, 0, sprite) { TriggerMode = 1, TriggerEntry = 2, AutoEntry = 1 }
            ]);
        var leader = DynamicEventSessionTests.DynamicFixture.Leader();
        var entry = events.GetEntry("shared", "testEntry");
        return new DynamicEventSession(rules, leader, events, MinimalLanguage(), entry, 1, 0, 0);
    }

    [Fact]
    public void DynamicEventSession_SupportedOpcodes_matches_real_ValidateAutoClosure_bidirectionally()
    {
        // 1. Every opcode declared in DynamicEventSession.SupportedOpcodes MUST be accepted by real ValidateAutoClosure.
        var autoSamples = new Dictionary<int, string>
        {
            [0x0000] = """{"label":"L0001","op":"end","mode":"keep","next":"L0002","source":{"index":1,"opcode":0,"operands":[0,0,0]}}""",
            [0x0001] = """{"label":"L0001","op":"end","mode":"advance","next":"L0002","source":{"index":1,"opcode":1,"operands":[0,0,0]}}""",
            [0x0002] = """{"label":"L0001","op":"end","mode":"redirect","to":"L0002","limit":0,"next":"L0002","source":{"index":1,"opcode":2,"operands":[2,0,0]}}""",
            [0x0003] = """{"label":"L0001","op":"goto","to":"L0002","limit":0,"next":"L0002","source":{"index":1,"opcode":3,"operands":[2,0,0]}}""",
            [0x0009] = """{"label":"L0001","op":"waitUpdate","frames":1,"runTriggers":0,"updateGestures":0,"next":"L0002","source":{"index":1,"opcode":9,"operands":[1,0,0]}}""",
            [0x000B] = """{"label":"L0001","op":"walkStep","direction":0,"next":"L0002","source":{"index":1,"opcode":11,"operands":[0,0,0]}}""",
            [0x000C] = """{"label":"L0001","op":"walkStep","direction":1,"next":"L0002","source":{"index":1,"opcode":12,"operands":[0,0,0]}}""",
            [0x000D] = """{"label":"L0001","op":"walkStep","direction":2,"next":"L0002","source":{"index":1,"opcode":13,"operands":[0,0,0]}}""",
            [0x000E] = """{"label":"L0001","op":"walkStep","direction":3,"next":"L0002","source":{"index":1,"opcode":14,"operands":[0,0,0]}}""",
            [0x000F] = """{"label":"L0001","op":"face","direction":0,"frame":0,"next":"L0002","source":{"index":1,"opcode":15,"operands":[0,0,0]}}""",
            [0x0010] = """{"label":"L0001","op":"walkTo","x":5,"y":5,"h":0,"next":"L0002","source":{"index":1,"opcode":16,"operands":[5,5,0]}}""",
            [0x0049] = """{"label":"L0001","op":"setState","eventObjectId":0,"state":1,"next":"L0002","source":{"index":1,"opcode":73,"operands":[0,1,0]}}""",
            [0x0052] = """{"label":"L0001","op":"hide","frames":0,"next":"L0002","source":{"index":1,"opcode":82,"operands":[0,0,0]}}"""
        };

        Assert.Equal(DynamicEventSession.SupportedOpcodes.Keys.Order().ToArray(), autoSamples.Keys.Order().ToArray());
        foreach (var (code, cmdJson) in autoSamples)
        {
            var events = BuildSingleCommandCollection(cmdJson, isAutoEntry: true);
            var session = CreateAutoSession(events);
            Assert.NotNull(session);
            Assert.Equal(code, DynamicEventSession.SupportedOpcodes[code].Opcode);
        }

        // 2. Non-auto commands MUST be rejected by real ValidateAutoClosure.
        string[] rejectedInAuto =
        [
            """{"label":"L0001","op":"showDialog","messageId":61,"descriptionLineWord":0,"next":"L0002","source":{"index":1,"opcode":65535,"operands":[61,0,0]}}""",
            """{"label":"L0001","op":"setMusic","musicId":1,"mode":0,"next":"L0002","source":{"index":1,"opcode":67,"operands":[1,0,0]}}""",
            """{"label":"L0001","op":"noop","next":"L0002","source":{"index":1,"opcode":120,"operands":[0,0,0]}}""",
            """{"label":"L0001","op":"markFailed","next":"L0002","source":{"index":1,"opcode":65,"operands":[0,0,0]}}""",
            """{"label":"L0001","op":"fadeToRed","next":"L0002","source":{"index":1,"opcode":79,"operands":[0,0,0]}}""",
            """{"label":"L0001","op":"requestReload","next":"L0002","source":{"index":1,"opcode":78,"operands":[0,0,0]}}"""
        ];
        foreach (string cmdJson in rejectedInAuto)
        {
            var events = BuildSingleCommandCollection(cmdJson, isAutoEntry: true);
            var ex = Assert.Throws<InvalidDataException>(() => CreateAutoSession(events));
            Assert.Contains("unsupported auto-script command", ex.Message);
        }
    }

    [Fact]
    public void TriggerScriptSession_SupportedOpcodes_matches_real_ValidateCapabilityClosure_bidirectionally()
    {
        var supported = TriggerScriptSession.SupportedOpcodes;
        Assert.Equal(54, supported.Count);
        Assert.DoesNotContain(0x000A, supported.Keys);

        foreach (var (code, cap) in supported)
        {
            Assert.Equal(code, cap.Opcode);
            Assert.False(string.IsNullOrWhiteSpace(cap.Verb));
            Assert.False(string.IsNullOrWhiteSpace(cap.FormatCell()));
        }

        // 1. Verify real TriggerScriptSession.Start (ValidateCapabilityClosure) accepts representative supported trigger commands.
        string[] acceptedTriggerCommands =
        [
            """{"label":"L0001","op":"end","mode":"keep","next":"L0002","source":{"index":1,"opcode":0,"operands":[0,0,0]}}""",
            """{"label":"L0001","op":"goto","to":"L0002","limit":0,"next":"L0002","source":{"index":1,"opcode":3,"operands":[2,0,0]}}""",
            """{"label":"L0001","op":"call","to":"L0002","eventObjectId":0,"next":"L0002","source":{"index":1,"opcode":4,"operands":[2,0,0]}}""",
            """{"label":"L0001","op":"advanceEntry","next":"L0002","source":{"index":1,"opcode":8,"operands":[0,0,0]}}""",
            """{"label":"L0001","op":"setMusic","musicId":1,"mode":0,"next":"L0002","source":{"index":1,"opcode":67,"operands":[1,0,0]}}""",
            """{"label":"L0001","op":"fadeToRed","next":"L0002","source":{"index":1,"opcode":79,"operands":[0,0,0]}}""",
            """{"label":"L0001","op":"requestReload","next":"L0002","source":{"index":1,"opcode":78,"operands":[0,0,0]}}""",
            """{"label":"L0001","op":"noop","next":"L0002","source":{"index":1,"opcode":120,"operands":[0,0,0]}}""",
            """{"label":"L0001","op":"markFailed","next":"L0002","source":{"index":1,"opcode":65,"operands":[0,0,0]}}""",
            """{"label":"L0001","op":"showDialog","messageId":61,"descriptionLineWord":0,"next":"L0002","source":{"index":1,"opcode":65535,"operands":[61,0,0]}}""",
            """{"label":"L0001","op":"setDialog","box":"upper","portraitId":0,"color":0,"rngWord":0,"next":"L0002","source":{"index":1,"opcode":60,"operands":[0,0,0]}}""",
            """{"label":"L0001","op":"walkStep","direction":0,"next":"L0002","source":{"index":1,"opcode":11,"operands":[0,0,0]}}""",
            """{"label":"L0001","op":"setState","eventObjectId":0,"state":1,"next":"L0002","source":{"index":1,"opcode":73,"operands":[0,1,0]}}""",
            """{"label":"L0001","op":"hide","frames":0,"next":"L0002","source":{"index":1,"opcode":82,"operands":[0,0,0]}}"""
        ];
        var lang = MinimalLanguage();
        var baseFixture = new DynamicEventSessionTests.DynamicFixture();
        foreach (string cmdJson in acceptedTriggerCommands)
        {
            var events = BuildSingleCommandCollection(cmdJson);
            var world = new DynamicEventSession(baseFixture.Rules(), DynamicEventSessionTests.DynamicFixture.Leader(),
                baseFixture.Load(), lang, baseFixture.Load().GetEntry("scene", "npc"), 1, 0, 0).World;
            var session = new TriggerScriptSession(events, lang, world);
            session.Start("shared", "testEntry", 5);
            Assert.NotEqual(Guid.Empty, session.CallId);
        }

        // 2. Verify real ValidateCapabilityClosure rejects an unsupported raw opcode before any state mutation.
        var unsupportedRaw = BuildSingleCommandCollection(
            """{"label":"L0001","op":"raw","next":"L0002","source":{"index":1,"opcode":139,"operands":[0,0,0]}}""");
        var badSession = new TriggerScriptSession(unsupportedRaw, lang);
        var ex = Assert.Throws<InvalidDataException>(() => badSession.Start("shared", "testEntry", 5));
        Assert.Contains("unsupported", ex.Message, StringComparison.OrdinalIgnoreCase);

        // 3. Verify exact verb set parity against SupportedOpcodes.
        var expectedVerbs = new HashSet<string>(StringComparer.Ordinal)
        {
            "end", "goto", "call", "redraw", "randomJump", "startBattle", "advanceEntry",
            "waitUpdate", "walkStep", "face", "walkTo", "setObjectPosition", "setObjectGesture",
            "setPartyFacing", "changeHp", "changeMp", "changeHpMp", "changeCash", "addItem",
            "removeItem", "revivePlayer", "setAutoEntry", "setTriggerEntry", "showBuyMenu",
            "showSellMenu", "boostStat", "setDialog", "setTriggerMode", "markFailed", "setMusic",
            "setPartyPos", "setState", "setBattlefield", "requestReload", "fadeToRed", "fadeOut",
            "hide", "itemShortageJump", "setSceneScripts", "fadeScene", "setParty", "noop",
            "halveCash", "setObjectScript", "setStateRange", "showDialog"
        };
        var declaredVerbs = supported.Values.Select(c => c.Verb).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(expectedVerbs, declaredVerbs);
    }
}
