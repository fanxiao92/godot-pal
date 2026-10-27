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

    private static void AssertThrown(Action action)
    {
        bool rejected = false;
        try { action(); }
        catch (InvalidDataException) { rejected = true; }
        AssertThat(rejected).IsTrue();
    }
}
