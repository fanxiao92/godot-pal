using System;
using System.IO;
using System.Linq;
using Pal.Game.Resources;
using GdUnit4;
using Pal.Core;
using Pal.Game.Profiles;
using static GdUnit4.Assertions;

namespace Pal.Game.Tests;

[TestSuite, RequireGodotRuntime, GodotExceptionMonitor]
public class NativeExplorationProfileTests
{
    [TestCase("version")]
    [TestCase("scene")]
    [TestCase("bundle")]
    [TestCase("start")]
    [TestCase("x")]
    [TestCase("y")]
    [TestCase("direction")]
    [TestCase("layer")]
    [TestCase("phase")]
    [TestCase("leader")]
    [TestCase("role")]
    [TestCase("walk")]
    [TestCase("source")]
    [TestCase("archive")]
    [TestCase("sha")]
    [TestCase("chunk")]
    [TestCase("offset")]
    [TestCase("source-role")]
    [TestCase("field")]
    public void MissingOrMismatchedV1FieldsFailInsteadOfUsingZeroDefaults(string kind)
    {
        using var f = new NativeProfileFixture();
        switch (kind)
        {
            case "version": f.Profile.FormatVersion = 2; break;
            case "scene": f.Profile.SceneId = 3; break;
            case "bundle": f.Profile.BundleSha256 = new string('c', 64); break;
            case "start": f.Profile.Start = null!; break;
            case "x": f.Profile.Start.X = int.MinValue; break;
            case "y": f.Profile.Start.Y = int.MinValue; break;
            case "direction": f.Profile.Start.Direction = -1; break;
            case "layer": f.Profile.Start.Layer = -1; break;
            case "phase": f.Profile.Start.StepPhase = -1; break;
            case "leader": f.Profile.Leader = null!; break;
            case "role": f.Profile.Leader.RoleId = -1; break;
            case "walk": f.Profile.Leader.WalkFramesWord = -1; break;
            case "source": f.Profile.Leader.WalkFramesSource = null!; break;
            case "archive": f.Profile.Leader.WalkFramesSource.Archive = "other.mkf"; break;
            case "sha": f.Profile.Leader.WalkFramesSource.Sha256 = new string('c', 64); break;
            case "chunk": f.Profile.Leader.WalkFramesSource.Chunk = -1; break;
            case "offset": f.Profile.Leader.WalkFramesSource.ByteOffset = -1; break;
            case "source-role": f.Profile.Leader.WalkFramesSource.RoleId = -1; break;
            case "field": f.Profile.Leader.WalkFramesSource.Field = ""; break;
        }
        var scene = f.Save();
        ExpectInvalid(() => NativeExplorationProfile.Load(f.Uri, scene));
    }

    [TestCase("external")]
    [TestCase("relative")]
    [TestCase("user")]
    [TestCase("traversal")]
    [TestCase("backslash")]
    [TestCase("empty-segment")]
    [TestCase("encoded")]
    [TestCase("query")]
    [TestCase("whitespace")]
    [TestCase("trailing-dot")]
    [TestCase("missing")]
    [TestCase("wrong-type")]
    public void OnlyCanonicalExistingProjectProfileResourcesAreAccepted(string kind)
    {
        using var f = new NativeProfileFixture();
        var scene = f.Save();
        string uri = kind switch
        {
            "external" => Godot.ProjectSettings.GlobalizePath(f.Uri),
            "relative" => f.Uri[6..],
            "user" => "user://Profile.tres",
            "traversal" => f.Root + "/../" + f.Root.Split('/').Last() + "/Profile.tres",
            "backslash" => f.Root + "\\Profile.tres",
            "empty-segment" => f.Root + "//Profile.tres",
            "encoded" => f.Root + "/%50rofile.tres",
            "query" => f.Uri + "?x=1",
            "whitespace" => f.Root + "/ Profile.tres",
            "trailing-dot" => f.Uri + ".",
            "missing" => f.Root + "/Missing.tres",
            _ => f.Root + "/Wrong.tres"
        };
        if (kind == "wrong-type") NativeProfileFixture.SaveResource(new Godot.Resource(), uri);
        ExpectInvalid(() => NativeExplorationProfile.Load(uri, scene), kind == "wrong-type" ? "PalExplorationProfile" : "res://");
    }

