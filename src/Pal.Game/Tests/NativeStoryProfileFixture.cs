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

    public NativeStoryProfileFixture()
    {
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(Root));
        var owner = Static.AddObject();
        owner.WTriggerScript = 1; owner.WTriggerMode = 1;
        Static.Scene.Palette = new PalPalette { Rgb = new byte[768] };
        Save(Static.Scene.Palette, "Palette.tres");
        Static.Scene.Palette = ResourceLoader.Load<PalPalette>(Root + "/Palette.tres", cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
        Static.Scene.Sprites[0].Frames[0].Texture = ResourceLoader.Load<Texture2D>(TextureSource);
        Static.SavePacked();
        Write("index.json", Index);
        Write("shared.json", Shared);
        Write("outcomes.json", Outcomes);
        Numeric = Numbers();
        Save(Numeric.Roles[0], "Role.tres");
        Numeric.Roles = [ResourceLoader.Load<PalBattleRole>(Root + "/Role.tres", cacheMode: ResourceLoader.CacheMode.IgnoreDeep)];
        Save(Numeric, "Numeric.tres");
        Font = new PalDialogueFont { SourceIdentity = "synthetic-story-font" };
        foreach (int codepoint in new[] { 65, 66, 67, 68, 0x7532, 0x4e59, 0x4e19, 0x4e01 })
            Font.Glyphs.Add(new PalDialogueGlyph { Codepoint = codepoint, Width = 8, Height = 15, RowBytes = 1, Bitmap = new byte[15] });
        Save(Font.Glyphs[0], "Glyph.tres");
        Font.Glyphs[0] = ResourceLoader.Load<PalDialogueGlyph>(Root + "/Glyph.tres", cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
        Save(Font, "Font.tres");
        Chinese = Language("zh-Hans", ["甲", "乙", "丙", "丁"]);
        English = Language("en", ["A", "B", "C", "D"]);
        Save(Chinese, "Language.zh-Hans.tres"); Save(English, "Language.en.tres");
        for (int i = 0; i < 3; i++) Save(Image("prompt-" + i), $"Prompt-{i:D2}.tres");
        foreach (int id in new[] { 7, 8, 9, 10 }) Save(Image("portrait-" + id), $"Portrait-{id}.tres");
        Profile = new PalStoryProfile
        {
            FormatVersion = 1, StaticResources = Static.ResourceRoot, StaticProfile = Static.Uri,
            BundleSha256 = new string('a', 64), EventsIndex = Root + "/index.json", EntryFileId = "shared", EntryId = "npc",
            OwnerId = 1, EntryAddress = 1, BattleContentUri = Root + "/Numeric.tres", DialogueDirectory = Root,
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
        for (int i = 0; i < 4; i++) language.Messages.Add(new PalDialogueMessageRange
        {
            StartMessageId = 61 + i, EndMessageId = 61 + i,
            Entries = [new PalDialogueMessageEntry { Text = texts[i] }]
        });
        return language;
    }
    private static PalBattleContent Numbers() => new()
    {
        FormatVersion = 1,
        Roles = [new PalBattleRole { RoleId = 0, Level = 2, Hp = 100, MaxHp = 150, Mp = 20, MaxMp = 50,
            PrimaryExperience = Experience(), HiddenExperience = new PalBattleHiddenExperience
            { Health = Experience(), Magic = Experience(), Attack = Experience(), MagicPower = Experience(), Defense = Experience(), Dexterity = Experience(), Flee = Experience() },
            Equipment = new int[6], Magics = new int[32], Statuses = new int[9] }],
        Players = [new PalBattlePlayer { RoleId = 0, Hp = 100, MaxHp = 150, Attack = 40, Defense = 20, Dexterity = 100,
            Equipment = new int[6], Statuses = new int[9], Poisons = [0] }],
        Growth = [new PalBattleGrowth { RoleId = 0, LevelUpExperience = Enumerable.Repeat(100, 100).ToArray(), LevelUpMagics = [] }],
        Teams = [new PalBattleTeam { TeamId = 18, Enemies = [new PalBattleEnemy { Id = 7, Hp = 43, PhysicalResistance = 1, Statuses = new int[9], Poisons = [0] }] }]
    };
    private static PalBattleExperience Experience() => new() { Value = 7, Level = 3, Count = 55 };

    public void Dispose()
    {
        Static.Dispose();
        Directory.Delete(ProjectSettings.GlobalizePath(Root), true);
    }

    private const string Index = """
    {"formatVersion":1,"profile":"faithful","source":{"archive":"SSS.MKF","chunk":4,"byteOrder":"little","instructionCount":13,"byteLength":104,"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},"files":["outcomes","shared"]}
    """;
    private const string Shared = """
    {"formatVersion":1,"profile":"faithful","fileId":"shared","entries":[{"id":"npc","owner":{"type":"eventObject","id":1},"slot":"wTriggerScript","kind":"trigger","target":"L0001","sourceAddress":1}],"commands":[
    {"label":"L0000","op":"raw","next":"L0001","source":{"index":0,"opcode":134,"operands":[0,65535,8]}},
    {"label":"L0001","op":"setDialog","box":"upper","portraitId":7,"color":0,"rngWord":0,"next":"L0002","source":{"index":1,"opcode":60,"operands":[99,0,0]}},
    {"label":"L0002","op":"showDialog","messageId":61,"descriptionLineWord":0,"next":"L0003","source":{"index":2,"opcode":65535,"operands":[61,0,0]}},
    {"label":"L0003","op":"startBattle","teamId":18,"lossTarget":"outcomes#L0007","isBoss":false,"fleeTarget":"outcomes#L000A","next":"L0004","source":{"index":3,"opcode":7,"operands":[99,7,10]}},
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
