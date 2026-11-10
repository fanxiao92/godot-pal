using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Pal.Game.Resources;

namespace Pal.Game.Tests;

// Literal authored content, not a second loader/walker/assembly path.
internal sealed class NativeStoryProfileFixture : IDisposable
{
    public const string TextureSource = "res://Tests/Fixtures/Story/Leader.svg";
    public NativeProfileFixture Static { get; } = new();
    public string Root { get; } = "res://Tests/RuntimeStory/" + Guid.NewGuid().ToString("N");
    public string Uri => Root + "/Story.tres";
    public PalStoryProfile Profile { get; }
    public PalBattleContent Numeric { get; }
    public PalDialogueFont Font { get; }
    public PalDialogueLanguage Chinese { get; }
    public PalDialogueLanguage English { get; }
    public string[] ExpectedPaths { get; }

    public bool Party { get; }
    public NativeStoryProfileFixture(bool party = false, bool hiddenCurrentGlyph = false, bool currentHookPositive = false,
        bool seedDyingGlyph = false, bool futureDyingWrite = false, bool secondOrderDyingWrite = false,
        bool randomDyingBranch = false, bool titleDyingGlyph = false, bool forceCurrentFriendDeath = false)
    {
        if (forceCurrentFriendDeath && (!currentHookPositive || !secondOrderDyingWrite || randomDyingBranch || titleDyingGlyph))
            throw new ArgumentException("The controlled FriendDeath variant requires only the second-order current-hook graph.", nameof(forceCurrentFriendDeath));
        if (titleDyingGlyph && (!futureDyingWrite || randomDyingBranch || secondOrderDyingWrite))
            throw new ArgumentException("The title variant requires only the future-write graph.", nameof(titleDyingGlyph));
        if (randomDyingBranch && (!futureDyingWrite || secondOrderDyingWrite))
            throw new ArgumentException("The random variant requires only the future-write graph.", nameof(randomDyingBranch));
        if (secondOrderDyingWrite && !futureDyingWrite)
            throw new ArgumentException("The second-order variant requires the future-write fixture.", nameof(secondOrderDyingWrite));
        if (seedDyingGlyph && (!currentHookPositive || hiddenCurrentGlyph))
            throw new ArgumentException("The seed glyph variant requires the clean current-hook positive fixture.", nameof(seedDyingGlyph));
        if (futureDyingWrite && (!currentHookPositive || hiddenCurrentGlyph || seedDyingGlyph))
            throw new ArgumentException("The future-write variant requires the clean, non-seed current-hook positive fixture.", nameof(futureDyingWrite));
        Party = party || currentHookPositive;
        bool currentHookGraph = hiddenCurrentGlyph || currentHookPositive;
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(Root));
        if (currentHookGraph) Static.ConfigureStaticScene4();
        var owner = currentHookGraph ? Static.Scene.Objects.Single(obj => obj.Id == 60) : Static.AddObject();
        owner.WTriggerScript = 1; owner.WTriggerMode = 1;
        Static.Scene.Palette = new PalPalette { Rgb = new byte[768] };
        Save(Static.Scene.Palette, "Palette.tres");
        Static.Scene.Palette = ResourceLoader.Load<PalPalette>(Root + "/Palette.tres", cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
        Static.Scene.Sprites[0].Frames[0].Texture = ResourceLoader.Load<Texture2D>(TextureSource);
        Static.SavePacked();
        Write("index.json", currentHookGraph ? Index.Replace("\"instructionCount\":13,\"byteLength\":104", "\"instructionCount\":64,\"byteLength\":512") : Index);
        Write("shared.json", currentHookGraph ? HiddenCurrentGlyphShared(seedDyingGlyph, futureDyingWrite, secondOrderDyingWrite, randomDyingBranch) : Shared);
        Write("outcomes.json", futureDyingWrite ? FutureOutcomes
            : currentHookGraph ? Outcomes.Replace("\"next\":null", "\"next\":\"shared#L000D\"") : Outcomes);
        Numeric = Numbers(Party);
        if (hiddenCurrentGlyph && !currentHookPositive)
        {
            Numeric.Players[0].HasNameObjectId = true;
            Numeric.Players[0].NameObjectId = 0;
            Numeric.Players[0].DyingScript = 40;
        }
        if (currentHookPositive)
        {
            // Literal two-role/one-enemy Dying control; persistent HP must match each player.
            Numeric.Roles[0].Hp = 100; Numeric.Roles[0].MaxHp = 200;
            Numeric.Roles[0].Mp = 20; Numeric.Roles[0].MaxMp = 50;
            Numeric.Roles[0].CoveredByRoleId = 2; Numeric.Roles[0].PanelDeclared = false;
            Numeric.Roles[1].Hp = 60; Numeric.Roles[1].MaxHp = 240;
            Numeric.Roles[1].Mp = 10; Numeric.Roles[1].MaxMp = 240;
            Numeric.Roles[1].CoveredByRoleId = 0; Numeric.Roles[1].PanelDeclared = false;
            Numeric.Players[0].Hp = 100; Numeric.Players[0].MaxHp = 200;
            Numeric.Players[0].Attack = 40; Numeric.Players[0].Defense = 20; Numeric.Players[0].Dexterity = 100;
            Numeric.Players[0].HasNameObjectId = true; Numeric.Players[0].NameObjectId = 37;
            Numeric.Players[0].DyingScript = 0; Numeric.Players[0].FriendDeathScript = 0;
            Numeric.Players[0].HasCoveredByRoleId = true; Numeric.Players[0].CoveredByRoleId = 2;
            Numeric.Players[1].Hp = 60; Numeric.Players[1].MaxHp = 240;
            Numeric.Players[1].Attack = 20; Numeric.Players[1].Defense = 0; Numeric.Players[1].Dexterity = 100;
            Numeric.Players[1].HasNameObjectId = true; Numeric.Players[1].NameObjectId = 0;
            Numeric.Players[1].DyingScript = 40; Numeric.Players[1].FriendDeathScript = 0;
            Numeric.Players[1].HasCoveredByRoleId = true; Numeric.Players[1].CoveredByRoleId = 0;
            Numeric.Teams[0].Enemies = forceCurrentFriendDeath ? [Enemy(), Enemy()] : [Enemy()];
            foreach (var enemy in Numeric.Teams[0].Enemies)
            {
                // Optional two real hits: 60 -> 16 (Dying), then 16 -> 0 (coverer's FriendDeath).
                enemy.Attack = 8; enemy.Dexterity = 500;
            }
        }
        Save(Numeric.Roles[0], "Role.tres");
        Numeric.Roles = [ResourceLoader.Load<PalBattleRole>(Root + "/Role.tres", cacheMode: ResourceLoader.CacheMode.IgnoreDeep), .. Numeric.Roles[1..]];
        Save(Numeric, "Numeric.tres");
        Font = new PalDialogueFont { SourceIdentity = "synthetic-story-font" };
        foreach (int codepoint in new[] { 65, 66, 67, 68, 0x7532, 0x4e59, 0x4e19, 0x4e01 })
            Font.Glyphs.Add(new PalDialogueGlyph { Codepoint = codepoint, Width = 8, Height = 15, RowBytes = 1, Bitmap = new byte[15] });
        if (seedDyingGlyph || futureDyingWrite)
            Font.Glyphs.Add(new PalDialogueGlyph { Codepoint = 69, Width = 8, Height = 15, RowBytes = 1, Bitmap = new byte[15] });
        if (secondOrderDyingWrite)
            Font.Glyphs.Add(new PalDialogueGlyph { Codepoint = 70, Width = 8, Height = 15, RowBytes = 1, Bitmap = new byte[15] });
        if (randomDyingBranch || titleDyingGlyph)
            Font.Glyphs.Add(new PalDialogueGlyph { Codepoint = 71, Width = 8, Height = 15, RowBytes = 1, Bitmap = new byte[15] });
        if (titleDyingGlyph)
            Font.Glyphs.Add(new PalDialogueGlyph { Codepoint = 58, Width = 8, Height = 15, RowBytes = 1, Bitmap = new byte[15] });
        Save(Font.Glyphs[0], "Glyph.tres");
        Font.Glyphs[0] = ResourceLoader.Load<PalDialogueGlyph>(Root + "/Glyph.tres", cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
        Save(Font, "Font.tres");
        Chinese = Language("zh-Hans", randomDyingBranch ? ["甲", "乙", "丙", "丁", "乙", "乙", "乙"]
            : secondOrderDyingWrite ? ["甲", "乙", "丙", "丁", "乙", "乙", "乙"]
            : futureDyingWrite ? ["甲", "乙", "丙", "丁", "乙", "乙"]
            : currentHookPositive ? ["甲", "乙", "丙", "丁", "乙"]
            : hiddenCurrentGlyph ? ["甲", "乙", "丙", "丁", "戊"] : ["甲", "乙", "丙", "丁"]);
        English = Language("en", randomDyingBranch ? ["A", "B", "C", "D", "B", "E", "G"]
            : secondOrderDyingWrite ? ["A", "B", "C", "D", "B", "E", "F"]
            : futureDyingWrite ? ["A", "B", "C", "D", "B", "E"]
            : currentHookPositive ? ["A", "B", "C", "D", seedDyingGlyph ? "E" : "B"]
            : hiddenCurrentGlyph ? ["A", "B", "C", "D", "E"] : ["A", "B", "C", "D"]);
        if (titleDyingGlyph)
            foreach (var language in new[] { Chinese, English })
                language.Messages.Single(message => message.StartMessageId == 66).Entries.Insert(0,
                    new PalDialogueMessageEntry { Text = "G:" });
        Save(Chinese, "Language.zh-Hans.tres"); Save(English, "Language.en.tres");
        for (int i = 0; i < 3; i++) Save(Image("prompt-" + i), $"Prompt-{i:D2}.tres");
        foreach (int id in new[] { 7, 8, 9, 10 }) Save(Image("portrait-" + id), $"Portrait-{id}.tres");
        Profile = new PalStoryProfile
        {
            FormatVersion = 1, StaticResources = Static.ResourceRoot, StaticProfile = Static.Uri,
            BundleSha256 = new string('a', 64), EventsIndex = Root + "/index.json", EntryFileId = "shared", EntryId = "npc",
            OwnerId = currentHookGraph ? 60 : 1, EntryAddress = 1, BattleContentUri = Root + "/Numeric.tres", DialogueDirectory = Root,
            SourceIdentity = "synthetic authored content, not source authentication",
            Portraits = new Godot.Collections.Dictionary<int, string>
            { [7] = Root + "/Portrait-7.tres", [8] = Root + "/Portrait-8.tres", [9] = Root + "/Portrait-9.tres", [10] = Root + "/Portrait-10.tres" }
        };
        // Independent literal manifest; never use production dependency enumeration to author expectations.
        ExpectedPaths = new[] { Static.ResourceRoot + "/Scenes/Scene.tscn", Static.Uri, TextureSource, TextureSource + ".import" }
            .Concat(new[] { "index.json", "shared.json", "outcomes.json", "Numeric.tres", "Role.tres", "Palette.tres", "Font.tres", "Glyph.tres",
                "Language.zh-Hans.tres", "Language.en.tres", "Prompt-00.tres", "Prompt-01.tres", "Prompt-02.tres",
                "Portrait-7.tres", "Portrait-8.tres", "Portrait-9.tres", "Portrait-10.tres" }.Select(name => Root + "/" + name)).ToArray();
        RefreshHashes(); SaveProfile();
    }

    public void Save(Resource resource, string name) => NativeProfileFixture.SaveResource(resource, Root + "/" + name);
    public void SaveProfile() => NativeProfileFixture.SaveResource(Profile, Uri);
    public void Write(string name, string text) => File.WriteAllText(ProjectSettings.GlobalizePath(Root + "/" + name), text);
    public string Read(string name) => File.ReadAllText(ProjectSettings.GlobalizePath(Root + "/" + name));
    public void Edit(string name, string before, string after)
    {
        string text = Read(name);
        if (!text.Contains(before, StringComparison.Ordinal)) throw new InvalidOperationException("Missing literal fixture edit: " + before);
        Write(name, text.Replace(before, after, StringComparison.Ordinal));
    }
    public void RefreshHashes()
    {
        Profile.ResourceSha256.Clear();
        foreach (string path in ExpectedPaths) Profile.ResourceSha256.Add(path, Godot.FileAccess.GetSha256(path));
    }
    private static PalDialogueIndexedImage Image(string identity) => new()
        { SourceIdentity = identity, Width = 1, Height = 1, PaletteIndices = [4], Alpha = [255] };
    private static PalDialogueLanguage Language(string id, string[] texts)
    {
        var language = new PalDialogueLanguage { Language = id, SourceIdentity = "synthetic-" + id };
        for (int i = 0; i < texts.Length; i++) language.Messages.Add(new PalDialogueMessageRange
        {
            StartMessageId = 61 + i, EndMessageId = 61 + i,
            Entries = [new PalDialogueMessageEntry { Text = texts[i] }]
        });
        return language;
    }
    private static PalBattleContent Numbers(bool party) => new()
    {
        FormatVersion = 1,
        Roles = party
            ? [Role(0, 100), Role(1, 95)]
            : [Role(0, 100)],
        Players = party
            ? [Player(0, hp: 100, attack: 40, defense: 20), Player(1, hp: 95, attack: 35, defense: 18)]
            : [Player(0, hp: 100, attack: 40, defense: 20)],
        Growth = party
            ? [Growth(0), Growth(1)]
            : [Growth(0)],
        Teams = [new PalBattleTeam { TeamId = 18, Enemies = party
            ? [Enemy(), Enemy()]
            : [Enemy()] }]
    };
    private static PalBattleRole Role(int roleId, int hp) => new() { RoleId = roleId, Level = 2, Hp = hp, MaxHp = 150, Mp = 20, MaxMp = 50,
        PrimaryExperience = Experience(), HiddenExperience = new PalBattleHiddenExperience
        { Health = Experience(), Magic = Experience(), Attack = Experience(), MagicPower = Experience(), Defense = Experience(), Dexterity = Experience(), Flee = Experience() },
        Equipment = new int[6], Magics = new int[32], Statuses = new int[9] };
    private static PalBattlePlayer Player(int roleId, int hp, int attack, int defense) => new() { RoleId = roleId, Hp = hp, MaxHp = 150,
        Attack = attack, Defense = defense, Dexterity = 100, Equipment = new int[6], Statuses = new int[9], Poisons = [0] };
    private static PalBattleGrowth Growth(int roleId) => new() { RoleId = roleId, LevelUpExperience = Enumerable.Repeat(100, 100).ToArray(), LevelUpMagics = [] };
    private static PalBattleEnemy Enemy() => new() { Id = 7, Hp = 43, PhysicalResistance = 1, Statuses = new int[9], Poisons = [0] };
    private static PalBattleExperience Experience() => new() { Value = 7, Level = 3, Count = 55 };

    public void Dispose()
    {
        Static.Dispose();
        Directory.Delete(ProjectSettings.GlobalizePath(Root), true);
    }

    // Independent WORD layout: default seed40 keeps; the opt-in seed40 displays E then keeps42.
    // Default live20 remains reachable only after a real public 0090.
    private static string HiddenCurrentGlyphShared(bool seedDyingGlyph, bool futureDyingWrite, bool secondOrderDyingWrite,
        bool randomDyingBranch)
    {
        const string entries = """
        {"id":"seed","owner":{"type":"object","id":0},"slot":"player.wScriptOnDying","kind":"trigger","target":"L0028","sourceAddress":40},
        {"id":"setter","owner":{"type":"object","id":0},"slot":"item.wScriptOnUse","kind":"trigger","target":"L0032","sourceAddress":50}
        """;
        string shared = (futureDyingWrite ? FutureShared : Shared)
            .Replace("\"type\":\"eventObject\",\"id\":1", "\"type\":\"eventObject\",\"id\":60")
            .Replace("],\"commands\":[", "," + entries + "],\"commands\":[");
        var commands = new List<string>();
        for (int address = futureDyingWrite ? 8 : 13; address < 64; address++)
        {
            // Only the opt-in graph moves loss/flee to13..18; every WORD still occurs exactly once.
            if (futureDyingWrite && address is >= 13 and <= 18) continue;
            string next = futureDyingWrite && address == 12 ? "\"outcomes#L000D\""
                : address == 63 ? "null" : "\"L" + (address + 1).ToString("X4") + "\"";
            string command = address switch
            {
                20 => "\"op\":\"setDialog\",\"box\":\"upper\",\"portraitId\":7,\"color\":0,\"rngWord\":0",
                21 => "\"op\":\"showDialog\",\"messageId\":65,\"descriptionLineWord\":0",
                24 when randomDyingBranch => "\"op\":\"randomJump\",\"rate\":50,\"to\":\"L0020\"",
                25 when randomDyingBranch => "\"op\":\"setDialog\",\"box\":\"upper\",\"portraitId\":7,\"color\":0,\"rngWord\":0",
                26 when randomDyingBranch => "\"op\":\"showDialog\",\"messageId\":66,\"descriptionLineWord\":0",
                27 or 34 when randomDyingBranch => "\"op\":\"end\",\"mode\":\"keep\"",
                32 when randomDyingBranch => "\"op\":\"setDialog\",\"box\":\"upper\",\"portraitId\":7,\"color\":0,\"rngWord\":0",
                33 when randomDyingBranch => "\"op\":\"showDialog\",\"messageId\":67,\"descriptionLineWord\":0",
                24 when futureDyingWrite => "\"op\":\"setDialog\",\"box\":\"upper\",\"portraitId\":7,\"color\":0,\"rngWord\":0",
                25 when futureDyingWrite => "\"op\":\"showDialog\",\"messageId\":66,\"descriptionLineWord\":0",
                // Write another Name/slot: completing Y still writes back its own Name0/Dying24.
                26 when secondOrderDyingWrite => "\"op\":\"setObjectScript\",\"objectId\":37,\"value\":32,\"slotIndex\":0",
                26 when futureDyingWrite => "\"op\":\"end\",\"mode\":\"keep\"",
                27 or 34 when secondOrderDyingWrite => "\"op\":\"end\",\"mode\":\"keep\"",
                32 when secondOrderDyingWrite => "\"op\":\"setDialog\",\"box\":\"upper\",\"portraitId\":7,\"color\":0,\"rngWord\":0",
                33 when secondOrderDyingWrite => "\"op\":\"showDialog\",\"messageId\":67,\"descriptionLineWord\":0",
                40 when seedDyingGlyph => "\"op\":\"setDialog\",\"box\":\"upper\",\"portraitId\":7,\"color\":0,\"rngWord\":0",
                41 when seedDyingGlyph => "\"op\":\"showDialog\",\"messageId\":65,\"descriptionLineWord\":0",
                42 when seedDyingGlyph => "\"op\":\"end\",\"mode\":\"keep\"",
                22 or 40 or 51 => "\"op\":\"end\",\"mode\":\"keep\"",
                50 => "\"op\":\"setObjectScript\",\"objectId\":0,\"value\":20,\"slotIndex\":1",
                _ => "\"op\":\"raw\""
            };
            string source = address switch
            {
                20 => "\"opcode\":60,\"operands\":[7,0,0]",
                21 => "\"opcode\":65535,\"operands\":[65,0,0]",
                24 when randomDyingBranch => "\"opcode\":6,\"operands\":[50,32,0]",
                25 or 32 when randomDyingBranch => "\"opcode\":60,\"operands\":[7,0,0]",
                26 when randomDyingBranch => "\"opcode\":65535,\"operands\":[66,0,0]",
                27 or 34 when randomDyingBranch => "\"opcode\":0,\"operands\":[0,0,0]",
                33 when randomDyingBranch => "\"opcode\":65535,\"operands\":[67,0,0]",
                24 when futureDyingWrite => "\"opcode\":60,\"operands\":[7,0,0]",
                25 when futureDyingWrite => "\"opcode\":65535,\"operands\":[66,0,0]",
                26 when secondOrderDyingWrite => "\"opcode\":144,\"operands\":[37,32,0]",
                26 when futureDyingWrite => "\"opcode\":0,\"operands\":[0,0,0]",
                27 or 34 when secondOrderDyingWrite => "\"opcode\":0,\"operands\":[0,0,0]",
                32 when secondOrderDyingWrite => "\"opcode\":60,\"operands\":[7,0,0]",
                33 when secondOrderDyingWrite => "\"opcode\":65535,\"operands\":[67,0,0]",
                40 when seedDyingGlyph => "\"opcode\":60,\"operands\":[7,0,0]",
                41 when seedDyingGlyph => "\"opcode\":65535,\"operands\":[65,0,0]",
                42 when seedDyingGlyph => "\"opcode\":0,\"operands\":[0,0,0]",
                22 or 40 or 51 => "\"opcode\":0,\"operands\":[0,0,0]",
                50 => "\"opcode\":144,\"operands\":[0,20,1]",
                _ => "\"opcode\":134,\"operands\":[0,65535,8]"
            };
            commands.Add("{\"label\":\"L" + address.ToString("X4") + "\"," + command
                + ",\"next\":" + next + ",\"source\":{\"index\":" + address + "," + source + "}}");
        }
        return shared[..^2] + "," + string.Join(",", commands) + "]}";
    }

    // A fully linear opt-in prefix: real0090 follows the prelude ACK; no unsupported goto.
    private const string FutureShared = """
    {"formatVersion":1,"profile":"faithful","fileId":"shared","entries":[{"id":"npc","owner":{"type":"eventObject","id":1},"slot":"wTriggerScript","kind":"trigger","target":"L0001","sourceAddress":1}],"commands":[
    {"label":"L0000","op":"raw","next":"L0001","source":{"index":0,"opcode":134,"operands":[0,65535,8]}},
    {"label":"L0001","op":"setDialog","box":"upper","portraitId":7,"color":0,"rngWord":0,"next":"L0002","source":{"index":1,"opcode":60,"operands":[7,0,0]}},
    {"label":"L0002","op":"showDialog","messageId":61,"descriptionLineWord":0,"next":"L0003","source":{"index":2,"opcode":65535,"operands":[61,0,0]}},
    {"label":"L0003","op":"setObjectScript","objectId":0,"value":24,"slotIndex":1,"next":"L0004","source":{"index":3,"opcode":144,"operands":[0,24,1]}},
    {"label":"L0004","op":"startBattle","enemyTeamId":18,"lossTarget":"outcomes#L000D","isBoss":false,"fleeTarget":"outcomes#L0010","next":"L0005","source":{"index":4,"opcode":7,"operands":[18,13,16]}},
    {"label":"L0005","op":"setDialog","box":"upper","portraitId":8,"color":0,"rngWord":0,"next":"L0006","source":{"index":5,"opcode":60,"operands":[8,0,0]}},
    {"label":"L0006","op":"showDialog","messageId":62,"descriptionLineWord":0,"next":"L0007","source":{"index":6,"opcode":65535,"operands":[62,0,0]}},
    {"label":"L0007","op":"end","mode":"keep","next":"L0008","source":{"index":7,"opcode":0,"operands":[0,0,0]}}]}
    """;
    private const string FutureOutcomes = """
    {"formatVersion":1,"profile":"faithful","fileId":"outcomes","entries":[],"commands":[
    {"label":"L000D","op":"setDialog","box":"upper","portraitId":9,"color":0,"rngWord":0,"next":"L000E","source":{"index":13,"opcode":60,"operands":[9,0,0]}},
    {"label":"L000E","op":"showDialog","messageId":63,"descriptionLineWord":0,"next":"L000F","source":{"index":14,"opcode":65535,"operands":[63,0,0]}},
    {"label":"L000F","op":"end","mode":"keep","next":"L0010","source":{"index":15,"opcode":0,"operands":[0,0,0]}},
    {"label":"L0010","op":"setDialog","box":"upper","portraitId":10,"color":0,"rngWord":0,"next":"L0011","source":{"index":16,"opcode":60,"operands":[10,0,0]}},
    {"label":"L0011","op":"showDialog","messageId":64,"descriptionLineWord":0,"next":"L0012","source":{"index":17,"opcode":65535,"operands":[64,0,0]}},
    {"label":"L0012","op":"end","mode":"keep","next":"shared#L0013","source":{"index":18,"opcode":0,"operands":[0,0,0]}}]}
    """;

    private const string Index = """
    {"formatVersion":1,"profile":"faithful","source":{"archive":"SSS.MKF","chunk":4,"byteOrder":"little","instructionCount":13,"byteLength":104,"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},"files":["outcomes","shared"]}
    """;
    private const string Shared = """
    {"formatVersion":1,"profile":"faithful","fileId":"shared","entries":[{"id":"npc","owner":{"type":"eventObject","id":1},"slot":"wTriggerScript","kind":"trigger","target":"L0001","sourceAddress":1}],"commands":[
    {"label":"L0000","op":"raw","next":"L0001","source":{"index":0,"opcode":134,"operands":[0,65535,8]}},
    {"label":"L0001","op":"setDialog","box":"upper","portraitId":7,"color":0,"rngWord":0,"next":"L0002","source":{"index":1,"opcode":60,"operands":[99,0,0]}},
    {"label":"L0002","op":"showDialog","messageId":61,"descriptionLineWord":0,"next":"L0003","source":{"index":2,"opcode":65535,"operands":[61,0,0]}},
    {"label":"L0003","op":"startBattle","enemyTeamId":18,"lossTarget":"outcomes#L0007","isBoss":false,"fleeTarget":"outcomes#L000A","next":"L0004","source":{"index":3,"opcode":7,"operands":[99,7,10]}},
    {"label":"L0004","op":"setDialog","box":"upper","portraitId":8,"color":0,"rngWord":0,"next":"L0005","source":{"index":4,"opcode":60,"operands":[8,0,0]}},
    {"label":"L0005","op":"showDialog","messageId":62,"descriptionLineWord":0,"next":"L0006","source":{"index":5,"opcode":65535,"operands":[62,0,0]}},
    {"label":"L0006","op":"end","mode":"keep","next":"outcomes#L0007","source":{"index":6,"opcode":0,"operands":[0,0,0]}}]}
    """;
    private const string Outcomes = """
    {"formatVersion":1,"profile":"faithful","fileId":"outcomes","entries":[],"commands":[
    {"label":"L0007","op":"setDialog","box":"upper","portraitId":9,"color":0,"rngWord":0,"next":"L0008","source":{"index":7,"opcode":60,"operands":[9,0,0]}},
    {"label":"L0008","op":"showDialog","messageId":63,"descriptionLineWord":0,"next":"L0009","source":{"index":8,"opcode":65535,"operands":[63,0,0]}},
    {"label":"L0009","op":"end","mode":"keep","next":"L000A","source":{"index":9,"opcode":0,"operands":[0,0,0]}},
    {"label":"L000A","op":"setDialog","box":"upper","portraitId":10,"color":0,"rngWord":0,"next":"L000B","source":{"index":10,"opcode":60,"operands":[10,0,0]}},
    {"label":"L000B","op":"showDialog","messageId":64,"descriptionLineWord":0,"next":"L000C","source":{"index":11,"opcode":65535,"operands":[64,0,0]}},
    {"label":"L000C","op":"end","mode":"keep","next":null,"source":{"index":12,"opcode":0,"operands":[0,0,0]}}]}
    """;
}
