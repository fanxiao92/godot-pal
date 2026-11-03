using System;
using System.IO;
using System.Text.Json.Nodes;
using Godot;
using Pal.Game.Editor;

namespace Pal.Game.Tests;

internal sealed class AuthoredStoryFixture : IDisposable
{
    private readonly string input = Path.Combine(Path.GetTempPath(), "pal-story-c-" + Guid.NewGuid().ToString("N"));
    internal string Root { get; } = "res://Assets/RuntimeTests/story-c-" + Guid.NewGuid().ToString("N");
    internal string StoryUri { get; }
    internal string VisualUri { get; }
    internal AuthoredStoryFixture(string scenario, string? missingPresentation = null)
    {
        string repository = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "../.."));
        string source = Path.Combine(repository, "samples/authored-battle", scenario);
        Directory.CreateDirectory(input);
        string output = ProjectSettings.GlobalizePath(Root);
        Directory.CreateDirectory(Path.Combine(output, "Static/Scenes"));
        try
        {
            File.Copy(ProjectSettings.GlobalizePath("res://Samples/AuthoredBattle/Static/Scenes/Scene.tscn"), Path.Combine(output, "Static/Scenes/Scene.tscn"));
            string numeric = PrepareBattleData.Prepare(Path.Combine(source, "Numbers"), Path.Combine(output, "Numbers"));
            Directory.CreateDirectory(Path.Combine(input, "events"));
            foreach (string path in Directory.GetFiles(Path.Combine(source, "Story/events"))) File.Copy(path, Path.Combine(input, "events", Path.GetFileName(path)));
            var metadata = JsonNode.Parse(File.ReadAllText(Path.Combine(source, "Story/story-package.json")))!;
            if (missingPresentation == "effect")
            {
                metadata["visual"]!.AsObject().Remove("attackEffectSpriteUri");
                metadata["visual"]!.AsObject().Remove("attackEffectIndex");
            }
            if (missingPresentation == "idle") metadata["visual"]!["teams"]![0]!.AsObject().Remove("idleAnimSpeed");
            metadata["staticResources"] = Root + "/Static";
            metadata["battleContentUri"] = numeric;
            File.WriteAllText(Path.Combine(input, "story-package.json"), metadata.ToJsonString());
            var prepared = PrepareStoryPackage.Prepare(input, Path.Combine(output, "Story"));
            StoryUri = prepared.StoryUri; VisualUri = prepared.VisualUri;
        }
        catch { Dispose(); throw; }
    }
    public void Dispose()
    {
        if (Directory.Exists(input)) Directory.Delete(input, true);
        string output = ProjectSettings.GlobalizePath(Root);
        if (Directory.Exists(output)) Directory.Delete(output, true);
    }
}
