using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Pal.TestFixtures;
using Xunit;

namespace Pal.Prepare.Tests;

/// <summary>#236 end-to-end route-A verification: synthetic caller-verified M5 tables through
/// the formal CLI, explicit projection Prepare, Story battleProjection expectation, and the
/// shipped normal Main. Every stage consumes the previous stage's actual bytes; no test-only
/// battle host and no runtime JSON/MKF fallback. Identity drift is rejected by the owning
/// prepare stage with a located diagnostic.</summary>
public sealed class ProjectedStoryChainTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "pal-projected-chain-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> ownedOutputs = [];
    private readonly string repo;
    private string GameProject => Path.Combine(repo, "src", "Pal.Game");
    private string TestGameProject => Path.Combine(repo, "tests", "Pal.Game.Tests");
    private static string Godot => Environment.GetEnvironmentVariable("PAL_TEST_GODOT")
        ?? throw new InvalidOperationException("Set PAL_TEST_GODOT.");

    public ProjectedStoryChainTests()
    {
        repo = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(repo, "GodotPal.sln"))) repo = Directory.GetParent(repo)!.FullName;
        Directory.CreateDirectory(root);
    }

    [Fact]
    public async Task VerifiedM5ProjectionReachesStoryMainAndRejectsIdentityDrift()
    {
        string m5 = Path.Combine(root, "m5");
        Directory.CreateDirectory(m5);
        var tables = ProjectedStoryChainTables.Create();
        foreach (var pair in tables) File.WriteAllBytes(Path.Combine(m5, pair.Key), pair.Value);
        string[] m5Before = Snapshot(m5);

        string projection = Path.Combine(root, "projection");
        var cli = await Cli("project-battle-data", "--input", m5, "--roles", "0", "--teams", "18", "--output", projection);
        Assert.Equal(0, cli.ExitCode);
        Assert.Contains("Battle data projected: independent base-table context", cli.Text);
        Assert.DoesNotContain("failed", cli.Text, StringComparison.OrdinalIgnoreCase);
        string numericPath = Path.Combine(projection, "numeric", "battle-data.json");
        string receiptPath = Path.Combine(projection, "receipt.json");
        byte[] numericBytes = File.ReadAllBytes(numericPath);
        byte[] receiptBytes = File.ReadAllBytes(receiptPath);
        string numericSha = Convert.ToHexStringLower(SHA256.HashData(numericBytes));
        string receiptSha = Convert.ToHexStringLower(SHA256.HashData(receiptBytes));
        var receipt = JsonNode.Parse(File.ReadAllText(receiptPath))!;
        Assert.Equal("numeric/battle-data.json", receipt["output"]!["file"]!.GetValue<string>());
        Assert.Equal(numericSha, receipt["output"]!["sha256"]!.GetValue<string>());
        Assert.Equal(numericBytes.LongLength, receipt["output"]!["byteLength"]!.GetValue<long>());
        Assert.Equal(new[] { 0 }, receipt["roles"]!.AsArray().Select(row => row!.GetValue<int>()).ToArray());
        Assert.Equal(new[] { 18 }, receipt["teams"]!.AsArray().Select(row => row!.GetValue<int>()).ToArray());
        var inputs = receipt["modernInputs"]!.AsArray();
        Assert.Equal(14, inputs.Count);
        foreach (var input in inputs)
        {
            string file = input!["file"]!.GetValue<string>();
            Assert.True(tables.ContainsKey(file), file);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(tables[file])), input["sha256"]!.GetValue<string>());
            Assert.Equal(tables[file].LongLength, input["byteLength"]!.GetValue<long>());
        }
        var teamSlots = receipt["enemySlots"]!.AsArray().Where(row => row!["teamId"]!.GetValue<int>() == 18).ToArray();
        Assert.Equal(new[] { "loaded", "loaded", "skip-FFFF", "skip-FFFF", "skip-FFFF" },
            teamSlots.Select(row => row!["disposition"]!.GetValue<string>()).ToArray());
        Assert.Equal(new[] { 7, 7 }, teamSlots.Where(row => row!["disposition"]!.GetValue<string>() == "loaded")
            .Select(row => row!["objectId"]!.GetValue<int>()).ToArray());
        Assert.Equal(new[] { 1, 2 }, teamSlots.Where(row => row!["disposition"]!.GetValue<string>() == "loaded")
            .Select(row => row!["battleId"]!.GetValue<int>()).ToArray());
        var numeric = JsonNode.Parse(File.ReadAllText(numericPath))!;
        Assert.Equal(0, numeric["players"]![0]!["roleId"]!.GetValue<int>());
        Assert.Equal(30, numeric["players"]![0]!["hp"]!.GetValue<int>());
        Assert.Equal(1, numeric["teams"]!.AsArray().Count);
        var team = numeric["teams"]![0]!;
        Assert.Equal(18, team["teamId"]!.GetValue<int>());
        Assert.Equal(new[] { 1, 2 }, team["enemies"]!.AsArray().Select(enemy => enemy!["id"]!.GetValue<int>()).ToArray());
        Assert.Equal(new[] { 1, 1 }, team["enemies"]!.AsArray().Select(enemy => enemy!["hp"]!.GetValue<int>()).ToArray());
        Assert.Equal(new[] { 3, 3 }, team["enemies"]!.AsArray().Select(enemy => enemy!["experience"]!.GetValue<int>()).ToArray());
        Assert.Equal(new[] { 5, 5 }, team["enemies"]!.AsArray().Select(enemy => enemy!["cash"]!.GetValue<int>()).ToArray());

        var (battleOutputUri, battleOutput) = NewAssetOutput("battle");
        var battle = await GodotScene("res://Editor/PrepareBattleData.tscn", "--battle-input", Path.GetDirectoryName(numericPath)!,
            "--battle-output", battleOutput, "--projection-receipt", receiptPath);
        Assert.True(battle.ExitCode == 0, battle.Text);
        Assert.Contains("PAL_PREPARE_BATTLE_DATA:BattleContent.tres", battle.Text);
        Assert.True(File.Exists(Path.Combine(battleOutput, "BattleContent.tres")));
        string battleUri = battleOutputUri + "/BattleContent.tres";

        var (storyRootUri, storyRoot) = NewAssetOutput("story");
        string staticRoot = Path.Combine(storyRoot, "Static");
        Directory.CreateDirectory(Path.Combine(staticRoot, "Scenes"));
        File.Copy(Path.Combine(GameProject, "Samples", "AuthoredBattle", "Static", "Scenes", "Scene.tscn"),
            Path.Combine(staticRoot, "Scenes", "Scene.tscn"));
        string storyInput = Path.Combine(root, "story-input");
        Directory.CreateDirectory(Path.Combine(storyInput, "events"));
        string sample = Path.Combine(repo, "samples", "authored-battle", "PartyWin", "Story");
        foreach (string path in Directory.GetFiles(Path.Combine(sample, "events")))
            File.Copy(path, Path.Combine(storyInput, "events", Path.GetFileName(path)));
        var metadata = JsonNode.Parse(File.ReadAllText(Path.Combine(sample, "story-package.json")))!;
        var samplePlayers = metadata["visual"]!["players"]!.AsArray();
        var sampleTeam = metadata["visual"]!["teams"]![0]!;
        string enemySprite = sampleTeam["enemies"]![0]!["enemySpriteUri"]!.GetValue<string>();
        metadata["staticResources"] = storyRootUri + "/Static";
        metadata["battleContentUri"] = battleUri;
        metadata["visual"]!["players"] = new JsonArray(new JsonObject
        {
            ["roleId"] = 0,
            ["playerSpriteUri"] = samplePlayers[0]!["playerSpriteUri"]!.GetValue<string>(),
            ["playerPosition"] = new JsonArray(240, 170),
            ["attackEffectSpriteUri"] = samplePlayers[0]!["attackEffectSpriteUri"]!.GetValue<string>(),
            ["attackEffectIndex"] = 0
        });
        metadata["visual"]!["teams"] = new JsonArray(new JsonObject
        {
            ["teamId"] = 18,
            ["backgroundUri"] = sampleTeam["backgroundUri"]!.GetValue<string>(),
            ["enemies"] = new JsonArray(EnemySlot(1, 100, 120, enemySprite), EnemySlot(2, 128, 104, enemySprite))
        });
        metadata["battleProjection"] = new JsonObject
        {
            ["receiptSha256"] = receiptSha,
            ["receiptByteLength"] = receiptBytes.LongLength
        };
        metadata["sourceIdentity"] = metadata["sourceIdentity"]!.GetValue<string>()
            + " Test-owned #236 projected-chain derivation from synthetic M5 tables; not retail data.";
        File.WriteAllText(Path.Combine(storyInput, "story-package.json"), metadata.ToJsonString());
        string storyOutput = Path.Combine(storyRoot, "Story");
        var story = await GodotScene("res://Editor/PrepareStoryPackage.tscn", "--story-input", storyInput, "--story-output", storyOutput);
        Assert.True(story.ExitCode == 0, story.Text);
        Assert.Contains("PAL_PREPARE_STORY_PACKAGE:", story.Text);
        string storyUri = storyRootUri + "/Story/Story.tres";
        string visualUri = storyRootUri + "/Story/Visual.tres";
        Assert.True(File.Exists(Path.Combine(storyOutput, "Story.tres")));
        Assert.True(File.Exists(Path.Combine(storyOutput, "Visual.tres")));

        var observed = await TestGodotScene("res://Tests/ObserveProjectedBattle.tscn",
            "--story-profile", storyUri, "--battle-visual-profile", visualUri, "--battle-seed", "1");
        Assert.True(observed.ExitCode == 0, observed.Text);
        Assert.Contains($"PAL_PROJECTED_CHAIN:receipt={receiptSha}|{receiptBytes.Length};" +
            $"numeric={numericSha}|{numericBytes.Length};inputs=14;roles=0;teams=18;slots=1,2", observed.Text);
        Assert.Contains("PAL_PROJECTED_BATTLE_OBSERVED:outcome=Won;xp=6;cash=10;liveRole=0;liveXp=6;enemies=0;phase=Completed;failure=none", observed.Text);
        Assert.DoesNotContain("ERROR:", observed.Text);

        string driftNumericRoot = Path.Combine(root, "drift-numeric");
        Directory.CreateDirectory(driftNumericRoot);
        string numericText = File.ReadAllText(numericPath);
        int hpIndex = numericText.IndexOf("\"hp\": 1", StringComparison.Ordinal);
        Assert.True(hpIndex >= 0);
        string driftNumericText = numericText[..hpIndex] + "\"hp\": 2" + numericText[(hpIndex + 7)..];
        Assert.Equal(numericText.Length, driftNumericText.Length);
        File.WriteAllText(Path.Combine(driftNumericRoot, "battle-data.json"), driftNumericText);
        var (_, driftBattleOutput) = NewAssetOutput("drift-numeric");
        var driftBattle = await GodotScene("res://Editor/PrepareBattleData.tscn", "--battle-input", driftNumericRoot,
            "--battle-output", driftBattleOutput, "--projection-receipt", receiptPath);
        Assert.Equal(1, driftBattle.ExitCode);
        Assert.Contains("receipt.json.$.output.sha256", driftBattle.Text);
        Assert.DoesNotContain("PAL_PREPARE_BATTLE_DATA", driftBattle.Text);
        Assert.False(Directory.Exists(driftBattleOutput) && Directory.EnumerateFileSystemEntries(driftBattleOutput).Any());

        string driftStoryInput = Path.Combine(root, "drift-story-input");
        Directory.CreateDirectory(Path.Combine(driftStoryInput, "events"));
        foreach (string path in Directory.GetFiles(Path.Combine(storyInput, "events")))
            File.Copy(path, Path.Combine(driftStoryInput, "events", Path.GetFileName(path)));
        var driftMetadata = metadata.DeepClone();
        string driftedHash = (receiptSha[0] == '0' ? "1" : "0") + receiptSha[1..];
        driftMetadata["battleProjection"]!["receiptSha256"] = driftedHash;
        File.WriteAllText(Path.Combine(driftStoryInput, "story-package.json"), driftMetadata.ToJsonString());
        var (_, driftStoryOutput) = NewAssetOutput("drift-story");
        var driftStory = await GodotScene("res://Editor/PrepareStoryPackage.tscn",
            "--story-input", driftStoryInput, "--story-output", driftStoryOutput);
        Assert.Equal(1, driftStory.ExitCode);
        Assert.Contains("story battleProjection receipt identity does not match native battle content source", driftStory.Text);
        Assert.DoesNotContain("PAL_PREPARE_STORY_PACKAGE", driftStory.Text);
        Assert.False(Directory.Exists(driftStoryOutput) && Directory.EnumerateFileSystemEntries(driftStoryOutput).Any());

        Assert.Equal(m5Before, Snapshot(m5));
        Assert.Equal(numericBytes, File.ReadAllBytes(numericPath));
        Assert.Equal(receiptBytes, File.ReadAllBytes(receiptPath));
    }

    private static JsonObject EnemySlot(int targetId, int x, int y, string sprite) => new()
    {
        ["targetId"] = targetId,
        ["enemyPosition"] = new JsonArray(x, y),
        ["enemySpriteUri"] = sprite,
        ["idleFrames"] = 1,
        ["idleAnimSpeed"] = 2,
        ["magicFrames"] = 1,
        ["attackFrames"] = 1,
        ["actWaitFrames"] = 0
    };

    private Task<PrepareProcessTests.Result> Cli(params string[] args) =>
        PrepareProcessTests.Run(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            [Path.Combine(AppContext.BaseDirectory, "cli", "Pal.Extract.dll"), .. args]);

    private Task<PrepareProcessTests.Result> GodotScene(string scene, params string[] args) =>
        PrepareProcessTests.Run(Godot, ["--headless", "--path", GameProject, scene, "--", .. args]);

    private Task<PrepareProcessTests.Result> TestGodotScene(string scene, params string[] args) =>
        PrepareProcessTests.Run(Godot, ["--headless", "--path", TestGameProject, scene, "--", .. args]);

    private (string Uri, string Path) NewAssetOutput(string suffix)
    {
        string name = "chain-" + Guid.NewGuid().ToString("N") + "-" + suffix;
        string path = Path.Combine(GameProject, "Assets", "PrepareTests", name);
        ownedOutputs.Add(path);
        return ("res://Assets/PrepareTests/" + name, path);
    }

    private static string[] Snapshot(string directory) => Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal)
        .Select(path => Path.GetRelativePath(directory, path) + ":" +
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)))).ToArray();

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
        foreach (string path in ownedOutputs)
            if (Directory.Exists(path)) Directory.Delete(path, true);
    }
}
