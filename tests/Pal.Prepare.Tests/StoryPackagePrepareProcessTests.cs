using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pal.TestFixtures;
using Xunit;

namespace Pal.Prepare.Tests;

// Process contracts only. Literal native test leaves are not a desktop content package.
public sealed class StoryPackagePrepareProcessTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "pal-story-process-" + Guid.NewGuid().ToString("N"));
    private readonly string repo;
    private readonly string native;
    private readonly string output;
    private string Input => Path.Combine(root, "story");
    private string Project => Path.Combine(repo, "src", "Pal.Game");
    private static string Godot => Environment.GetEnvironmentVariable("PAL_TEST_GODOT") ?? throw new InvalidOperationException("Set PAL_TEST_GODOT.");
    private const string Marker = "PAL_PREPARE_STORY_PACKAGE:";
    public StoryPackagePrepareProcessTests()
    {
        repo = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(repo, "GodotPal.sln"))) repo = Directory.GetParent(repo)!.FullName;
        native = Path.Combine(Project, "Assets", "PrepareTests", Guid.NewGuid().ToString("N"));
        output = native + "-package";
        Directory.CreateDirectory(Input);
    }

    [Fact]
    public async Task Normal_wrapper_and_editor_publish_strict_packages_and_never_overwrite()
    {
        await WriteInput();
        string[] before = Snapshot(Input);
        Directory.CreateDirectory(output);
        var wrapper = await Wrapper("-InputPath", Input, "-OutputPath", output, "-GodotPath", Godot);
        Assert.True(wrapper.ExitCode == 0, wrapper.Text);
        Assert.Contains(Marker + Uri(output) + "/Story.tres;" + Uri(output) + "/Visual.tres", wrapper.Text);
        Assert.Equal(new[] { "Events", "Story.tres", "Visual.tres" }, Directory.GetFileSystemEntries(output).Select(Path.GetFileName).Order().ToArray());
        string[] published = Snapshot(output);
        var collision = await Wrapper("-InputPath", Input, "-OutputPath", output, "-GodotPath", Godot);
        Assert.Equal(1, collision.ExitCode); Assert.Contains("new or empty", collision.Text);
        Assert.DoesNotContain(Marker, collision.Text); Assert.Equal(published, Snapshot(output));
        var editor = await Editor("--story-input", Input, "--story-output", output + "-editor");
        Assert.True(editor.ExitCode == 0, editor.Text); Assert.Contains(Marker, editor.Text);
        Assert.Equal(before, Snapshot(Input));
        // The producer itself strictly reloads both final typed roots, including both story languages.
        Assert.Equal(File.ReadAllBytes(Path.Combine(Input, "events", "story.json")), File.ReadAllBytes(Path.Combine(output, "Events", "story.json")));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("reordered")]
    [InlineData("unknown")]
    public async Task Real_editor_rejects_invalid_argv_without_success_or_output(string defect)
    {
        string[] args = defect switch
        {
            "missing" => ["--story-input", Input],
            "extra" => ["--story-input", Input, "--story-output", output, "--extra", "x"],
            "reordered" => ["--story-output", output, "--story-input", Input],
            _ => ["--unknown", Input, "--story-output", output]
        };
        var result = await Editor(args);
        Assert.Equal(1, result.ExitCode); Assert.Contains("invalid story-package prepare arguments", result.Text);
        Assert.DoesNotContain(Marker, result.Text); Assert.False(Directory.Exists(output));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    [InlineData("timeout")]
    public async Task Wrapper_rejects_invalid_arguments_before_tool_resolution(string defect)
    {
        string[] args = defect switch
        {
            "missing" => ["-InputPath", Input],
            "duplicate" => ["-InputPath", Input, "-InputPath", Input, "-OutputPath", output],
            "unknown" => ["-InputPath", Input, "-OutputPath", output, "-Unknown", "x"],
            _ => ["-InputPath", Input, "-OutputPath", output, "-TimeoutSeconds", "601"]
        };
        var result = await Wrapper(args);
        Assert.Equal(2, result.ExitCode); Assert.Contains("Prepare-StoryPackage argument error:", result.Text);
        Assert.DoesNotContain(Marker, result.Text); Assert.False(Directory.Exists(output));
    }

    [Fact]
    public async Task Editor_propagates_invalid_metadata_and_preserves_caller_empty_directory()
    {
        Directory.CreateDirectory(Path.Combine(Input, "events"));
        File.WriteAllText(Path.Combine(Input, "story-package.json"), "{}");
        File.WriteAllText(Path.Combine(Input, "events", "index.json"), "{}");
        File.WriteAllText(Path.Combine(Input, "events", "story.json"), "{}");
        Directory.CreateDirectory(output); string[] before = Snapshot(Input);
        var result = await Editor("--story-input", Input, "--story-output", output);
        Assert.Equal(1, result.ExitCode); Assert.Contains("metadata has missing, duplicate or unknown fields", result.Text);
        Assert.DoesNotContain(Marker, result.Text); Assert.Equal(before, Snapshot(Input));
        Assert.True(Directory.Exists(output)); Assert.Empty(Directory.GetFileSystemEntries(output));
    }

    private async Task WriteInput()
    {
        string staticInput = Path.Combine(root, "static"), staticOutput = Path.Combine(native, "Static");
        M1Bundle.Write(staticInput);
        M1Bundle.Mutate(staticInput, "scene.json", node => node["objects"]![0]!["wTriggerScript"] = 1);
        var prepared = await PrepareProcessTests.Run("pwsh", "-NoProfile", "-File", Path.Combine(repo, "scripts", "Prepare-GameScene.ps1"),
            "-InputPath", staticInput, "-OutputPath", staticOutput, "-GodotPath", Godot);
        Assert.True(prepared.ExitCode == 0, prepared.Text);
        var profile = await PrepareProcessTests.Run(Godot, "--headless", "--path", Project, "--script", Path.Combine(repo, "tests", "Fixtures", "WriteStartupProfile.gd"), "--", "--native-root", Uri(staticOutput));
        Assert.True(profile.ExitCode == 0, profile.Text);
        string numbers = Path.Combine(root, "numbers"); Directory.CreateDirectory(numbers);
        var numeric = JsonNode.Parse(File.ReadAllText(Path.Combine(repo, "tests", "Fixtures", "ModernBattleData", "battle-data.json")))!;
        foreach (string key in new[] { "roles", "players", "growth" }) numeric[key]![0]!["roleId"] = 0;
        File.WriteAllText(Path.Combine(numbers, "battle-data.json"), numeric.ToJsonString());
        var battle = await PrepareProcessTests.Run("pwsh", "-NoProfile", "-File", Path.Combine(repo, "scripts", "Prepare-BattleData.ps1"),
            "-InputPath", numbers, "-OutputPath", Path.Combine(native, "Numbers"), "-GodotPath", Godot);
        Assert.True(battle.ExitCode == 0, battle.Text);
        foreach (string name in new[] { "Font.tres", "Prompt-00.tres", "Prompt-01.tres", "Prompt-02.tres" })
            File.Copy(Path.Combine(Project, "Tests", "Fixtures", "TriggerDialogue", name), Path.Combine(native, name));
        foreach (string language in new[] { "zh-Hans", "en" })
            WriteNative("Language." + language + ".tres", "PalDialogueLanguage", $"Language = \"{language}\"\nSourceIdentity = \"literal-process-test\"\nMessages = Array[Resource]([])");
        // No display blocks are used by this minimal process representative; direct tests cover glyph closure.
        WriteSprite("Player.tres", 10); WriteSprite("Enemy.tres", 1);
        WriteNative("Background.tres", "PalDialogueIndexedImage", "SourceIdentity = \"literal-process-test\"\nWidth = 320\nHeight = 200\nPaletteIndices = PackedByteArray(" + string.Join(",", Enumerable.Repeat("1", 64000)) + ")\nAlpha = PackedByteArray(" + string.Join(",", Enumerable.Repeat("255", 64000)) + ")");
        Directory.CreateDirectory(Path.Combine(Input, "events"));
        File.WriteAllText(Path.Combine(Input, "events", "index.json"), """
        {"formatVersion":1,"profile":"faithful","source":{"archive":"SSS.MKF","chunk":4,"byteOrder":"little","instructionCount":4,"byteLength":32,"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},"files":["story"]}
        """);
        File.WriteAllText(Path.Combine(Input, "events", "story.json"), """
        {"formatVersion":1,"profile":"faithful","fileId":"story","entries":[{"id":"npc","owner":{"type":"eventObject","id":1},"slot":"wTriggerScript","kind":"trigger","target":"L0001","sourceAddress":1}],"commands":[
        {"label":"L0000","op":"raw","next":"L0001","source":{"index":0,"opcode":134,"operands":[0,0,0]}},
        {"label":"L0001","op":"startBattle","teamId":18,"lossTarget":"L0003","isBoss":true,"fleeTarget":null,"next":"L0002","source":{"index":1,"opcode":7,"operands":[18,3,0]}},
        {"label":"L0002","op":"end","mode":"keep","next":"L0003","source":{"index":2,"opcode":0,"operands":[0,0,0]}},
        {"label":"L0003","op":"end","mode":"keep","next":null,"source":{"index":3,"opcode":0,"operands":[0,0,0]}}]}
        """);
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(staticInput, "manifest.json")))!;
        File.WriteAllText(Path.Combine(Input, "story-package.json"), JsonSerializer.Serialize(new
        {
            formatVersion = 1, sourceIdentity = "literal process test, not desktop art", staticResources = Uri(staticOutput),
            staticProfile = Uri(staticOutput) + "/Profiles/Startup.tres", bundleSha256 = manifest["bundleSha256"]!.GetValue<string>(),
            entryFileId = "story", entryId = "npc", ownerId = 1, entryAddress = 1, battleContentUri = Uri(native) + "/Numbers/BattleContent.tres",
            dialogueDirectory = Uri(native), portraits = Array.Empty<object>(),
            visual = new { roleId = 0, playerSpriteUri = Uri(native) + "/Player.tres", playerPosition = new[] { 240, 170 },
                paletteUri = Uri(staticOutput) + "/Palettes/Palette.tres", teams = new[] { new { teamId = 18, targetId = 7,
                    backgroundUri = Uri(native) + "/Background.tres", enemySpriteUri = Uri(native) + "/Enemy.tres", enemyPosition = new[] { 100, 120 }, idleFrames = 1, magicFrames = 0, attackFrames = 0, actWaitFrames = 0 } } }
        }));
    }
    private void WriteNative(string name, string type, string fields) => File.WriteAllText(Path.Combine(native, name),
        $"[gd_resource type=\"Resource\" script_class=\"{type}\" format=3]\n[ext_resource type=\"Script\" path=\"res://Scripts/Resources/{type}.cs\" id=\"1\"]\n[resource]\nscript = ExtResource(\"1\")\n{fields}\n");
    private void WriteSprite(string name, int count)
    {
        var text = new StringBuilder("[gd_resource type=\"Resource\" script_class=\"PalSprite\" format=3]\n[ext_resource type=\"Script\" path=\"res://Scripts/Resources/PalSprite.cs\" id=\"1\"]\n[ext_resource type=\"Script\" path=\"res://Scripts/Resources/PalFrame.cs\" id=\"2\"]\n");
        for (int i = 0; i < count; i++) text.Append($"[sub_resource type=\"Resource\" id=\"F{i}\"]\nscript = ExtResource(\"2\")\nIndex = {i}\nWidth = 1\nHeight = 1\nIndices = PackedByteArray(1)\nAlpha = PackedByteArray(255)\n");
        text.Append($"[resource]\nscript = ExtResource(\"1\")\nSpriteId = 41\nDecodedByteLength = 100\nFrameCount = {count}\nSource = {{\"author\": \"literal-process-test\"}}\nFrames = Array[Resource]([" + string.Join(",", Enumerable.Range(0, count).Select(i => $"SubResource(\"F{i}\")")) + "])\n");
        File.WriteAllText(Path.Combine(native, name), text.ToString());
    }
    private Task<PrepareProcessTests.Result> Editor(params string[] args) => PrepareProcessTests.Run(Godot, ["--headless", "--path", Project, "res://Editor/PrepareStoryPackage.tscn", "--", .. args]);
    private Task<PrepareProcessTests.Result> Wrapper(params string[] args) => PrepareProcessTests.Run("pwsh", ["-NoProfile", "-NonInteractive", "-File", Path.Combine(repo, "scripts", "Prepare-StoryPackage.ps1"), .. args]);
    private string Uri(string path) => "res://" + Path.GetRelativePath(Project, path).Replace('\\', '/');
    private static string[] Snapshot(string directory) => Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
        .Select(p => Path.GetRelativePath(directory, p) + ":" + M1Bundle.Hash(File.ReadAllBytes(p))).ToArray();
    public void Dispose()
    {
        foreach (string path in new[] { root, native, output, output + "-editor" }) if (Directory.Exists(path)) Directory.Delete(path, true);
    }
}
