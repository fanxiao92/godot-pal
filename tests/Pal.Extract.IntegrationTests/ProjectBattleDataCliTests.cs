using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Pal.Core;
using Xunit;

namespace Pal.Extract.Tests;

public sealed class ProjectBattleDataCliTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"pal-project-battle-{Guid.NewGuid():N}");
    private readonly string input;
    private readonly string output;

    public ProjectBattleDataCliTests()
    {
        input = Path.Combine(root, "input"); output = Path.Combine(root, "output");
        Directory.CreateDirectory(input);
        Save(BattleProjectionFixture.Create());
    }

    [Fact]
    public async Task Real_four_option_cli_is_deterministic_readonly_and_prepare_directory_compatible()
    {
        var before = Directory.GetFiles(input).ToDictionary(p => p, p => (Bytes: File.ReadAllBytes(p), Time: File.GetLastWriteTimeUtc(p)));
        Directory.CreateDirectory(output); // empty preexisting directory is explicitly accepted
        var result = await Run();
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("Battle data projected: independent base-table context (upstream-verified input required).\n", result.Stdout.Replace("\r\n", "\n"));
        Assert.Empty(result.Stderr);
        string numeric = Path.Combine(output, "numeric", "battle-data.json");
        Assert.Equal(new[] { "battle-data.json" }, Directory.GetFiles(Path.GetDirectoryName(numeric)!).Select(Path.GetFileName));
        var content = ModernBattleContent.Parse(File.ReadAllText(numeric));
        Assert.Equal(new[] { 4, 0 }, content.Players.Select(x => x.RoleId));
        Assert.Equal(123, content.InitialRoles[4].Attack);
        Assert.Equal(-32768, content.EnemyTeams[0][1].Defense);
        var receipt = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "receipt.json")))!;
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(numeric))), receipt["output"]!["sha256"]!.GetValue<string>());
        Assert.Equal(new FileInfo(numeric).Length, receipt["output"]!["byteLength"]!.GetValue<long>());
        Assert.Equal(14, receipt["modernInputs"]!.AsArray().Count);
        foreach (var pair in before)
        {
            Assert.Equal(pair.Value.Bytes, File.ReadAllBytes(pair.Key));
            Assert.Equal(pair.Value.Time, File.GetLastWriteTimeUtc(pair.Key));
            var identity = receipt["modernInputs"]!.AsArray().Single(x => x!["file"]!.GetValue<string>() == Path.GetFileName(pair.Key))!;
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(pair.Value.Bytes)), identity["sha256"]!.GetValue<string>());
        }
        string second = Path.Combine(root, "second");
        var reordered = await CliProcess.Run(root, "project-battle-data", "--teams", "0", "--output", second, "--roles", "4,0", "--input", input);
        Assert.Equal(0, reordered.ExitCode);
        Assert.Empty(reordered.Stderr);
        Assert.Equal(File.ReadAllBytes(numeric), File.ReadAllBytes(Path.Combine(second, "numeric", "battle-data.json")));
        Assert.Equal(File.ReadAllBytes(Path.Combine(output, "receipt.json")), File.ReadAllBytes(Path.Combine(second, "receipt.json")));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    [InlineData("empty")]
    [InlineData("signed")]
    [InlineData("space")]
    [InlineData("hex")]
    [InlineData("duplicateRoles")]
    [InlineData("duplicateTeams")]
    [InlineData("trailing")]
    public async Task Invalid_argv_exits_two_without_reading_or_writing(string mutation)
    {
        string[] args = ["project-battle-data", "--input", "not-present", "--roles", "4,0", "--teams", "0", "--output", output];
        if (mutation == "missing") args = args[..7];
        else if (mutation == "duplicate") args[5] = "--roles";
        else if (mutation == "unknown") args[3] = "--unknown";
        else if (mutation == "duplicateTeams") args[6] = "0,0";
        else args[4] = mutation switch { "empty" => "", "signed" => "+4", "space" => "4, 0", "hex" => "0x4", "duplicateRoles" => "4,4", _ => "4," };
        var result = await CliProcess.Run(root, args);
        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Contains("Unsupported arguments for project-battle-data", result.Stderr);
        Assert.False(Directory.Exists(output));
    }

    [Theory]
    [InlineData("missingTable", "13")]
    [InlineData("extraTable", "13")]
    [InlineData("badUtf8", "UTF-8")]
    [InlineData("duplicateKey", "duplicate JSON key")]
    [InlineData("unknownKey", "unknown field")]
    [InlineData("wrongType", "projection failed")]
    [InlineData("truncated", "JSON")]
    [InlineData("wrongCount", "recordCount")]
    [InlineData("sourceHash", "sha256")]
    [InlineData("nonzeroEquipment", "equipment[2][4]")]
    [InlineData("magicFFFF", "magicId")]
    [InlineData("fourRoles", "roles")]
    [InlineData("role5", "sixth")]
    [InlineData("missingTeam", "missing referenced")]
    public async Task Invalid_data_exits_one_with_diagnostics_and_leaves_output_untouched(string mutation, string diagnostic)
    {
        var files = BattleProjectionFixture.Create();
        if (mutation == "missingTable") File.Delete(Path.Combine(input, "enemy.json"));
        else if (mutation == "extraTable") File.WriteAllText(Path.Combine(input, "extra.json"), "{}");
        else if (mutation == "badUtf8") File.WriteAllBytes(Path.Combine(input, "index.json"), [0xff, 0xfe]);
        else if (mutation == "duplicateKey") File.WriteAllText(Path.Combine(input, "index.json"), "{\"formatVersion\":1,\"formatVersion\":1}");
        else if (mutation == "truncated") File.WriteAllText(Path.Combine(input, "index.json"), "{");
        else if (mutation is not "fourRoles" and not "role5" and not "missingTeam")
        {
            string file = mutation is "nonzeroEquipment" ? "player-roles.json" : mutation is "magicFFFF" ? "enemy.json" : "index.json";
            BattleProjectionFixture.Mutate(files, file, d =>
            {
                if (mutation == "unknownKey") d["unexpected"] = true;
                else if (mutation == "wrongType") d["tables"] = "bad";
                else if (mutation == "wrongCount") d["tables"]![0]!["recordCount"] = 999;
                else if (mutation == "sourceHash") d["sources"]![1]!["sha256"] = "not-a-hash";
                else if (mutation == "nonzeroEquipment") d["records"]![0]!["equipment"]![2]![4] = 8;
                else if (mutation == "magicFFFF") { d["records"]![1]!["magicId"] = 65535; d["records"]![1]!["magicRate"] = 0; }
                else d["records"]![0]!["enemies"]![1] = 65534;
            });
            Save(files);
        }
        Directory.CreateDirectory(output);
        var result = await Run(mutation == "fourRoles" ? "4,0,1,2" : mutation == "role5" ? "5" : "4,0", mutation == "missingTeam" ? "99" : "0");
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Contains(diagnostic, result.Stderr, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(output));
    }

    [Theory]
    [InlineData("emptySlot", 1, 1)]
    [InlineData("tailEmptySlot", 4, -1)]
    public async Task Zero_source_rows_project_as_isEmpty_runtime_slots(string mutation, int sourceSlot, int expectedIndex)
    {
        // #140 (battle.c:1617-1717): a zero source row is a legal empty runtime slot, not a
        // projection fault — the CLI exits zero and emits the canonical isEmpty enemy. The old
        // exit-one expectation predates #140 and was not migrated with the unit-layer contract.
        // FFFF rows are skipped without runtime slots, so a zero tail row lands at the dense
        // array end (expectedIndex -1) rather than at its source index.
        var files = BattleProjectionFixture.Create();
        BattleProjectionFixture.Mutate(files, "enemy-team.json", d =>
            d["records"]![0]!["enemies"]![sourceSlot] = 0);
        Save(files);
        var result = await Run();
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Stderr);
        string numeric = Path.Combine(output, "numeric", "battle-data.json");
        var content = JsonNode.Parse(File.ReadAllText(numeric))!;
        var enemies = content["teams"]![0]!["enemies"]!.AsArray();
        Assert.Equal(1, enemies.Count(enemy => (bool?)enemy!["isEmpty"] == true));
        int actualIndex = enemies.TakeWhile(enemy => (bool?)enemy!["isEmpty"] != true).Count();
        Assert.Equal(expectedIndex == -1 ? enemies.Count - 1 : expectedIndex, actualIndex);
        var empty = enemies[actualIndex]!;
        Assert.Equal(0, empty["id"]!.GetValue<int>());
        Assert.Equal(0, empty["hp"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("nonempty")]
    [InlineData("file")]
    [InlineData("inside")]
    [InlineData("equal")]
    [InlineData("ancestor")]
    public async Task Output_protection_preserves_existing_files_and_all_input_bytes(string mutation)
    {
        var before = Directory.GetFiles(input).ToDictionary(p => p, File.ReadAllBytes);
        string destination = output;
        if (mutation == "nonempty") { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "keep.txt"), "keep"); }
        else if (mutation == "file") File.WriteAllText(output, "keep");
        else if (mutation == "inside") destination = Path.Combine(input, "nested");
        else if (mutation == "equal") destination = input;
        else destination = root;
        var result = await CliProcess.Run(root, "project-battle-data", "--input", input, "--roles", "4,0", "--teams", "0", "--output", destination);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.NotEmpty(result.Stderr);
        foreach (var pair in before) Assert.Equal(pair.Value, File.ReadAllBytes(pair.Key));
        if (mutation == "file") Assert.Equal("keep", File.ReadAllText(output));
        if (mutation == "nonempty") Assert.Equal("keep", File.ReadAllText(Path.Combine(output, "keep.txt")));
        Assert.False(Directory.Exists(Path.Combine(destination, "numeric")));
    }

    [Theory]
    [InlineData("inputRoot")]
    [InlineData("outputRoot")]
    [InlineData("inputAncestor")]
    [InlineData("outputAncestor")]
    public async Task Linked_roots_or_ancestors_are_refused_without_touching_targets(string mode)
    {
        string target = Path.Combine(root, "target");
        string link = Path.Combine(root, "link");
        Directory.CreateDirectory(target);
        DirectoryLinkFixture.Create(link, mode.StartsWith("input", StringComparison.Ordinal) ? input : target);
        try
        {
            string selectedInput = mode == "inputRoot" ? link : mode == "inputAncestor" ? Path.Combine(link, "child") : input;
            string selectedOutput = mode == "outputRoot" ? link : mode == "outputAncestor" ? Path.Combine(link, "child") : output;
            var result = await CliProcess.Run(root, "project-battle-data", "--input", selectedInput, "--roles", "4", "--teams", "0", "--output", selectedOutput);
            Assert.Equal(1, result.ExitCode);
            Assert.Contains("links/reparse", result.Stderr);
            Assert.Empty(Directory.EnumerateFileSystemEntries(target));
        }
        finally { Directory.Delete(link); }
    }

    [Fact]
    public async Task Table_path_link_is_refused_before_reading_its_contents()
    {
        // A directory junction at a required file path needs no symbolic-link privilege on Windows.
        string file = Path.Combine(input, "enemy.json");
        File.Delete(file);
        string target = Path.Combine(root, "foreign");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "keep"), "keep");
        DirectoryLinkFixture.Create(file, target);
        try
        {
            var result = await Run();
            Assert.Equal(1, result.ExitCode);
            Assert.Contains("enemy.json: input links/reparse", result.Stderr);
            Assert.False(Directory.Exists(output));
            Assert.Equal("keep", File.ReadAllText(Path.Combine(target, "keep")));
        }
        finally { Directory.Delete(file); }
    }

    private Task<(int ExitCode, string Stdout, string Stderr)> Run(string roles = "4,0", string teams = "0") =>
        CliProcess.Run(root, "project-battle-data", "--input", input, "--roles", roles, "--teams", teams, "--output", output);
    private void Save(Dictionary<string, byte[]> files)
    {
        foreach (var pair in files) File.WriteAllBytes(Path.Combine(input, pair.Key), pair.Value);
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
