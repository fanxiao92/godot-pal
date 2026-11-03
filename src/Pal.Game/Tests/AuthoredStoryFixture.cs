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
    private string? numericInput;
    internal AuthoredStoryFixture(string scenario, string? missingPresentation = null, int? enemyHp = null)
    {
        string repository = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "../.."));
        string source = Path.Combine(repository, "samples/authored-battle", scenario);
        Directory.CreateDirectory(input);
        string output = ProjectSettings.GlobalizePath(Root);
        Directory.CreateDirectory(Path.Combine(output, "Static/Scenes"));
        try
        {
            File.Copy(ProjectSettings.GlobalizePath("res://Samples/AuthoredBattle/Static/Scenes/Scene.tscn"), Path.Combine(output, "Static/Scenes/Scene.tscn"));
            string numbers = Path.Combine(source, "Numbers");
            if (enemyHp is { } hp)
            {
                // Owned sibling, not an extra directory in strict Story input. Original author bytes stay read-only.
                numericInput = Path.Combine(Path.GetTempPath(), "pal-story-numeric-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(numericInput);
                foreach (string path in Directory.GetFiles(numbers)) File.Copy(path, Path.Combine(numericInput, Path.GetFileName(path)));
                string dataPath = Path.Combine(numericInput, "battle-data.json");
                var data = JsonNode.Parse(File.ReadAllText(dataPath, System.Text.Encoding.UTF8))!;
                var team = System.Linq.Enumerable.Single(data["teams"]!.AsArray(), t => (int)t!["teamId"]! == 18)!;
                var enemy = System.Linq.Enumerable.Single(team["enemies"]!.AsArray(), e => (int)e!["id"]! == 7)!;
                enemy["hp"] = hp;
                File.WriteAllText(dataPath, data.ToJsonString(), System.Text.Encoding.UTF8);
                numbers = numericInput;
            }
            string numeric = PrepareBattleData.Prepare(numbers, Path.Combine(output, "Numbers"));
            Directory.CreateDirectory(Path.Combine(input, "events"));
            foreach (string path in Directory.GetFiles(Path.Combine(source, "Story/events"))) File.Copy(path, Path.Combine(input, "events", Path.GetFileName(path)));
            var metadata = JsonNode.Parse(File.ReadAllText(Path.Combine(source, "Story/story-package.json")))!;
            if (missingPresentation == "effect")
            {
                metadata["visual"]!.AsObject().Remove("attackEffectSpriteUri");
                metadata["visual"]!.AsObject().Remove("attackEffectIndex");
            }
            if (missingPresentation == "number") metadata["visual"]!.AsObject().Remove("numberSpriteUri");
            if (missingPresentation == "idle") metadata["visual"]!["teams"]![0]!.AsObject().Remove("idleAnimSpeed");
            if (enemyHp is { } authoredHp)
                metadata["sourceIdentity"] = metadata["sourceIdentity"]!.GetValue<string>()
                    + $" Test-owned numeric copy: team18/enemy7 hp={authoredHp} only; not retail data.";
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
        if (numericInput is not null && Directory.Exists(numericInput)) Directory.Delete(numericInput, true);
        string output = ProjectSettings.GlobalizePath(Root);
        if (Directory.Exists(output)) Directory.Delete(output, true);
    }
}
