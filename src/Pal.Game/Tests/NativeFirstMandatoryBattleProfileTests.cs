using System;
using System.IO;
using System.Linq;
using System.Reflection;
using GdUnit4;
using Godot;
using Pal.Game.Profiles;
using Pal.Game.Resources;
using static GdUnit4.Assertions;

namespace Pal.Game.Tests;

[TestSuite, RequireGodotRuntime]
public class NativeFirstMandatoryBattleProfileTests
{
    [TestCase]
    public void StrictNativeProfileLoadsExactCoreIdentityAndCreatesIndependentSessions()
    {
        using var fixture = new NativeBattleProfileFixture();
        CheckedFirstMandatoryBattleProfile loaded = NativeFirstMandatoryBattleProfile.Load(fixture.Uri);
        var scene = NativeExplorationProfile.Load(fixture.Static.Uri, fixture.Static.Scene);
        loaded.ValidateStatic(fixture.Static.Scene, scene);
        var first = loaded.CreateSession(2, scene);
        var second = loaded.CreateSession(2, scene);
        first.State.Roles[0].Hp = 1;
        first.State.World.EventObjects[0].State = 0;
        AssertThat(second.State.Roles[0].Hp).IsEqual(150);
        AssertThat(second.State.World.EventObjects[0].State).IsEqual(2);
        AssertThat(loaded.Profile.ContextSha256).IsEqual(fixture.Core.ContextSha256);
    }

    [TestCase("version")]
    [TestCase("hash")]
    [TestCase("identity")]
    [TestCase("references")]
    [TestCase("closure-identity")]
    [TestCase("static-hash")]
    public void NativeProfileRejectsCorruptEnvelope(string defect)
    {
        using var fixture = new NativeBattleProfileFixture();
        switch (defect)
        {
            case "version": fixture.Native.FormatVersion = 2; break;
            case "hash": fixture.Native.ContextSha256 = new string('0', 64); break;
            case "identity": fixture.Native.Identity!.OwnerId = 29; break;
            case "references": fixture.Native.Resources[0].Id = "wrong"; break;
            case "closure-identity": ((PalBattleEventRangeIdentity)fixture.Native.Closure!.Entries[0].Identity!).BattleAddress++; break;
            case "static-hash": fixture.Native.StaticSceneSha256 = new string('0', 64); break;
        }
        fixture.Save();
        AssertThrown(() => NativeFirstMandatoryBattleProfile.Load(fixture.Uri));
    }

    [TestCase("identity-null")]
    [TestCase("party-null")]
    [TestCase("closure-entries-null")]
    [TestCase("static-root-null")]
    public void NativeProfileNormalizesMalformedNestedValuesWithSelectedUri(string defect)
    {
        using var fixture = new NativeBattleProfileFixture();
        switch (defect)
        {
            case "identity-null": fixture.Native.Identity = null; break;
            case "party-null": fixture.Native.PartyRoleIds = null!; break;
            case "closure-entries-null": fixture.Native.Closure!.Entries = null!; break;
            case "static-root-null": fixture.Native.StaticResources = null!; break;
        }
        fixture.Save();
        InvalidDataException? error = null;
        try { NativeFirstMandatoryBattleProfile.Load(fixture.Uri); }
        catch (InvalidDataException caught) { error = caught; }
        AssertThat(error is not null).IsTrue();
        AssertThat(error!.Message.Contains(fixture.Uri, StringComparison.Ordinal)).IsTrue();
    }

    [TestCase]
    public void ResourceBindingWithoutRealResourcesRejectsBeforeAnySideEffect()
    {
        // #53 unit 2 designated RED starting point: a binding that names real modern resources
        // which do not exist must reject with a precise diagnostic; the @09C3 gate stays closed.
        using var fixture = new NativeBattleProfileFixture();
        fixture.Native.EventsIndex = "res://Tests/FirstBattle/Events/index.json";
        fixture.Native.BattleContentUri = "res://Tests/FirstBattle/BattleContent.tres";
        fixture.Native.DialogueDirectory = "res://Tests/FirstBattle/Dialogue";
        fixture.Native.VisualUri = "res://Tests/FirstBattle/Visual.tres";
        // GlobalObjectsUri completes the aea456d five-together binding so the rejection still
        // surfaces the missing-resource diagnostic for the bound real resources below.
        fixture.Native.GlobalObjectsUri = "res://Tests/FirstBattle/GlobalObjects.tres";
        fixture.Native.ImageEnemyId = 97;
        fixture.Native.ImageEnemyArchiveEntry = "ABC97";
        fixture.Native.ImageRoleSpriteIds = ["F0", "F1"];
        fixture.Native.ImagePlayerSpriteUris = ["res://Tests/FirstBattle/F0.tres", "res://Tests/FirstBattle/F1.tres"];
        fixture.Native.ImageBattlefieldId = 24;
        fixture.Native.ImageBattlefieldArchiveEntry = "FBP24";
        fixture.Native.ResourceSha256.Add("res://Tests/FirstBattle/BattleContent.tres", new string('a', 64));
        fixture.Save();
        InvalidDataException? error = null;
        try { NativeFirstMandatoryBattleProfile.Load(fixture.Uri); }
        catch (InvalidDataException caught) { error = caught; }
        AssertThat(error is not null).IsTrue();
        AssertThat(error!.Message.Contains("res://Tests/FirstBattle/BattleContent.tres", StringComparison.Ordinal)).IsTrue();
    }