    [TestCase("party-absent")]
    [TestCase("party-duplicate")]
    [TestCase("party-role")]
    [TestCase("party-id")]
    [TestCase("party-source")]
    [TestCase("party-source-type")]
    [TestCase("sprite-id")]
    [TestCase("sprite-missing")]
    [TestCase("sprite-duplicate")]
    [TestCase("sprite-source")]
    [TestCase("frame-index")]
    [TestCase("frame-dimensions")]
    [TestCase("frame-count")]
    [TestCase("frame-nominal-hole")]
    [TestCase("frame-offset")]
    [TestCase("sprite-length")]
    [TestCase("object-word")]
    [TestCase("object-signed")]
    [TestCase("object-script")]
    [TestCase("object-id")]
    [TestCase("object-ref")]
    [TestCase("object-order")]
    [TestCase("scene-script")]
    public void NativeMetadataIsValidatedBeforeNarrowingOrFrameSelection(string kind)
    {
        using var f = new NativeProfileFixture();
        var obj = f.AddObject();
        switch (kind)
        {
            case "party-absent": f.Scene.DefaultParty.Clear(); break;
            case "party-duplicate": f.Scene.DefaultParty.Add(f.Scene.DefaultParty[0]); break;
            case "party-role": f.Scene.DefaultParty[0].RoleId = 1; break;
            case "party-id": f.Scene.DefaultParty[0].WSpriteNum = 18; break;
            case "party-source": f.Scene.DefaultParty[0].Source["spriteByteOffset"] = 26; break;
            case "party-source-type": f.Scene.DefaultParty[0].Source["chunk"] = "3"; break;
            case "sprite-id": f.Scene.Sprites[0].SpriteId = 65536; break;
            case "sprite-missing": f.Scene.Sprites.Clear(); break;
            case "sprite-duplicate": f.Scene.Sprites.Add(f.Scene.Sprites[0]); break;
            case "sprite-source": f.Scene.Sprites[0].Source["chunk"] = 18; break;
            case "frame-index": f.Scene.Sprites[0].Frames[0].Index = 65536; break;
            case "frame-dimensions": f.Scene.Sprites[0].Frames[0].Width = 12; break;
            case "frame-count": f.Scene.Sprites[0].FrameCount = 0; break;
            case "frame-nominal-hole": f.Scene.Sprites[0].FrameCount = 2; break;
            case "frame-offset": f.Scene.Sprites[0].Frames[0].SourceOffset = -1; break;
            case "sprite-length": f.Scene.Sprites[0].DecodedByteLength = 0; break;
            case "object-word": obj.X = 65536; break;
            case "object-signed": obj.SLayer = 32768; break;
            case "object-script": obj.WAutoScript = -1; break;
            case "object-id": obj.Id = 0; break;
            case "object-ref": obj.WSpriteNum = 0; break;
            case "object-order": f.Scene.WEventObjectIndex = 3; f.Scene.NextEventObjectIndex = 4; break;
            case "scene-script": f.Scene.WScriptOnEnter = 65536; break;
        }
        var scene = f.Save();
        ExpectInvalid(() => NativeExplorationProfile.Load(f.Uri, scene));
    }

    [TestCase]
    public void AliasFrameIndicesAndAllOriginalObjectsArePreservedWithoutRunningScripts()
    {
        using var f = new NativeProfileFixture();
        f.AddAliasFrame(6); f.Profile.Start.Direction = 2; f.Profile.Start.StepPhase = 3;
        f.Scene.WEventObjectIndex = 4;
        var hidden = f.AddObject(5);
        hidden.X = 65535; hidden.Y = 65534; hidden.SLayer = -32768; hidden.SState = -32768;
        hidden.SVanishTime = 32767; hidden.WDirection = 65535; hidden.WCurrentFrameNum = 65534;
        hidden.WTriggerScript = 65000; hidden.NScriptIdleFrame = 65535;
        // Derived hints deliberately disagree: neither is a rules input.
        hidden.InitiallyVisible = true;
        var visible = f.AddObject(6);
        f.AddAliasFrame(2);
        visible.X = 336; visible.Y = 320; visible.WAutoScript = 1234; visible.NSpriteFramesAuto = 9;
        visible.NSpriteFrames = 3; visible.WCurrentFrameNum = 3; visible.InitiallyVisible = false;
        var loaded = f.Save();
        var result = NativeExplorationProfile.Load(f.Uri, loaded);
        AssertThat(result.Plan.LeaderFrameIndex).IsEqual(6); // alias6 >= nominal FrameCount1
        AssertThat(result.Plan.StepPhase).IsEqual(3);
        AssertThat(result.Rules.Objects.Select(o => (int)o.Id).ToArray()).IsEqual(new[] { 5, 6 });
        AssertThat((int)result.Rules.Objects[0].X).IsEqual(65535);
        AssertThat((int)result.Rules.Objects[0].State).IsEqual(-32768);
        AssertThat((int)result.Rules.Objects[0].Layer).IsEqual(-32768);
        AssertThat(result.Plan.Commands.Single(c => c.Source == SceneDrawSource.Object).FrameIndex).IsEqual(2);
        AssertThat(loaded.Objects[0].WTriggerScript).IsEqual(65000);
        AssertThat(loaded.Objects[0].NScriptIdleFrame).IsEqual(65535);
        AssertThat(loaded.Objects[1].WAutoScript).IsEqual(1234);
        AssertThat(loaded.Objects[1].NSpriteFramesAuto).IsEqual(9);
        AssertThat(loaded.Objects[1].WCurrentFrameNum).IsEqual(3);
        AssertThat(loaded.Objects[1].InitialFrameIndex).IsEqual(0);
        AssertThat(loaded.Objects[1].InitiallyVisible).IsFalse();
        AssertThat(loaded.Objects[0].SVanishTime).IsEqual(32767);
    }

