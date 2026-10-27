using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Pal.TestFixtures;
using Xunit;

namespace Pal.Prepare.Tests;

public sealed class BattlePrepareProcessTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "pal-battle-prepare-" + Guid.NewGuid().ToString("N"));
    private readonly string repo;
    private readonly string staticOutput;
    private readonly string battleOutput;
    private string Input => Path.Combine(root, "battle-input");
    private string StaticInput => Path.Combine(root, "scene-input");
    private static string Godot => Environment.GetEnvironmentVariable("PAL_TEST_GODOT") ?? throw new InvalidOperationException("Set PAL_TEST_GODOT.");

    public BattlePrepareProcessTests()
    {
        repo = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(repo, "GodotPal.sln"))) repo = Directory.GetParent(repo)!.FullName;
        string assets = Path.Combine(repo, "src", "Pal.Game", "Assets", "PrepareTests");
        staticOutput = Path.Combine(assets, Guid.NewGuid().ToString("N"));
        battleOutput = Path.Combine(assets, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Input);
        WriteBattleContext(ValidBattleContext());
        M1Bundle.Write(StaticInput);
        M1Bundle.ConfigureFirstMandatoryBattle(StaticInput);
    }

    [Fact]
    public async Task Prepared_profile_is_strictly_validated_and_starts_through_shipped_main()
    {
        (string staticUri, string staticProfile) = await PrepareStatic();
        string[] before = Snapshot(Input);

        var result = await Prepare(battleOutput, staticUri, staticProfile);
        Assert.True(result.ExitCode == 0, result.Text);
        string prepared = Path.Combine(battleOutput, "Battle", "Profile.tres");
        Assert.True(File.Exists(prepared));
        Assert.True(new FileInfo(prepared).Length > 0);
        Assert.Empty(Directory.GetFiles(battleOutput, "*.json", SearchOption.AllDirectories));
        Assert.Equal(before, Snapshot(Input));

        string battleUri = ToUri(prepared);
        var main = await PrepareProcessTests.Run(Godot, "--headless", "--path", Path.Combine(repo, "src", "Pal.Game"),
            "--quit-after", "4", "--", "--m4-first-battle-profile", battleUri, "--m4-first-battle-language", "en");
        Assert.True(main.ExitCode == 0, main.Text);
        Assert.Contains("PAL_MAP_LOADED:" + staticUri + "/Scenes/Scene.tscn", main.Text);
        Assert.Contains("PAL_M4_FIRST_BATTLE_CONTEXT:scene2/map12; owner28/09C3; language=en", main.Text);
        Assert.Contains("PAL_GAME_READY:Core ready", main.Text);

        string emptyOutput = battleOutput + "-empty";
        Directory.CreateDirectory(emptyOutput);
        var empty = await Prepare(emptyOutput, staticUri, staticProfile);
        Assert.True(empty.ExitCode == 0, empty.Text);
        Assert.True(new FileInfo(Path.Combine(emptyOutput, "Battle", "Profile.tres")).Length > 0);
    }

    [Fact]
    public async Task Publication_is_single_move_no_overwrite_and_never_exposes_zero_byte_profile()
    {
        (string staticUri, string staticProfile) = await PrepareStatic();
        Task<PrepareProcessTests.Result> first = Prepare(battleOutput, staticUri, staticProfile);
        Task<PrepareProcessTests.Result> second = Prepare(battleOutput, staticUri, staticProfile);
        string destination = Path.Combine(battleOutput, "Battle", "Profile.tres");
        bool observedZero = false;
        while (!first.IsCompleted || !second.IsCompleted)
        {
            if (File.Exists(destination) && new FileInfo(destination).Length == 0) observedZero = true;
            await Task.Delay(2);
        }
        PrepareProcessTests.Result[] results = [await first, await second];
        Assert.Single(results, item => item.ExitCode == 0);
        Assert.Single(results, item => item.ExitCode == 1);
        Assert.False(observedZero);
        Assert.True(File.Exists(destination) && new FileInfo(destination).Length > 0);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(destination)!, "*.tmp-*.tres"));

        byte[] bytes = File.ReadAllBytes(destination);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        var noOverwrite = await Prepare(battleOutput, staticUri, staticProfile);
        Assert.Equal(1, noOverwrite.ExitCode);
        Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(destination))));
        Assert.Equal(bytes, File.ReadAllBytes(destination));
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("start")]
    [InlineData("search")]
    public async Task Static_owner_start_and_search_mismatch_fail_without_publication(string kind)
    {
        if (kind == "owner") M1Bundle.Mutate(StaticInput, "scene.json", node => node["objects"]![0]!["wTriggerScript"] = 0x09fe);
        else
        {
            BattleContextTestFixture changed = ValidBattleContext();
            if (kind == "start") changed.Document["snapshot"]!["world"]!["leader"]!["x"] = 1248;
            else changed.Document["snapshot"]!["world"]!["searchProof"]!["sampleIndex"] = 2;
            WriteBattleContext(changed);
        }
        (string staticUri, string staticProfile) = await PrepareStatic();
        var result = await Prepare(battleOutput, staticUri, staticProfile);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains(kind == "owner" ? "owner28" : kind == "start" ? "start identity" : "start/search identity", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(battleOutput, "Battle", "Profile.tres")));
        Assert.False(Directory.Exists(battleOutput) && Directory.EnumerateFiles(battleOutput, "*", SearchOption.AllDirectories).Any());
    }

    [Theory]
    [InlineData("root")]
    [InlineData("profile")]
    public async Task Static_resource_reparse_points_are_rejected_before_engine_or_output(string kind)
    {
        string assets = Path.Combine(repo, "src", "Pal.Game", "Assets", "PrepareTests");
        string targetRoot = Path.Combine(root, "external-static");
        Directory.CreateDirectory(targetRoot);
        File.WriteAllText(Path.Combine(targetRoot, "Profile.tres"), "external");
        string linkRoot = Path.Combine(assets, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(linkRoot)!);
        string rootUri;
        string profileUri;
        if (kind == "root")
        {
            await CreateLink(linkRoot, targetRoot, directory: true);
            rootUri = ToUri(linkRoot);
            profileUri = rootUri + "/Profile.tres";
        }
        else
        {
            Directory.CreateDirectory(linkRoot);
            string profileDirectoryLink = Path.Combine(linkRoot, "LinkedProfiles");
            await CreateLink(profileDirectoryLink, targetRoot, directory: true);
            rootUri = ToUri(linkRoot);
            profileUri = ToUri(Path.Combine(profileDirectoryLink, "Profile.tres"));
        }
        try
        {
            string unavailable = Path.Combine(root, "godot-must-not-start.exe");
            var result = await PrepareWithGodot(battleOutput, rootUri, profileUri, unavailable);
            Assert.Equal(1, result.ExitCode);
            Assert.Contains("reparse", result.Text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(unavailable, result.Text);
            Assert.False(Directory.Exists(battleOutput));
        }
        finally
        {
            string nested = Path.Combine(linkRoot, "LinkedProfiles");
            if (Directory.Exists(nested)) Directory.Delete(nested);
            if (Directory.Exists(linkRoot)) Directory.Delete(linkRoot, !new DirectoryInfo(linkRoot).Attributes.HasFlag(FileAttributes.ReparsePoint));
            else if (File.Exists(linkRoot)) File.Delete(linkRoot);
        }
    }

    [Fact]
    public async Task Input_reparse_point_is_rejected_before_engine_or_output()
    {
        string link = Path.Combine(root, "linked-input");
        await CreateLink(link, Input, directory: true);
        string staticRoot = Path.Combine(repo, "src", "Pal.Game", "Assets", "PrepareTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(staticRoot, "Scenes"));
        File.WriteAllText(Path.Combine(staticRoot, "Scenes", "Scene.tscn"), "fixture");
        File.WriteAllText(Path.Combine(staticRoot, "Profile.tres"), "fixture");
        try
        {
            string unavailable = Path.Combine(root, "godot-must-not-start.exe");
            string staticUri = ToUri(staticRoot);
            var result = await PrepareProcessTests.Run("pwsh", "-NoProfile", "-NonInteractive", "-File", Path.Combine(repo, "scripts", "Prepare-BattleContext.ps1"),
                "-InputPath", link, "-OutputPath", battleOutput, "-StaticResources", staticUri, "-StaticProfile", staticUri + "/Profile.tres", "-GodotPath", unavailable);
            Assert.Equal(1, result.ExitCode);
            Assert.Contains("reparse", result.Text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(unavailable, result.Text);
            Assert.False(Directory.Exists(battleOutput));
        }
        finally { Directory.Delete(link); Directory.Delete(staticRoot, true); }
    }

    private async Task<(string Root, string Profile)> PrepareStatic()
    {
        var scene = await PrepareProcessTests.Run("pwsh", "-NoProfile", "-NonInteractive", "-File", Path.Combine(repo, "scripts", "Prepare-GameScene.ps1"),
            "-InputPath", StaticInput, "-OutputPath", staticOutput, "-GodotPath", Godot);
        Assert.True(scene.ExitCode == 0, scene.Text);
        string staticUri = ToUri(staticOutput);
        var profile = await PrepareProcessTests.Run(Godot, "--headless", "--path", Path.Combine(repo, "src", "Pal.Game"),
            "--script", Path.Combine(repo, "tests", "Fixtures", "WriteStartupProfile.gd"), "--", "--native-root", staticUri, "--first-battle");
        Assert.True(profile.ExitCode == 0, profile.Text);
        return (staticUri, staticUri + "/Profiles/Startup.tres");
    }

    private Task<PrepareProcessTests.Result> Prepare(string output, string staticUri, string staticProfile) =>
        PrepareWithGodot(output, staticUri, staticProfile, Godot);

    private Task<PrepareProcessTests.Result> PrepareWithGodot(string output, string staticUri, string staticProfile, string godot) =>
        PrepareProcessTests.Run("pwsh", "-NoProfile", "-NonInteractive", "-File", Path.Combine(repo, "scripts", "Prepare-BattleContext.ps1"),
            "-InputPath", Input, "-OutputPath", output, "-StaticResources", staticUri, "-StaticProfile", staticProfile, "-GodotPath", godot);

    private static BattleContextTestFixture ValidBattleContext()
    {
        BattleContextTestFixture fixture = BattleContextTestFixture.Valid();
        fixture.Document["snapshot"]!["world"]!["leader"]!["x"] = 1264;
        fixture.Document["snapshot"]!["world"]!["leader"]!["y"] = 248;
        fixture.Document["snapshot"]!["world"]!["searchProof"]!["sampleIndex"] = 1;
        return fixture;
    }

    private void WriteBattleContext(BattleContextTestFixture fixture)
    {
        byte[] context = Encoding.UTF8.GetBytes(fixture.Document.ToJsonString());
        string hash = Convert.ToHexStringLower(SHA256.HashData(context));
        byte[] index = Encoding.UTF8.GetBytes(new JsonObject
        {
            ["formatVersion"] = 1,
            ["profile"] = "first-mandatory-battle-pre-trigger-v1",
            ["contextFile"] = "battle-context.json",
            ["contextSha256"] = hash
        }.ToJsonString());
        File.WriteAllBytes(Path.Combine(Input, "index.json"), index);
        File.WriteAllBytes(Path.Combine(Input, "battle-context.json"), context);
    }

    private string ToUri(string path) => "res://" + Path.GetRelativePath(Path.Combine(repo, "src", "Pal.Game"), path).Replace('\\', '/');

    private static async Task CreateLink(string link, string target, bool directory)
    {
        if (OperatingSystem.IsWindows())
        {
            var created = directory
                ? await PrepareProcessTests.Run("cmd.exe", "/c", "mklink", "/J", link, target)
                : await PrepareProcessTests.Run("cmd.exe", "/c", "mklink", link, target);
            Assert.True(created.ExitCode == 0, created.Text);
        }
        else if (directory) Directory.CreateSymbolicLink(link, target);
        else File.CreateSymbolicLink(link, target);
    }

    private static string[] Snapshot(string directory) => Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal).Select(path => Path.GetRelativePath(directory, path) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))).ToArray();

    public void Dispose()
    {
        foreach (string directory in new[] { root, staticOutput, battleOutput, battleOutput + "-empty", battleOutput + "-corrupt" })
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
