using System;
using System.IO;
using System.Linq;
using Godot;
using Pal.Game.Resources;

namespace Pal.Game.Tests;

internal sealed class NpcInteractionFixture : IDisposable
{
    public NativeProfileFixture Static { get; } = new();
    public string Uri => Static.Root + "/Interaction.tres";
    public PalNpcInteractionProfile Profile { get; }
    public NpcInteractionFixture()
    {
        Static.ConfigureStaticScene4();
        var owner = Static.Scene.Objects.Single(o => o.Id == 60);
        owner.WTriggerMode = 3; owner.WTriggerScript = 0x0188; owner.NSpriteFrames = 3;
        foreach (var frame in Static.Scene.Map.Tiles.Frames.Concat(Static.Scene.Sprites.SelectMany(s => s.Frames)))
        {
            frame.Indices = Enumerable.Repeat((byte)0xF9, frame.Width * frame.Height).ToArray();
            frame.Alpha = Enumerable.Repeat((byte)255, frame.Width * frame.Height).ToArray();
        }
        Static.Scene.Palette = new PalPalette { PaletteId = 0, SelectedLighting = "day", Rgb = new byte[768] };
        for (int i = 0; i < 256; i++) Static.Scene.Palette.Rgb[i * 3] = (byte)i;
        Static.SavePacked();
        const string dialogue = "res://Tests/Fixtures/TriggerDialogue";
        Profile = new PalNpcInteractionProfile { FormatVersion = 1, StaticResources = Static.ResourceRoot,
            StaticProfile = Static.Uri, BundleSha256 = Static.Scene.BundleSha256,
            EventsIndex = dialogue + "/index.json", DialogueDirectory = dialogue };
        foreach (string path in new[] { Static.Uri, dialogue + "/index.json", dialogue + "/scene-004.json", dialogue + "/shared.json" }
            .Concat(new[] { "Font.tres", "Language.zh-Hans.tres", "Language.en.tres", "Portrait-059.tres", "Prompt-00.tres", "Prompt-01.tres", "Prompt-02.tres" }.Select(n => dialogue + "/" + n)))
            Profile.ResourceSha256.Add(path, Godot.FileAccess.GetSha256(path));
        Save();
    }
    public void CopyDialogue()
    {
        string target = Static.Root + "/Dialogue";
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(target));
        foreach (string source in Directory.GetFiles(ProjectSettings.GlobalizePath(Profile.DialogueDirectory)))
            File.Copy(source, ProjectSettings.GlobalizePath(target) + "/" + Path.GetFileName(source));
        Profile.DialogueDirectory = target;
        Profile.EventsIndex = target + "/index.json";
        Rehash();
    }
    public void Rehash()
    {
        Profile.ResourceSha256.Clear();
        using var index = System.Text.Json.JsonDocument.Parse(File.ReadAllText(ProjectSettings.GlobalizePath(Profile.EventsIndex)));
        var paths = index.RootElement.GetProperty("files").EnumerateArray().Select(i => Profile.DialogueDirectory + "/" + i.GetString() + ".json")
            .Concat(new[] { Static.Uri, Profile.EventsIndex })
            .Concat(new[] { "Font.tres", "Language.zh-Hans.tres", "Language.en.tres", "Portrait-059.tres", "Prompt-00.tres", "Prompt-01.tres", "Prompt-02.tres" }.Select(n => Profile.DialogueDirectory + "/" + n));
        foreach (string path in paths) Profile.ResourceSha256[path] = Godot.FileAccess.GetSha256(path);
        Save();
    }
    public void Save() => NativeProfileFixture.SaveResource(Profile, Uri);
    public void Dispose() => Static.Dispose();
}