    [TestCase("map")]
    [TestCase("object")]
    [TestCase("range")]
    [TestCase("trapped")]
    public void StartMustBeClearAndHaveALegalNormalNeighborWithoutAutomaticRelocation(string kind)
    {
        using var f = new NativeProfileFixture();
        if (kind == "map") { f.Scene.Map.RawCells[2580] |= 0x2000; f.Scene.Map.Blocked[2580] = 1; }
        if (kind == "range") f.Profile.Start.X = 159;
        if (kind == "object") Blocker(f, 1, 320, 320);
        if (kind == "trapped")
        {
            Blocker(f, 1, 304, 328); Blocker(f, 2, 304, 312);
            Blocker(f, 3, 336, 312); Blocker(f, 4, 336, 328);
        }
        var scene = f.Save();
        ExpectInvalid(() => NativeExplorationProfile.Load(f.Uri, scene), kind == "trapped" ? "neighbor" : "start");
        AssertThat(f.Profile.Start.X).IsEqual(kind == "range" ? 159 : 320);
        AssertThat(f.Profile.Start.Y).IsEqual(320);
    }

    [TestCase]
    public void Scene4StaticStartKeepsIndependentIdentityCoordinatesAndReferenceNeighborOrder()
    {
        using var f = new NativeProfileFixture();
        f.Scene.SceneId = 4;
        f.Scene.MapId = f.Scene.Map.MapId = f.Scene.Map.Tiles.MapId = 10;
        f.Profile.SceneId = 4;
        f.Profile.Start.X = 1232; f.Profile.Start.Y = 1336;
        f.Profile.Start.Direction = 0; f.Profile.Start.Layer = 0; f.Profile.Start.StepPhase = 0;
        var result = NativeExplorationProfile.Load(f.Uri, f.Save());
        AssertThat(result.Leader.X).IsEqual((short)1232);
        AssertThat(result.Leader.Y).IsEqual((short)1336);
        AssertThat(result.Leader.Direction).IsEqual(0);
        AssertThat(result.Leader.Layer).IsEqual((ushort)0);
        AssertThat(result.Leader.StepPhase).IsEqual(0);
        AssertThat(result.Plan.CameraX).IsEqual(1072);
        AssertThat(result.Plan.CameraY).IsEqual(1224);
        AssertThat(result.Rules.IsBlocked(1232, 1336)).IsFalse();
        AssertThat(result.Neighbors.ToArray()).IsEqual(new[] {
            new StartNeighbor(0, 1216, 1344, false), new StartNeighbor(1, 1216, 1328, false),
            new StartNeighbor(2, 1248, 1328, false), new StartNeighbor(3, 1248, 1344, false) });
    }

    [TestCase]
    public void CheckedStartReportsFourCoreCollisionFactsInReferenceDirectionOrder()
    {
        using var f = new NativeProfileFixture();
        Blocker(f, 1, 304, 328);
        var result = NativeExplorationProfile.Load(f.Uri, f.Save());
        AssertThat(result.Neighbors.ToArray()).IsEqual(new[] {
            new StartNeighbor(0, 304, 328, true), new StartNeighbor(1, 304, 312, false),
            new StartNeighbor(2, 336, 312, false), new StartNeighbor(3, 336, 328, false) });
        AssertThat(result.Rules.IsBlocked(304, 328)).IsTrue();
        AssertThat((int)result.Leader.X).IsEqual(320);
        AssertThat((int)result.Leader.Y).IsEqual(320);
    }

