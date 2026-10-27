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
        var fixture = BattleContextTestFixture.Valid();
        File.WriteAllBytes(Path.Combine(Input, "index.json"), fixture.IndexBytes);
        File.WriteAllBytes(Path.Combine(Input, "battle-context.json"), fixture.ContextBytes);
        M1Bundle.Write(StaticInput);
    }

    [Fact]
    public async Task Strict_authored_context_becomes_native_without_changing_source_or_overwriting()
    {
        var scene = await PrepareProcessTests.Run("pwsh", "-NoProfile", "-NonInteractive", "-File", Path.Combine(repo, "scripts", "Prepare-GameScene.ps1"),
            "-InputPath", StaticInput, "-OutputPath", staticOutput, "-GodotPath", Godot);
        Assert.True(scene.ExitCode == 0, scene.Text);
        string staticUri = "res://" + Path.GetRelativePath(Path.Combine(repo, "src", "Pal.Game"), staticOutput).Replace('\\', '/');
        var profile = await PrepareProcessTests.Run(Godot, "--headless", "--path", Path.Combine(repo, "src", "Pal.Game"),
            "--script", Path.Combine(repo, "tests", "Fixtures", "WriteStartupProfile.gd"), "--", "--native-root", staticUri);
        Assert.True(profile.ExitCode == 0, profile.Text);
        string staticProfile = staticUri + "/Profiles/Startup.tres";
        string[] before = Snapshot(Input);

        var result = await Prepare(battleOutput, staticUri, staticProfile);
        Assert.True(result.ExitCode == 0, result.Text);
        Assert.True(File.Exists(Path.Combine(battleOutput, "Battle", "Profile.tres")));
        Assert.Empty(Directory.GetFiles(battleOutput, "*.json", SearchOption.AllDirectories));
        Assert.Equal(before, Snapshot(Input));

        string emptyOutput = battleOutput + "-empty";
        Directory.CreateDirectory(emptyOutput);
        var empty = await Prepare(emptyOutput, staticUri, staticProfile);
        Assert.True(empty.ExitCode == 0, empty.Text);
        Assert.True(File.Exists(Path.Combine(emptyOutput, "Battle", "Profile.tres")));

        File.WriteAllText(Path.Combine(battleOutput, "keep.txt"), "keep");
        var overwrite = await Prepare(battleOutput, staticUri, staticProfile);
        Assert.Equal(1, overwrite.ExitCode);
        Assert.Contains("new or empty", overwrite.Text);
        Assert.Equal("keep", File.ReadAllText(Path.Combine(battleOutput, "keep.txt")));

        string corruptOutput = battleOutput + "-corrupt";
        File.AppendAllText(Path.Combine(Input, "battle-context.json"), " ");
        string[] corruptSource = Snapshot(Input);
        var corrupt = await Prepare(corruptOutput, staticUri, staticProfile);
        Assert.Equal(1, corrupt.ExitCode);
        Assert.Contains("SHA-256", corrupt.Text, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(corruptOutput) && Directory.EnumerateFiles(corruptOutput, "*", SearchOption.AllDirectories).Any());
        Assert.Equal(corruptSource, Snapshot(Input));
    }

    [Fact]
    public async Task Input_reparse_point_is_rejected_before_engine_or_output()
    {
        string link = Path.Combine(root, "linked-input");
        string target = Input;
        if (OperatingSystem.IsWindows())
        {
            var created = await PrepareProcessTests.Run("cmd.exe", "/c", "mklink", "/J", link, target);
            Assert.True(created.ExitCode == 0, created.Text);
        }
        else Directory.CreateSymbolicLink(link, target);
        try
        {
            string unavailable = Path.Combine(root, "godot-must-not-start.exe");
            var result = await PrepareProcessTests.Run("pwsh", "-NoProfile", "-NonInteractive", "-File", Path.Combine(repo, "scripts", "Prepare-BattleContext.ps1"),
                "-InputPath", link, "-OutputPath", battleOutput, "-StaticResources", "res://Assets/Static", "-StaticProfile", "res://Assets/Static/Profile.tres", "-GodotPath", unavailable);
            Assert.Equal(1, result.ExitCode);
            Assert.Contains("reparse", result.Text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(unavailable, result.Text);
            Assert.False(Directory.Exists(battleOutput));
        }
        finally { Directory.Delete(link); }
    }

    private Task<PrepareProcessTests.Result> Prepare(string output, string staticUri, string staticProfile) =>
        PrepareProcessTests.Run("pwsh", "-NoProfile", "-NonInteractive", "-File", Path.Combine(repo, "scripts", "Prepare-BattleContext.ps1"),
            "-InputPath", Input, "-OutputPath", output, "-StaticResources", staticUri, "-StaticProfile", staticProfile, "-GodotPath", Godot);

    private static string[] Snapshot(string directory) => Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal).Select(path => Path.GetRelativePath(directory, path) + ":" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)))).ToArray();

    public void Dispose()
    {
        foreach (string directory in new[] { root, staticOutput, battleOutput, battleOutput + "-empty", battleOutput + "-corrupt" })
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
