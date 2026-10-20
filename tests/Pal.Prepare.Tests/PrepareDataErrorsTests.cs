using System.Text.Json.Nodes;
using Pal.TestFixtures;
using Xunit;

namespace Pal.Prepare.Tests;

public sealed partial class PrepareProcessTests
{
    [Theory]
    [InlineData("manifest-version", "formatVersion")]
    [InlineData("map-version", "formatVersion")]
    [InlineData("sprite-version", "formatVersion")]
    [InlineData("sha256", "SHA-256")]
    [InlineData("length", "byteLength")]
    [InlineData("bundle", "bundleSha256")]
    [InlineData("sources", "source identity")]
    [InlineData("duplicate-artifact", "unique")]
    [InlineData("unlisted-file", "file set")]
    [InlineData("missing-file", "missing artifact")]
    [InlineData("missing-sprite", "Unlisted dependency")]
    [InlineData("scene-id", "ID mismatch")]
    [InlineData("tiles-id", "ID mismatch")]
    [InlineData("sprite-id", "ID/length")]
    [InlineData("palette-id", "palette ID")]
    [InlineData("map-size", "dimensions")]
    [InlineData("raw-cell", "raw height/blocked")]
    [InlineData("missing-tile-frame", "missing tile frame")]
    [InlineData("selected-frame", "initial frame")]
    [InlineData("nominal-frame", "nominal frames")]
    [InlineData("duplicate-frame", "duplicate/invalid")]
    [InlineData("frame-planes", "incomplete frame planes")]
    [InlineData("png-pixels", "PNG RGBA")]
    [InlineData("hidden-dependency", "sprite IDs")]
    [InlineData("role-reference", "sprite dependency")]
    [InlineData("relative-sprite-reference", "sprite dependency")]
    [InlineData("absolute-frame-reference", "root-relative PNG")]
    [InlineData("traversal-artifact", "root-relative path")]
    [InlineData("backslash-artifact", "root-relative path")]
    [InlineData("rooted-artifact", "root-relative path")]
    [InlineData("uri-artifact", "root-relative path")]
    [InlineData("truncated-png", "PNG decode failed")]
    [InlineData("corrupt-preview", "PNG decode failed")]
    [InlineData("unreferenced-corrupt-png", "PNG decode failed")]
    public async Task Invalid_complete_package_is_rejected_by_public_prepare_before_output(string defect, string diagnostic)
    {
        void Change(string path, Action<JsonNode> mutate, bool rehash = true) => M1Bundle.Mutate(Input, path, mutate, rehash);
        switch (defect)
        {
            case "manifest-version": Change("manifest.json", n => n["formatVersion"] = 2, false); break;
            case "map-version": Change("map.json", n => n["formatVersion"] = 2); break;
            case "sprite-version": Change("sprites/9.json", n => n["formatVersion"] = 2); break;
            case "sha256": File.AppendAllText(Path.Combine(Input, "scene.json"), " "); Change("manifest.json", n => n["artifacts"]!.AsArray().First(a => a!["path"]!.GetValue<string>() == "scene.json")!["byteLength"] = new FileInfo(Path.Combine(Input, "scene.json")).Length, false); break;
            case "length": Change("manifest.json", n => n["artifacts"]![0]!["byteLength"] = 1, false); break;
            case "bundle": Change("manifest.json", n => n["bundleSha256"] = new string('0', 64), false); break;
            case "sources": Change("manifest.json", n => n["sources"]![0]!["sha256"] = "bad", false); break;
            case "duplicate-artifact": Change("manifest.json", n => n["artifacts"]!.AsArray().Insert(0, n["artifacts"]![0]!.DeepClone()), false); break;
            case "unlisted-file": File.WriteAllText(Path.Combine(Input, "extra.txt"), "unlisted"); break;
            case "missing-file": File.Delete(Path.Combine(Input, "tiles", "0.png")); break;
            case "missing-sprite": File.Delete(Path.Combine(Input, "sprites", "9.json")); M1Bundle.Rehash(Input); break;
            case "scene-id": Change("scene.json", n => n["sceneId"] = 3); break;
            case "tiles-id": Change("tiles.json", n => n["mapId"] = 13); break;
            case "sprite-id": Change("sprites/9.json", n => n["spriteId"] = 8); break;
            case "palette-id": Change("palette.json", n => n["paletteId"] = 1); break;
            case "map-size": Change("map.json", n => n["width"] = 2); break;
            case "raw-cell": Change("map.json", n => n["cells"]![0]!["blocked"] = false); break;
            case "missing-tile-frame": Change("tiles.json", n => n["frames"]!.AsArray().RemoveAt(1)); break;
            case "selected-frame": Change("scene.json", n => { n["objects"]![0]!["wCurrentFrameNum"] = 1; n["objects"]![0]!["initialFrameIndex"] = 3; }); break;
            case "nominal-frame": Change("sprites/9.json", n => n["frameCount"] = 2); break;
            case "duplicate-frame": Change("tiles.json", n => n["frames"]!.AsArray().Add(n["frames"]![0]!.DeepClone())); break;
            case "frame-planes": Change("tiles.json", n => n["frames"]![0]!["alpha"] = "AA=="); break;
            case "png-pixels": Change("tiles.json", n => n["frames"]![0]!["indices"] = "AQEBAQ=="); break;
            case "hidden-dependency": Change("scene.json", n => n["spriteIds"]!.AsArray().RemoveAt(1)); break;
            case "role-reference": Change("scene.json", n => n["defaultParty"]![0]!["sprite"] = "sprites/9.json"); break;
            case "relative-sprite-reference": Change("scene.json", n => n["objects"]![0]!["sprite"] = "9.json"); break;
            case "absolute-frame-reference": Change("sprites/9.json", n => n["frames"]![0]!["png"] = Path.Combine(Input, "sprites", "9", "0.png")); break;
            case "traversal-artifact": Change("manifest.json", n => n["artifacts"]![0]!["path"] = "../escape.json", false); break;
            case "backslash-artifact": Change("manifest.json", n => n["artifacts"]![0]!["path"] = "bad\\path.json", false); break;
            case "rooted-artifact": Change("manifest.json", n => n["artifacts"]![0]!["path"] = "/escape.json", false); break;
            case "uri-artifact": Change("manifest.json", n => n["artifacts"]![0]!["path"] = "C:/escape.json", false); break;
            case "truncated-png": Corrupt("sprites/9/0.png"); break;
            case "corrupt-preview": Corrupt("preview.png"); break;
            case "unreferenced-corrupt-png": File.Copy(Path.Combine(Input, "tiles", "0.png"), Path.Combine(Input, "extra.png")); Corrupt("extra.png"); break;
            default: throw new InvalidOperationException(defect);
        }
        var before = Snapshot();
        var result = await Prepare();
        Assert.True(result.ExitCode == 1, result.Text);
        Assert.Contains(diagnostic, result.Text);
        Assert.DoesNotContain("Prepared native Godot scene", result.Text);
        Assert.False(Directory.Exists(output));
        Assert.Equal(before, Snapshot());
    }
    private void Corrupt(string relative)
    {
        // Keep valid signature/IHDR/dimensions, remove IDAT stream: must reach real PNG decoding.
        string path = Path.Combine(Input, relative);
        File.WriteAllBytes(path, File.ReadAllBytes(path)[..33]);
        M1Bundle.Rehash(Input);
    }
}