    [TestCase("leader")]
    [TestCase("object")]
    public void RequiredCurrentFramesFailExplicitlyThroughThePublicProfileSeam(string kind)
    {
        using var f = new NativeProfileFixture();
        if (kind == "leader") f.Profile.Start.Direction = 1; // raw0 requires actual index3, not array position
        else
        {
            var obj = f.AddObject(); obj.WCurrentFrameNum = 1;
            // Keep the optional imported hint valid: selection must use the raw WORD current value.
            obj.InitialFrameIndex = 0;
        }
        var scene = f.Save();
        ExpectInvalid(() => NativeExplorationProfile.Load(f.Uri, scene), "missing frame");
    }

    [TestCase]
    public void FinalCommandsResolveActualNativeTileAndAliasTexturesWithoutAnotherCoordinateTransform()
    {
        using var f = new NativeProfileFixture();
        f.AddAliasFrame(9); f.Profile.Start.Direction = 3;
        var result = NativeExplorationProfile.Load(f.Uri, f.Save());
        foreach (var command in result.Plan.Commands)
        {
            var texture = result.TextureFor(command);
            AssertThat(texture.GetWidth()).IsGreater(0);
            if (command.Source == SceneDrawSource.Leader)
            {
                AssertThat(command.FrameIndex).IsEqual(9);
                AssertThat(texture.GetSize()).IsEqual(new Godot.Vector2(11, 19));
                using var image = texture.GetImage();
                AssertThat(image.GetPixel(0, 0)).IsEqual(Godot.Colors.Magenta);
            }
            else AssertThat(texture.GetSize()).IsEqual(new Godot.Vector2(40, 24));
        }
        ExpectInvalid(() => result.TextureFor(new(SceneDrawSource.Leader, 18, 9, 0, 0, 0, 0)), "texture");
        ExpectInvalid(() => result.TextureFor(new(SceneDrawSource.Leader, 17, 8, 0, 0, 0, 0)), "texture");
    }

    [TestCase("RoleId")]
    [TestCase("WalkFramesWord")]
    [TestCase("StepPhase")]
    [TestCase("Direction")]
    [TestCase("Layer")]
    public void OmittedZeroValuedPropertiesInSavedNativeResourcesAreNotAccepted(string property)
    {
        using var f = new NativeProfileFixture();
        var scene = f.Save();
        string path = Godot.ProjectSettings.GlobalizePath(f.Uri);
        string original = File.ReadAllText(path);
        string changed = System.Text.RegularExpressions.Regex.Replace(original, "(?m)^" + property + " = 0\\r?\\n", "");
        AssertThat(changed == original).IsFalse(); // Verify the real serialized field was removed.
        File.WriteAllText(path, changed);
        ExpectInvalid(() => NativeExplorationProfile.Load(f.Uri, scene));
    }

    [TestCase("x")]
    [TestCase("y")]
    [TestCase("direction")]
    [TestCase("phase")]
    [TestCase("layer")]
    [TestCase("walk")]
    [TestCase("bundle-nonhex")]
    [TestCase("source-nonhex")]
    [TestCase("archive-duplicate")]
    [TestCase("archive-type")]
    [TestCase("raw-dword")]
    [TestCase("raw-inconsistent")]
    [TestCase("sprite-null-texture")]
    [TestCase("sprite-duplicate-index")]
    [TestCase("sprite-nominal-count")]
    [TestCase("object-idle")]
    [TestCase("object-initial-frame")]
    public void NumericAndNativeReferenceBoundariesFailRatherThanNarrowOrSubstitute(string kind)
    {
        using var f = new NativeProfileFixture();
        switch (kind)
        {
            case "x": f.Profile.Start.X = 65536; break;
            case "y": f.Profile.Start.Y = -32769; break;
            case "direction": f.Profile.Start.Direction = 4; break;
            case "phase": f.Profile.Start.StepPhase = 4; break;
            case "layer": f.Profile.Start.Layer = 65536; break;
            case "walk": f.Profile.Leader.WalkFramesWord = 65536; break;
            case "bundle-nonhex": f.Profile.BundleSha256 = f.Scene.BundleSha256 = new string('z', 64); break;
            case "source-nonhex": f.Profile.Leader.WalkFramesSource.Sha256 = new string('z', 64); f.Scene.ArchiveSources[0]["sha256"] = new string('z', 64); break;
            case "archive-duplicate": f.Scene.ArchiveSources.Add(f.Scene.ArchiveSources[0]); break;
            case "archive-type": f.Scene.ArchiveSources[0]["sha256"] = 12; break;
            case "raw-dword": f.Scene.Map.RawCells[0] = (long)uint.MaxValue + 1; break;
            case "raw-inconsistent": f.Scene.Map.RawCells[0] = 1; break;
            case "sprite-null-texture": f.Scene.Sprites[0].Frames[0].Texture = null!; break;
            case "sprite-duplicate-index": f.AddAliasFrame(0); break;
            case "sprite-nominal-count": f.Scene.Sprites[0].FrameCount = 4097; break;
            case "object-idle": f.AddObject().WScriptIdleFrameCountAuto = 65536; break;
            case "object-initial-frame": f.AddObject().InitialFrameIndex = 1; break;
        }
        var scene = f.Save();
        ExpectInvalid(() => NativeExplorationProfile.Load(f.Uri, scene));
    }

