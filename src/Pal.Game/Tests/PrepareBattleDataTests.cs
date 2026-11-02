using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using GdUnit4;
using Godot;
using Pal.Core;
using Pal.Game.Editor;
using Pal.Game.Profiles;
using static GdUnit4.Assertions;

namespace Pal.Game.Tests;

[TestSuite, RequireGodotRuntime]
public class PrepareBattleDataTests
{
    [TestCase]
    public void PublicPreparePublishesStrictNativeContentAndRealKoSettlesRole()
    {
        using var fixture = new Preparation();
        byte[] before = File.ReadAllBytes(fixture.Source);
        string uri = PrepareBattleData.Prepare(fixture.Input, fixture.Output);
        AssertThat(uri).IsEqual(ProjectSettings.LocalizePath(fixture.Output).TrimEnd('/') + "/BattleContent.tres");
        var content = NativeBattleContent.Load(uri);
        AssertThat(content.InitialRole.PrimaryExperience.Count).IsEqual(17);
        AssertThat(content.InitialRole.HiddenExperience["flee"].Count).IsEqual(55);
        AssertThat(content.Growth.LevelUpMagics[1]).IsEqual(new BattleLevelUpMagic(3, 10));
        var battle = new ClassicBattleLifecycleSession(content.InitialRole, content.Player, content.Teams[18], content.Growth, new BattleRandom());
        battle.SelectAttack(7);
        AssertThat(battle.ResolveNextAction()).IsTrue();
        AssertThat(battle.Completion!.Outcome).IsEqual(ClassicBattlePhase.Won);
        AssertThat(battle.Current.Role.Hp).IsEqual(125);
        AssertThat(battle.Current.Role.Mp).IsEqual(35);
        AssertThat(battle.OwnsCompletion(battle.Completion)).IsTrue();
        AssertThat(File.ReadAllBytes(fixture.Source)).IsEqual(before);
        AssertThat(Directory.GetFileSystemEntries(fixture.Output).Length).IsEqual(1);
    }

    [TestCase]
    public void LegalNonzeroCapabilitiesAndZeroThresholdsArePreparedWithoutExecutionGate()
    {
        using var fixture = new Preparation();
        var json = JsonNode.Parse(File.ReadAllText(fixture.Source))!;
        json["roles"]![0]!["equipment"]![0] = 12;
        json["players"]![0]!["equipment"]![0] = 12;
        json["players"]![0]!["dyingScript"] = 123;
        json["teams"]![0]!["enemies"]![0]!["cash"] = 7;
        json["growth"]![0]!["levelUpExperience"]![2] = 0;
        json["growth"]![0]!["levelUpMagics"]!.AsArray().Add(new JsonObject { ["level"] = 2, ["magicId"] = 9 });
        File.WriteAllText(fixture.Source, json.ToJsonString());
        var content = NativeBattleContent.Load(PrepareBattleData.Prepare(fixture.Input, fixture.Output));
        AssertThat(content.InitialRole.Equipment[0]).IsEqual(12);
        AssertThat(content.Player.Equipment[0]).IsEqual(12);
        AssertThat(content.Player.DyingScript).IsEqual(123);
        AssertThat(content.Teams[18].Cash).IsEqual(7);
        AssertThat(content.Growth.LevelUpExperience[2]).IsEqual(0);
        AssertThat(content.Growth.LevelUpMagics.Count).IsEqual(3);
        AssertThat(content.Growth.LevelUpMagics[2]).IsEqual(new BattleLevelUpMagic(2, 9));
    }

