using System;
using System.IO;
using System.Linq;
using GdUnit4;
using Godot;
using Pal.Core;
using Pal.Game.Profiles;
using Pal.Game.Resources;
using System.Text.Json;
using System.Text.Json.Nodes;
using static GdUnit4.Assertions;

namespace Pal.Game.Tests;

[TestSuite, RequireGodotRuntime]
public class NativeBattleContentTests
{
    [TestCase(100)]
    [TestCase(20)]
    public void NumericContentSavesLoadsAndCompletesRealLifecycle(int hp)
    {
        var initial = Data();
        var content = new TriggerBattleContent(initial.InitialRole with { Hp = hp }, initial.Player with { Hp = hp }, initial.Growth,
            new System.Collections.Generic.Dictionary<int, ClassicBattleEnemy> { [18] = initial.Teams[18] with { Hp = hp == 20 ? 100 : 43 } });
        string root = "res://Tests/RuntimeBattleContent/" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(root));
        try
        {
            string uri = root + "/Battle.tres";
            AssertThat(ResourceSaver.Save(NativeBattleContent.Write(content), uri)).IsEqual(Error.Ok);
            var loaded = NativeBattleContent.Load(uri);
            var script = Script(loaded, new BattleRandom());
            script.Start("shared", "npc", 28);
            var request = script.PendingBattle!;
            var battle = request.Lifecycle;
            AssertThat(script.AcknowledgeBattle(request.Id, null)).IsFalse();
            battle.SelectAttack(7);
            AssertThat(battle.ResolveNextAction()).IsTrue();
            if (hp == 20) AssertThat(battle.ResolveNextAction()).IsTrue();
            AssertThat(battle.Completion!.Outcome).IsEqual(hp == 20 ? ClassicBattlePhase.Lost : ClassicBattlePhase.Won);
            AssertThat(battle.Current.Role.Hp).IsEqual(hp == 20 ? 0 : 125);
            AssertThat(battle.Current.Role.Mp).IsEqual(hp == 20 ? 20 : 35);
            AssertThat(battle.OwnsCompletion(battle.Completion)).IsTrue();
            AssertThat(battle.ResolveNextAction()).IsFalse();
            AssertThat(battle.Current.Role.HiddenExperience["attack"].Count).IsEqual(1);
            AssertThat(battle.Current.Role.HiddenExperience["health"].Count).IsEqual(2);
            AssertThat(script.AcknowledgeBattle(request.Id, battle.Completion)).IsTrue();
            AssertThat(script.Phase).IsEqual(TriggerScriptPhase.Completed);
            AssertThat(script.AcknowledgeBattle(request.Id, battle.Completion)).IsFalse();
        }
        finally { Directory.Delete(ProjectSettings.GlobalizePath(root), true); }
    }

    [TestCase]
    public void EveryTypedFieldSurvivesNativeSaveLoadAndMutableResourceIsolation()
    {
        var initial = Data();
        var role = initial.InitialRole with
        {
            Level = 65535, Hp = 0, MaxHp = 65535, Mp = 65535, MaxMp = 65535,
            Equipment = [1, 2, 3, 4, 5, 65535], Magics = Enumerable.Range(1, 32).ToArray(),
            Statuses = Enumerable.Range(1, 9).ToArray(), CoveredByRoleId = 5,
            PrimaryExperience = new(65535, 65534, 65533),
            HiddenExperience = new[] { "health", "magic", "attack", "magicPower", "defense", "dexterity", "flee" }
                .Select((name, index) => (name, index)).ToDictionary(x => x.name, x => new BattleExperienceSnapshot(x.index + 1, x.index + 11, x.index + 21), StringComparer.Ordinal)
        };
        var player = initial.Player with
        {
            Hp = 0, MaxHp = 65535, Attack = 65535, Defense = 123, Dexterity = 234,
            Equipment = [6, 5, 4, 3, 2, 1], Statuses = Enumerable.Range(10, 9).ToArray(), Poisons = [7, 65535],
            DyingScript = 12, FriendDeathScript = 13, AttackAll = true, CoveredByRoleId = 0
        };
        var enemy = initial.Teams[18] with
        {
            Id = 65535, Level = 65535, Hp = 65535, Attack = -32768, Defense = 32767, Dexterity = -123,
            PhysicalResistance = 65535, Statuses = Enumerable.Range(20, 9).ToArray(), Poisons = [9, 10],
            TurnStartScript = 11, ReadyScript = 12, BattleEndScript = 13, MagicId = 14, DualMove = 15,
            AttackEquivItem = 16, AttackEquivItemRate = 17, Experience = 18, Cash = 19
        };
        var content = new TriggerBattleContent(role, player,
            new(new int[100], [new(65535, 65535), new(0, 0), new(0, 0)]),
            new System.Collections.Generic.Dictionary<int, ClassicBattleEnemy> { [18] = enemy, [65535] = enemy with { Cash = 20 } });
        var native = NativeBattleContent.Write(content);
        AssertThat(native.Players[0].HasCoveredByRoleId).IsTrue();
        AssertThat(native.Players[0].CoveredByRoleId).IsEqual(0);
        AssertThat(JsonSerializer.Serialize(NativeBattleContent.CreateCore(native))).IsEqual(JsonSerializer.Serialize(content));
        AssertThat(content.Player.CoveredByRoleId!.Value).IsEqual(0);
        AssertThat(content.Teams[65535].Attack).IsEqual(-32768);
        AssertThat(content.Growth.LevelUpExperience[99]).IsEqual(0);
        AssertThat(content.Growth.LevelUpMagics.Count).IsEqual(3);
        string expected = JsonSerializer.Serialize(content);
        string root = "res://Tests/RuntimeBattleContent/" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(root));
        try
        {
            string uri = root + "/Battle.tres";
            var written = NativeBattleContent.Write(content);
            AssertThat(ResourceSaver.Save(written, uri)).IsEqual(Error.Ok);
            AssertThat(JsonSerializer.Serialize(NativeBattleContent.Load(uri))).IsEqual(expected);
            written.Roles[0].Equipment[0] = 99; written.Roles[0].HiddenExperience!.Attack!.Count = 99;
            native.Roles[0].Magics[0] = 99; native.Players[0].Poisons[0] = 99;
            native.Teams[0].Enemies[0].Cash = 99; native.Growth[0].LevelUpMagics[0].MagicId = 99;
            AssertThat(JsonSerializer.Serialize(content)).IsEqual(expected);
            AssertThat(JsonSerializer.Serialize(NativeBattleContent.Load(uri))).IsEqual(expected);
        }
        finally { Directory.Delete(ProjectSettings.GlobalizePath(root), true); }
    }

    [TestCase("version")]
    [TestCase("roles-null")]
    [TestCase("roles-empty")]
    [TestCase("roles-multiple")]
    [TestCase("role-null")]
    [TestCase("xp-null")]
    [TestCase("hidden-null")]
    [TestCase("hidden-xp-null")]
    [TestCase("equipment-null")]
    [TestCase("players-empty")]
    [TestCase("player-null")]
    [TestCase("player-hp")]
    [TestCase("player-role")]
    [TestCase("player-statuses")]
    [TestCase("player-poisons-null")]
    [TestCase("cover-conflict")]
    [TestCase("cover-range")]
    [TestCase("growth-null")]
    [TestCase("growth-role")]
    [TestCase("growth-threshold-null")]
    [TestCase("growth-threshold-shape")]
    [TestCase("growth-rows-null")]
    [TestCase("growth-row-null")]
    [TestCase("teams-empty")]
    [TestCase("team-null")]
    [TestCase("duplicate-team")]
    [TestCase("team-id")]
    [TestCase("enemies-empty")]
    [TestCase("enemies-multiple")]
    [TestCase("enemy-null")]
    [TestCase("enemy-statuses-null")]
    [TestCase("enemy-attack")]
    [TestCase("enemy-hp")]
    public void TypedNativeBoundaryRejectsMalformedFieldsBeforeLosingShapeOrIdentity(string defect)
    {
        var native = NativeBattleContent.Write(Data());
        switch (defect)
        {
            case "version": native.FormatVersion = 2; break;
            case "roles-null": native.Roles = null!; break;
            case "roles-empty": native.Roles = []; break;
            case "roles-multiple": native.Roles = [native.Roles[0], native.Roles[0]]; break;
            case "role-null": native.Roles = [null!]; break;
            case "xp-null": native.Roles[0].PrimaryExperience = null; break;
            case "hidden-null": native.Roles[0].HiddenExperience = null; break;
            case "hidden-xp-null": native.Roles[0].HiddenExperience!.Flee = null; break;
            case "equipment-null": native.Roles[0].Equipment = null!; break;
            case "players-empty": native.Players = []; break;
            case "player-null": native.Players = [null!]; break;
            case "player-hp": native.Players[0].Hp--; break;
            case "player-role": native.Players[0].RoleId = 0; break;
            case "player-statuses": native.Players[0].Statuses = new int[8]; break;
            case "player-poisons-null": native.Players[0].Poisons = null!; break;
            case "cover-conflict": native.Players[0].CoveredByRoleId = 1; break;
            case "cover-range": native.Players[0].HasCoveredByRoleId = true; native.Players[0].CoveredByRoleId = 6; break;
            case "growth-null": native.Growth = [null!]; break;
            case "growth-role": native.Growth[0].RoleId = 0; break;
            case "growth-threshold-null": native.Growth[0].LevelUpExperience = null!; break;
            case "growth-threshold-shape": native.Growth[0].LevelUpExperience = new int[101]; break;
            case "growth-rows-null": native.Growth[0].LevelUpMagics = null!; break;
            case "growth-row-null": native.Growth[0].LevelUpMagics = [null!]; break;
            case "teams-empty": native.Teams = []; break;
            case "team-null": native.Teams = [null!]; break;
            case "duplicate-team": native.Teams = [native.Teams[0], native.Teams[0]]; break;
            case "team-id": native.Teams[0].TeamId = -1; break;
            case "enemies-empty": native.Teams[0].Enemies = []; break;
            case "enemies-multiple": native.Teams[0].Enemies = [native.Teams[0].Enemies[0], native.Teams[0].Enemies[0]]; break;
            case "enemy-null": native.Teams[0].Enemies = [null!]; break;
            case "enemy-statuses-null": native.Teams[0].Enemies[0].Statuses = null!; break;
            case "enemy-attack": native.Teams[0].Enemies[0].Attack = 32768; break;
            case "enemy-hp": native.Teams[0].Enemies[0].Hp = 0; break;
        }
        InvalidDataException? error = null;
        try { NativeBattleContent.CreateCore(native, "selected-native.tres"); }
        catch (InvalidDataException caught) { error = caught; }
        AssertThat(error is not null).IsTrue();
        AssertThat(error!.Message.Contains("selected-native.tres", StringComparison.Ordinal)).IsTrue();
    }

    [TestCase("C:/Battle.tres")]
    [TestCase("res://Tests/../Battle.tres")]
    [TestCase("res://Tests/MissingBattleContent.tres")]
    public void LoadRejectsMissingOrIllegalUri(string uri)
    {
        bool rejected = false;
        try { NativeBattleContent.Load(uri); }
        catch (InvalidDataException error) { rejected = error.Message.Contains(uri, StringComparison.Ordinal); }
        AssertThat(rejected).IsTrue();
    }

    [TestCase("version")]
    [TestCase("type")]
    public void LoadRejectsWrongVersionOrResourceType(string defect)
    {
        string root = "res://Tests/RuntimeBattleContent/" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(root));
        try
        {
            var native = NativeBattleContent.Write(Data()); native.FormatVersion = 2;
            string uri = root + "/Bad.tres";
            AssertThat(ResourceSaver.Save(defect == "type" ? new PalBattleRole() : native, uri)).IsEqual(Error.Ok);
            bool rejected = false;
            try { NativeBattleContent.Load(uri); }
            catch (InvalidDataException error) { rejected = error.Message.Contains(defect == "type" ? "expected PalBattleContent" : "FormatVersion", StringComparison.Ordinal); }
            AssertThat(rejected).IsTrue();
        }
        finally { Directory.Delete(ProjectSettings.GlobalizePath(root), true); }
    }

    [TestCase]
    public void UnreferencedNonzeroTeamSurvivesButSelectedUnsupportedTeamRejectsBeforeRngOrRequest()
    {
        var native = NativeBattleContent.Write(Data());
        var extra = new PalBattleTeam { TeamId = 19, Enemies = [new() { Id = 7, Hp = 43, Statuses = new int[9], Cash = 1 }] };
        native.Teams = [native.Teams[0], extra];
        var random = new BattleRandom();
        var content = NativeBattleContent.CreateCore(native);
        AssertThat(content.Teams[19].Cash).IsEqual(1);
        var script = Script(content, random);
        script.Start("shared", "npc", 28);
        AssertThat(script.PendingBattle is not null).IsTrue();
        AssertThat(random.Draws).IsEqual(0);
        native.Teams[0].Enemies[0].Cash = 1;
        script = Script(NativeBattleContent.CreateCore(native), random);
        bool rejected = false;
        try { script.Start("shared", "npc", 28); }
        catch (InvalidDataException) { rejected = true; }
        AssertThat(rejected).IsTrue();
        AssertThat(script.Phase).IsEqual(TriggerScriptPhase.Ready);
        AssertThat(script.PendingBattle is null).IsTrue();
        AssertThat(script.Caller).IsEqual(0);
        AssertThat(random.Draws).IsEqual(0);
    }

    private static TriggerScriptSession Script(TriggerBattleContent content, BattleRandom random)
    {
        const string index = """
            {"formatVersion":1,"profile":"faithful","source":{"archive":"SSS.MKF","chunk":4,"byteOrder":"little","instructionCount":3,"byteLength":24,"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},"files":["shared"]}
            """;
        const string events = """
            {"formatVersion":1,"profile":"faithful","fileId":"shared","entries":[{"id":"npc","owner":{"type":"eventObject","id":28},"slot":"wTriggerScript","kind":"trigger","target":"L0001","sourceAddress":1}],"commands":[
            {"label":"L0000","op":"end","mode":"keep","next":"L0001","source":{"index":0,"opcode":0,"operands":[0,0,0]}},
            {"label":"L0001","op":"startBattle","teamId":18,"lossTarget":"L0002","isBoss":true,"fleeTarget":null,"next":"L0002","source":{"index":1,"opcode":7,"operands":[18,2,0]}},
            {"label":"L0002","op":"end","mode":"keep","next":null,"source":{"index":2,"opcode":0,"operands":[0,0,0]}}]}
            """;
        var collection = TriggerEventCollection.Parse(index, new System.Collections.Generic.Dictionary<string, string> { ["shared"] = events });
        return new(collection, new DialogueLanguagePackage("en", []), content, random);
    }

    private static TriggerBattleContent Data()
    {
        var data = JsonNode.Parse("""
        {"formatVersion":1,"roles":[{
          "roleId":1,"level":2,"hp":100,"maxHp":150,"mp":20,"maxMp":50,
          "primaryExperience":{"value":5,"level":2,"count":17},
          "hiddenExperience":{
            "health":{"value":7,"level":3,"count":55},"magic":{"value":7,"level":3,"count":55},
            "attack":{"value":7,"level":3,"count":55},"magicPower":{"value":7,"level":3,"count":55},
            "defense":{"value":7,"level":3,"count":55},"dexterity":{"value":7,"level":3,"count":55},
            "flee":{"value":7,"level":3,"count":55}},
          "equipment":[0,0,0,0,0,0],"magics":[],"statuses":[0,0,0,0,0,0,0,0,0],"coveredByRoleId":0}],
         "players":[{"roleId":1,"hp":100,"maxHp":150,"attack":40,"defense":20,"dexterity":100,
          "equipment":[0,0,0,0,0,0],"statuses":[0,0,0,0,0,0,0,0,0],"poisons":[0],
          "dyingScript":0,"friendDeathScript":0,"coveredByRoleId":null,"attackAll":false}],
         "growth":[{"roleId":1,"levelUpExperience":[],"levelUpMagics":[{"level":2,"magicId":9},{"level":3,"magicId":10}]}],
         "teams":[{"teamId":18,"enemies":[{"id":7,"level":0,"hp":43,"attack":0,"defense":0,"dexterity":0,"physicalResistance":1,
          "statuses":[0,0,0,0,0,0,0,0,0],"poisons":[0],"turnStartScript":0,"readyScript":0,"battleEndScript":0,
          "magicId":0,"dualMove":0,"attackEquivItem":0,"attackEquivItemRate":0,"experience":0,"cash":0}]}]}
        """)!.AsObject();
        data["roles"]![0]!["magics"] = new JsonArray(Enumerable.Range(0, 32).Select(i => JsonValue.Create(i == 0 ? 9 : 0) as JsonNode).ToArray());
        data["growth"]![0]!["levelUpExperience"] = new JsonArray(Enumerable.Repeat(100, 100).Select(x => JsonValue.Create(x) as JsonNode).ToArray());
        return ModernBattleContent.Parse(data.ToJsonString());
    }

    private sealed class BattleRandom : IBattleRandom
    {
        public int Draws { get; private set; }
        public int NextInclusive(int from, int to) { Draws++; return from == 0 && to == 5 ? 1 : from; }
        public float NextInclusive(float from, float to) { Draws++; return 1; }
    }
}