    [TestCase]
    public void MissingArchiveSourceCollectionFailsWithADataDiagnostic()
    {
        using var f = new NativeProfileFixture();
        var scene = f.Save();
        scene.ArchiveSources = null!;
        ExpectInvalid(() => NativeExplorationProfile.Load(f.Uri, scene), "ArchiveSources");
    }

    [TestCase]
    public void MaximumRawWordAndHighAliasIndexRemainUnnormalized()
    {
        using var f = new NativeProfileFixture();
        f.AddAliasFrame(65533); f.Profile.Start.Direction = 3; f.Profile.Start.Layer = 65535;
        f.Profile.Start.StepPhase = 3; f.Profile.Leader.WalkFramesWord = 65535;
        var result = NativeExplorationProfile.Load(f.Uri, f.Save());
        AssertThat((int)result.Leader.WalkFramesWord).IsEqual(65535);
        AssertThat((int)result.Leader.Layer).IsEqual(65535);
        AssertThat(result.Plan.StepPhase).IsEqual(3);
        AssertThat(result.Plan.LeaderFrameIndex).IsEqual(65533); // reference WORD assignment of 3 * 65535
        AssertThat(result.TextureFor(result.Plan.Commands.Single(c => c.Source == SceneDrawSource.Leader)).GetWidth()).IsEqual(11);
    }

    [TestCase]
    public void RawWordCoordinateIsCanonicalizedByPalPositionSemanticsBeforeCollision()
    {
        using var f = new NativeProfileFixture();
        f.Profile.Start.X = 65535;
        var scene = f.Save();
        ExpectInvalid(() => NativeExplorationProfile.Load(f.Uri, scene), "(-1,320)");
    }

    private static void Blocker(NativeProfileFixture f, int id, int x, int y)
    {
        var obj = f.AddObject(id); obj.X = x; obj.Y = y; obj.SState = 2; obj.SVanishTime = 9;
        obj.WSpriteNum = 0; obj.Sprite = null!; obj.InitialFrameIndex = -1; obj.InitiallyVisible = false;
    }

    private static void ExpectInvalid(Action load, string diagnostic = "")
    {
        InvalidDataException? failure = null;
        try { load(); } catch (InvalidDataException e) { failure = e; }
        AssertThat(failure != null).IsTrue();
        if (diagnostic.Length > 0) AssertThat(failure!.Message.Contains(diagnostic, StringComparison.OrdinalIgnoreCase)).IsTrue();
    }

    [TestCase]
    public void NativeRoundtripUsesUniqueRoleZeroSpriteAndRawWordZeroWithoutAdvancingPhase()
    {
        using var fixture = new NativeProfileFixture();
        var result = NativeExplorationProfile.Load(fixture.Uri, fixture.Save());
        AssertThat(result.Leader.Sprite.SpriteId).IsEqual(17);
        AssertThat((int)result.Leader.WalkFramesWord).IsEqual(0);
        AssertThat(result.Plan.LeaderFrameIndex).IsEqual(0);
        AssertThat(result.Plan.StepPhase).IsEqual(0);
        AssertThat(result.Plan.CameraX).IsEqual(160);
        AssertThat(result.Plan.CameraY).IsEqual(208);
        var leader = result.Plan.Commands.Single(c => c.Source == SceneDrawSource.Leader);
        AssertThat(leader.Left).IsEqual(155);
        AssertThat(leader.Top).IsEqual(97);
    }
}