    [TestCase("events-only")]
    [TestCase("images-only")]
    public void PartialResourceBindingIsRejected(string shape)
    {
        using var fixture = new NativeBattleProfileFixture();
        if (shape == "events-only") fixture.Native.EventsIndex = "res://Tests/FirstBattle/Events/index.json";
        else fixture.Native.ImageEnemyId = 97;
        fixture.Save();
        AssertThrown(() => NativeFirstMandatoryBattleProfile.Load(fixture.Uri));
    }

    [TestCase]
    public void LegacyProfileWithoutBindingLoadsAndKeepsTheTriggerGateClosed()
    {
        using var fixture = new NativeBattleProfileFixture();
        CheckedFirstMandatoryBattleProfile loaded = NativeFirstMandatoryBattleProfile.Load(fixture.Uri);
        AssertThat(loaded.Closure is null).IsTrue();
        AssertThat(loaded.Visual is null).IsTrue();
    }

    [TestCase]
    public void NativeBoundaryApiCannotCarryJsonOrForgeCheckedWrapper()
    {
        string[] propertyNames = typeof(PalFirstMandatoryBattleProfile).GetProperties().Select(x => x.Name).ToArray();
        AssertThat(propertyNames.Contains("IndexBytes")).IsFalse();
        AssertThat(propertyNames.Contains("ContextBytes")).IsFalse();
        Type checkedType = typeof(CheckedFirstMandatoryBattleProfile);
        AssertThat(checkedType.IsSealed).IsTrue();
        AssertThat(checkedType.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Length).IsEqual(0);
        AssertThat(checkedType.GetProperties().All(x => x.SetMethod is null)).IsTrue();
    }

    [TestCase]
    public void NativeProfileRejectsWrongNativeType()
    {
        string root = "res://Tests/RuntimeBattleProfiles/" + Guid.NewGuid().ToString("N");
        string uri = root + "/Wrong.tres";
        System.IO.Directory.CreateDirectory(ProjectSettings.GlobalizePath(root));
        try
        {
            NativeProfileFixture.SaveResource(new Pal.Game.Resources.PalExplorationProfile(), uri);
            AssertThrown(() => NativeFirstMandatoryBattleProfile.Load(uri));
        }
        finally { System.IO.Directory.Delete(ProjectSettings.GlobalizePath(root), true); }
    }

    [TestCase("res://Tests/MissingBattle.tres")]
    [TestCase("C:/Battle.tres")]
    [TestCase("res://Tests/../Battle.tres")]
    public void NativeProfileRejectsMissingOrIllegalUri(string uri) =>
        AssertThrown(() => NativeFirstMandatoryBattleProfile.Load(uri));

    [TestCase]
    public void M4ArgumentsAreIndependentAndStrict()
    {
        var valid = GameSceneConfiguration.FromArguments("--m4-first-battle-profile", "res://Tests/Battle.tres",
            "--m4-first-battle-language", "en");
        AssertThat(valid.BattleProfile).IsEqual("res://Tests/Battle.tres");
        AssertThat(valid.Language).IsEqual("en");
        AssertThrown(() => GameSceneConfiguration.FromArguments("--m4-first-battle-profile", "res://Tests/Battle.tres", "--m2-profile", "res://Tests/M2.tres"));
        AssertThrown(() => GameSceneConfiguration.FromArguments("--m4-first-battle-profile"));
        AssertThrown(() => GameSceneConfiguration.FromArguments("--m4-first-battle-profile", "res://Tests/Battle.tres", "--m4-first-battle-profile", "res://Tests/Battle2.tres"));
        AssertThrown(() => GameSceneConfiguration.FromArguments("--m4-first-battle-profile", "res://Tests/Battle.tres", "--m4-first-battle-language", "fr"));
    }