    [TestCase("missing", "exactly battle-data.json")]
    [TestCase("extra-file", "exactly battle-data.json")]
    [TestCase("extra-directory", "exactly battle-data.json")]
    [TestCase("nested-file", "exactly battle-data.json")]
    [TestCase("wrong-file", "exactly battle-data.json")]
    [TestCase("empty", "1..16777216 bytes")]
    [TestCase("oversize", "1..16777216 bytes")]
    [TestCase("utf8", "UTF-8")]
    [TestCase("utf16", "UTF-8")]
    [TestCase("json", "battle-data.json")]
    [TestCase("missing-field", "formatVersion")]
    [TestCase("wrong-version", "formatVersion")]
    public void InvalidSourceIsRejectedWithoutChangingInputOrExistingEmptyOutput(string defect, string diagnostic)
    {
        using var fixture = new Preparation();
        switch (defect)
        {
            case "missing": File.Delete(fixture.Source); break;
            case "extra-file": File.WriteAllText(Path.Combine(fixture.Input, "extra"), "keep"); break;
            case "extra-directory": Directory.CreateDirectory(Path.Combine(fixture.Input, "empty")); break;
            case "nested-file": Directory.CreateDirectory(Path.Combine(fixture.Input, "nested")); File.WriteAllText(Path.Combine(fixture.Input, "nested", "extra"), "keep"); break;
            case "wrong-file": File.Move(fixture.Source, fixture.Source + ".wrong"); break;
            case "empty": File.WriteAllBytes(fixture.Source, []); break;
            case "oversize": using (var stream = File.OpenWrite(fixture.Source)) stream.SetLength(16777217); break;
            case "utf8": File.WriteAllBytes(fixture.Source, [0xc3, 0x28]); break;
            case "utf16": File.WriteAllBytes(fixture.Source, [0xff, 0xfe, 0x7b, 0]); break;
            case "json": File.WriteAllText(fixture.Source, "{"); break;
            case "missing-field": File.WriteAllText(fixture.Source, File.ReadAllText(fixture.Source).Replace("\"formatVersion\": 1,", "")); break;
            case "wrong-version": File.WriteAllText(fixture.Source, File.ReadAllText(fixture.Source).Replace("\"formatVersion\": 1", "\"formatVersion\": 2")); break;
        }
        string[] before = Snapshot(fixture.Input);
        Directory.CreateDirectory(fixture.Output);
        Reject(fixture.Input, fixture.Output, diagnostic);
        AssertThat(Snapshot(fixture.Input)).IsEqual(before);
        AssertThat(Directory.Exists(fixture.Output)).IsTrue();
        AssertThat(Directory.GetFileSystemEntries(fixture.Output).Length).IsEqual(0);
    }

    [TestCase("file")]
    [TestCase("directory")]
    [TestCase("published")]
    public void OutputEntriesAreNeverOverwrittenOrRemoved(string entry)
    {
        using var fixture = new Preparation();
        Directory.CreateDirectory(fixture.Output);
        string sentinel = Path.Combine(fixture.Output, entry == "published" ? "BattleContent.tres" : "sentinel");
        if (entry == "directory") Directory.CreateDirectory(sentinel);
        else File.WriteAllText(sentinel, "caller-owned");
        string[] before = Snapshot(fixture.Output);
        Reject(fixture.Input, fixture.Output, "new or empty");
        AssertThat(Snapshot(fixture.Output)).IsEqual(before);
    }

    [TestCase("output-file", "existing file")]
    [TestCase("outside-assets", "project Assets")]
    [TestCase("input-file", "existing directory")]
    [TestCase("input-missing", "existing directory")]
    [TestCase("input-project", "outside src/Pal.Game")]
    [TestCase("overlap-input-parent", "overlap")]
    [TestCase("overlap-output-parent", "overlap")]
    public void PublicApiRejectsUnsafePathsBeforeCreatingOutput(string defect, string diagnostic)
    {
        using var fixture = new Preparation();
        string input = fixture.Input, output = fixture.Output;
        switch (defect)
        {
            case "output-file": Directory.CreateDirectory(Path.GetDirectoryName(output)!); File.WriteAllText(output, "keep"); break;
            case "outside-assets": output = Path.Combine(fixture.Input, "outside"); break;
            case "input-file": input = fixture.Source; break;
            case "input-missing": input += "-missing"; break;
            case "input-project": input = ProjectSettings.GlobalizePath("res://Tests"); break;
            case "overlap-input-parent": input = Path.GetDirectoryName(output)!; break;
            case "overlap-output-parent": input = Path.Combine(output, "input"); break;
        }
        try
        {
            Reject(input, output, diagnostic);
            if (defect == "output-file") AssertThat(File.ReadAllText(output)).IsEqual("keep");
            else AssertThat(Directory.Exists(output)).IsFalse();
        }
        finally { if (defect == "output-file") File.Delete(output); }
    }

    [TestCase]
    public void BomAndNormalizedPathsAndMissingAncestorsAreSupportedWithoutOverwrite()
    {
        using var fixture = new Preparation();
        byte[] bytes = File.ReadAllBytes(fixture.Source);
        File.WriteAllBytes(fixture.Source, new byte[] { 0xef, 0xbb, 0xbf }.Concat(bytes).ToArray());
        string output = Path.Combine(fixture.Output, "new-parent", "native");
        string uri = PrepareBattleData.Prepare(fixture.Input + Path.DirectorySeparatorChar + ".", output + Path.DirectorySeparatorChar);
        AssertThat(NativeBattleContent.Load(uri).InitialRole.Hp).IsEqual(100);
        string[] before = Snapshot(fixture.Output);
        Reject(fixture.Input, output, "new or empty");
        AssertThat(Snapshot(fixture.Output)).IsEqual(before);
        AssertThat(File.ReadAllBytes(fixture.Source).Length).IsEqual(bytes.Length + 3);
    }

    [TestCase("input")]
    [TestCase("input-ancestor")]
    [TestCase("output")]
    [TestCase("output-ancestor")]
    [TestCase("dangling-output-ancestor")]
    [TestCase("input-extra-link")]
    public void ReparseDirectoriesAndAncestorsAreRejectedWithoutTouchingTargets(string kind)
    {
        using var fixture = new Preparation();
        string link = kind == "input-extra-link" ? Path.Combine(fixture.Input, "linked")
            : kind.StartsWith("input", StringComparison.Ordinal) ? fixture.Input + "-link" : fixture.Output;
        string target = kind is "input" or "input-ancestor" ? fixture.Input : fixture.Input + "-target";
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        CreateDirectoryLink(link, target);
        if (kind == "dangling-output-ancestor") Directory.Delete(target);
        string input = kind == "input" ? link : kind == "input-ancestor" ? Path.Combine(link, "missing") : fixture.Input;
        string output = kind == "output" ? link : kind.Contains("output", StringComparison.Ordinal) ? Path.Combine(link, "missing", "native") : fixture.Output;
        byte[] before = File.ReadAllBytes(fixture.Source);
        try
        {
            Reject(input, output, "link/reparse");
            AssertThat(File.ReadAllBytes(fixture.Source)).IsEqual(before);
            AssertThat((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0).IsTrue();
            if (Directory.Exists(target) && target != fixture.Input) AssertThat(Directory.GetFileSystemEntries(target).Length).IsEqual(0);
        }
        finally
        {
            Directory.Delete(link);
            if (target != fixture.Input && Directory.Exists(target)) Directory.Delete(target);
        }
    }

    [TestCase]
    public void ExistingEmptyOutputAcceptsNormalizedWindowsCaseAndParentSegments()
    {
        using var fixture = new Preparation();
        Directory.CreateDirectory(fixture.Output);
        string input = fixture.Input;
        string output = Path.Combine(fixture.Output, "..", Path.GetFileName(Path.TrimEndingDirectorySeparator(fixture.Output)));
        if (OperatingSystem.IsWindows()) { input = input.ToUpperInvariant(); output = output.ToUpperInvariant(); }
        string uri = PrepareBattleData.Prepare(input, output);
        AssertThat(uri.StartsWith("res://Assets/", StringComparison.Ordinal)).IsTrue();
        AssertThat(NativeBattleContent.Load(uri).InitialRole.Hp).IsEqual(100);
        AssertThat(File.Exists(Path.Combine(fixture.Output, "BattleContent.tres"))).IsTrue();
    }

    private static void CreateDirectoryLink(string link, string target)
    {
        if (!OperatingSystem.IsWindows()) { Directory.CreateSymbolicLink(link, target); return; }
        var info = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in new[] { "/c", "mklink", "/J", Path.GetFullPath(link), Path.GetFullPath(target) }) info.ArgumentList.Add(arg);
        using var process = System.Diagnostics.Process.Start(info)!;
        string text = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        AssertThat(process.ExitCode).OverrideFailureMessage(text).IsEqual(0);
    }

    private static void Reject(string input, string output, string diagnostic)
    {
        InvalidDataException? error = null;
        try { PrepareBattleData.Prepare(input, output); }
        catch (InvalidDataException caught) { error = caught; }
        AssertThat(error is not null).OverrideFailureMessage("Expected public Prepare rejection: " + diagnostic).IsTrue();
        AssertThat(error!.Message.Contains(diagnostic, StringComparison.Ordinal)).OverrideFailureMessage(error.Message).IsTrue();
    }
    private static string[] Snapshot(string root) => Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal).Select(path => Path.GetRelativePath(root, path) + ":" +
            (File.Exists(path) ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))) : "directory")).ToArray();

    private sealed class Preparation : IDisposable
    {
        internal string Input { get; } = Path.Combine(Path.GetTempPath(), "pal-numeric-input-" + Guid.NewGuid().ToString("N"));
        internal string Output { get; } = ProjectSettings.GlobalizePath("res://Assets/PrepareTests/" + Guid.NewGuid().ToString("N"));
        internal string Source => Path.Combine(Input, "battle-data.json");
        internal Preparation()
        {
            Directory.CreateDirectory(Input);
            File.Copy(Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "../../tests/Fixtures/ModernBattleData/battle-data.json")), Source);
        }
        public void Dispose()
        {
            if (Directory.Exists(Input)) Directory.Delete(Input, true);
            if (Directory.Exists(Output)) Directory.Delete(Output, true);
        }
    }
    private sealed class BattleRandom : IBattleRandom
    {
        public int NextInclusive(int from, int to) => from == 0 && to == 5 ? 1 : from;
        public float NextInclusive(float from, float to) => 1;
    }
}