    [TestCase]
    public void DirectPrepareRejectsNonemptyOutputWithoutPublishingOrChangingSources()
    {
        using var fixture = new NativeBattleProfileFixture();
        fixture.AuthorPreparationSources();
        Directory.CreateDirectory(fixture.PreparationOutput);
        string sentinel = Path.Combine(fixture.PreparationOutput, "keep.txt");
        File.WriteAllText(sentinel, "existing output must survive");
        InvalidDataException? error = null;
        try
        {
            Editor.PrepareBattleContext.Prepare(fixture.PreparationInput, fixture.PreparationOutput,
                fixture.Static.ResourceRoot, fixture.PreparationStaticProfile);
        }
        catch (InvalidDataException caught) { error = caught; }
        AssertThat(error is not null).OverrideFailureMessage("Direct Prepare must reject nonempty output even without Battle/Profile.tres").IsTrue();
        AssertThat(error!.Message.Contains("new or empty", StringComparison.Ordinal)).IsTrue();
        AssertThat(string.Join("|", Directory.GetFileSystemEntries(fixture.PreparationOutput))).IsEqual(sentinel);
        AssertThat(File.ReadAllText(sentinel)).IsEqual("existing output must survive");
        foreach (var source in fixture.PreparationSourceHashes)
            AssertThat(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source.Key))))
                .IsEqual(source.Value);
    }

    [TestCase("extra-file")]
    [TestCase("nested-extra-file")]
    [TestCase("project-input")]
    [TestCase("same-path")]
    [TestCase("input-inside-output")]
    [TestCase("output-inside-input")]
    [TestCase("output-file")]
    public void DirectPrepareRejectsUnsafePathsWithoutChangingExistingFiles(string defect)
    {
        using var fixture = new NativeBattleProfileFixture();
        fixture.AuthorPreparationSources();
        string input = fixture.PreparationInput, output = fixture.PreparationOutput;
        string diagnostic;
        switch (defect)
        {
            case "extra-file":
                File.WriteAllText(Path.Combine(input, "extra.txt"), "keep extra input");
                diagnostic = "exactly index.json and battle-context.json";
                break;
            case "nested-extra-file":
                Directory.CreateDirectory(Path.Combine(input, "nested"));
                File.WriteAllText(Path.Combine(input, "nested", "extra.txt"), "keep nested input");
                diagnostic = "exactly index.json and battle-context.json";
                break;
            case "project-input": input = ProjectSettings.GlobalizePath(fixture.Root + "/Input"); diagnostic = "outside"; break;
            case "same-path": input = output; diagnostic = "overlap"; break;
            case "input-inside-output": input = Path.Combine(output, "Input"); diagnostic = "overlap"; break;
            case "output-inside-input": input = output; output = Path.Combine(input, "Output"); diagnostic = "overlap"; break;
            default:
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                File.WriteAllText(output, "keep output file");
                diagnostic = "existing file";
                break;
        }
        if (input != fixture.PreparationInput)
        {
            Directory.CreateDirectory(input);
            foreach (string file in Directory.GetFiles(fixture.PreparationInput))
                File.Copy(file, Path.Combine(input, Path.GetFileName(file)));
        }
        string inputBefore = Snapshot(input), staticBefore = Snapshot(ProjectSettings.GlobalizePath(fixture.Static.ResourceRoot));
        string outputBefore = Snapshot(fixture.PreparationOutput);
        InvalidDataException? error = null;
        try { Editor.PrepareBattleContext.Prepare(input, output, fixture.Static.ResourceRoot, fixture.PreparationStaticProfile); }
        catch (InvalidDataException caught) { error = caught; }
        AssertThat(error is not null).IsTrue();
        AssertThat(error!.Message.Contains(diagnostic, StringComparison.Ordinal))
            .OverrideFailureMessage($"Expected '{diagnostic}' for {defect}, got: {error.Message}").IsTrue();
        AssertThat(Snapshot(input)).IsEqual(inputBefore);
        AssertThat(Snapshot(ProjectSettings.GlobalizePath(fixture.Static.ResourceRoot))).IsEqual(staticBefore);
        AssertThat(Snapshot(fixture.PreparationOutput)).IsEqual(outputBefore);
    }

    [TestCase("input-ancestor")]
    [TestCase("input-descendant")]
    [TestCase("output-link")]
    [TestCase("output-ancestor")]
    [TestCase("dangling-output-ancestor")]
    public void DirectPrepareRejectsLinksIncludingMissingOutputDescendants(string defect)
    {
        using var fixture = new NativeBattleProfileFixture();
        fixture.AuthorPreparationSources();
        string input = fixture.PreparationInput, output = fixture.PreparationOutput;
        string target = ProjectSettings.GlobalizePath(fixture.Root + "/LinkTarget");
        Directory.CreateDirectory(target);
        string link = defect == "input-descendant" ? Path.Combine(input, "linked")
            : defect.StartsWith("input", StringComparison.Ordinal) ? input + "-link" : output;
        string inputBefore = Snapshot(input), staticBefore = Snapshot(ProjectSettings.GlobalizePath(fixture.Static.ResourceRoot));
        string targetBefore = Snapshot(target);
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        CreateDirectoryLink(link, defect == "input-ancestor" ? input : target);
        try
        {
            if (defect == "input-ancestor") input = link;
            if (defect.EndsWith("ancestor", StringComparison.Ordinal) && defect != "input-ancestor")
                output = Path.Combine(link, "missing", "output");
            if (defect == "dangling-output-ancestor") Directory.Delete(target);
            InvalidDataException? error = null;
            try { Editor.PrepareBattleContext.Prepare(input, output, fixture.Static.ResourceRoot, fixture.PreparationStaticProfile); }
            catch (InvalidDataException caught) { error = caught; }
            AssertThat(error is not null).IsTrue();
            AssertThat(error!.Message.Contains("link/reparse", StringComparison.Ordinal))
                .OverrideFailureMessage($"Expected link/reparse rejection for {defect}, got: {error.Message}").IsTrue();
            AssertThat((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0).IsTrue();
            AssertThat(Snapshot(ProjectSettings.GlobalizePath(fixture.Static.ResourceRoot))).IsEqual(staticBefore);
            AssertThat(Snapshot(target)).IsEqual(defect == "dangling-output-ancestor" ? "missing" : targetBefore);
            if (defect.StartsWith("input", StringComparison.Ordinal)) AssertThat(Directory.Exists(output)).IsFalse();
        }
        finally { Directory.Delete(link); }
        AssertThat(Snapshot(fixture.PreparationInput)).IsEqual(inputBefore);
        AssertThat(Directory.Exists(fixture.PreparationOutput)).IsFalse();
    }

    [TestCase("new")]
    [TestCase("empty")]
    [TestCase("missing-ancestors")]
    public void DirectPrepareAcceptsNewOrEmptyOutputAndEmptyInputSubdirectories(string outputKind)
    {
        using var fixture = new NativeBattleProfileFixture();
        fixture.AuthorPreparationSources();
        Directory.CreateDirectory(Path.Combine(fixture.PreparationInput, "empty", "nested"));
        string output = fixture.PreparationOutput;
        if (outputKind == "empty") Directory.CreateDirectory(output);
        if (outputKind == "missing-ancestors") output = Path.Combine(output, "new", "nested");
        string inputBefore = Snapshot(fixture.PreparationInput), staticBefore = Snapshot(ProjectSettings.GlobalizePath(fixture.Static.ResourceRoot));
        string uri = Editor.PrepareBattleContext.Prepare(fixture.PreparationInput, output,
            fixture.Static.ResourceRoot, fixture.PreparationStaticProfile);
        AssertThat(uri).IsEqual(ProjectSettings.LocalizePath(output).TrimEnd('/') + "/Battle/Profile.tres");
        AssertThat(File.Exists(ProjectSettings.GlobalizePath(uri))).IsTrue();
        AssertThat(Directory.GetFiles(output, "*", SearchOption.AllDirectories).Length).IsEqual(1);
        AssertThat(NativeFirstMandatoryBattleProfile.Load(uri).Profile.ContextSha256).IsEqual(fixture.Core.ContextSha256);
        AssertThat(Snapshot(fixture.PreparationInput)).IsEqual(inputBefore);
        AssertThat(Snapshot(ProjectSettings.GlobalizePath(fixture.Static.ResourceRoot))).IsEqual(staticBefore);
    }

    private static void CreateDirectoryLink(string link, string target)
    {
        // Same non-admin Windows junction mechanism as PreparePathSafetyTests.
        if (!OperatingSystem.IsWindows()) { Directory.CreateSymbolicLink(link, target); return; }
        var start = new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        };
        foreach (string argument in new[] { "/c", "mklink", "/J", Path.GetFullPath(link), Path.GetFullPath(target) })
            start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start)!;
        string stdout = process.StandardOutput.ReadToEnd(), stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        AssertThat(process.ExitCode).OverrideFailureMessage(stdout + stderr).IsEqual(0);
    }

    private static string Snapshot(string root)
    {
        if (File.Exists(root)) return "file:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(root)));
        if (!Directory.Exists(root)) return "missing";
        return "directory:" + string.Join("|", Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal).Select(path => Path.GetRelativePath(root, path) + ":" +
                (Directory.Exists(path) ? "directory" : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))))));
    }

    private static void AssertThrown(Action action)
    {
        bool rejected = false;
        try { action(); }
        catch (InvalidDataException) { rejected = true; }
        AssertThat(rejected).IsTrue();
    }
}
