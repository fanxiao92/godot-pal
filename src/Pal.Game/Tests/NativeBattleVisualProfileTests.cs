using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using GdUnit4;
using Godot;
using Pal.Game.Profiles;
using Pal.Game.Resources;
using static GdUnit4.Assertions;

namespace Pal.Game.Tests;

[TestSuite, RequireGodotRuntime]
public class NativeBattleVisualProfileTests
{
    [TestCase]
    public void StandaloneCheckedSpriteRejectsChangedSourceAfterCleanRoundtrip()
    {
        string root = "res://Assets/RuntimeTests/checked-sprite-" + Guid.NewGuid().ToString("N");
        string directory = ProjectSettings.GlobalizePath(root);
        string uri = root + "/Sprite.tres";
        try
        {
            Directory.CreateDirectory(directory);
            var authored = new PalSprite
            {
                SpriteId = 73, DecodedByteLength = 64, FrameCount = 2,
                Source = new Godot.Collections.Dictionary { ["archive"] = "literal-battle.mkf", ["chunk"] = 73 },
                Frames =
                [
                    new PalFrame { Index = 0, SourceOffset = 6, Width = 2, Height = 1, Indices = [4, 19], Alpha = [0, 255] },
                    new PalFrame { Index = 1, SourceOffset = 18, Width = 1, Height = 2, Indices = [22, 79], Alpha = [128, 17] }
                ]
            };
            // Independently authored expectation, never derived from the loaded target or its fields.
            var expected = new PalSprite
            {
                SpriteId = 73, DecodedByteLength = 64, FrameCount = 2,
                Source = new Godot.Collections.Dictionary { ["archive"] = "literal-battle.mkf", ["chunk"] = 73 },
                Frames =
                [
                    new PalFrame { Index = 0, SourceOffset = 6, Width = 2, Height = 1, Indices = [4, 19], Alpha = [0, 255] },
                    new PalFrame { Index = 1, SourceOffset = 18, Width = 1, Height = 2, Indices = [22, 79], Alpha = [128, 17] }
                ]
            };
            NativeProfileFixture.SaveResource(authored, uri);
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal) { [uri] = Godot.FileAccess.GetSha256(uri) };
            var clean = NativeBattleVisualProfile.LoadSprite(uri, expected, hashes);
            AssertThat(ReferenceEquals(clean, authored)).IsFalse();
            AssertThat(clean.SpriteId).IsEqual(73);
            AssertThat(clean.DecodedByteLength).IsEqual(64);
            AssertThat(clean.FrameCount).IsEqual(2);
            AssertThat(clean.Frames.Count).IsEqual(2);
            AssertThat(clean.Frames.Select(frame => frame.Index).Order().SequenceEqual(new[] { 0, 1 })).IsTrue();
            var first = clean.Frames.Single(frame => frame.Index == 0);
            AssertThat(first.SourceOffset).IsEqual(6);
            AssertThat(first.Width).IsEqual(2);
            AssertThat(first.Height).IsEqual(1);
            AssertThat(first.Indices.SequenceEqual(new byte[] { 4, 19 })).IsTrue();
            AssertThat(first.Alpha.SequenceEqual(new byte[] { 0, 255 })).IsTrue();
            AssertThat(first.Texture is null).IsTrue();
            var second = clean.Frames.Single(frame => frame.Index == 1);
            AssertThat(second.SourceOffset).IsEqual(18);
            AssertThat(second.Width).IsEqual(1);
            AssertThat(second.Height).IsEqual(2);
            AssertThat(second.Indices.SequenceEqual(new byte[] { 22, 79 })).IsTrue();
            AssertThat(second.Alpha.SequenceEqual(new byte[] { 128, 17 })).IsTrue();
            AssertThat(second.Texture is null).IsTrue();
            AssertThat(clean.Source.Count).IsEqual(2);
            AssertThat(clean.Source["archive"].VariantType).IsEqual(Variant.Type.String);
            AssertThat(clean.Source["archive"].AsString()).IsEqual("literal-battle.mkf");
            AssertThat(clean.Source["chunk"].VariantType).IsEqual(Variant.Type.Int);
            AssertThat(clean.Source["chunk"].AsInt32()).IsEqual(73);

            authored.Source["archive"] = "changed-battle.mkf";
            NativeProfileFixture.SaveResource(authored, uri);
            // The current bad file has a correct hash: byte identity is not the expected Source declaration.
            hashes[uri] = Godot.FileAccess.GetSha256(uri);
            string failure = Failure(() => NativeBattleVisualProfile.LoadSprite(uri, expected, hashes));
            AssertThat(failure.Contains("Source", StringComparison.Ordinal)).IsTrue();
            AssertThat(failure.Contains(uri, StringComparison.Ordinal)).IsTrue();
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [TestCase]
    public void StandaloneCheckedSpriteRejectsWrongRootHashAfterCleanRoundtrip()
    {
        string root = "res://Assets/RuntimeTests/checked-sprite-root-hash-" + Guid.NewGuid().ToString("N");
        string directory = ProjectSettings.GlobalizePath(root);
        string uri = root + "/Sprite.tres";
        try
        {
            Directory.CreateDirectory(directory);
            // Artificial literal fixture, not retail data; expected owns separate Source, frames and planes.
            var authored = new PalSprite
            {
                SpriteId = 73, DecodedByteLength = 64, FrameCount = 2,
                Source = new Godot.Collections.Dictionary { ["archive"] = "literal-battle.mkf", ["chunk"] = 73 },
                Frames =
                [
                    new PalFrame { Index = 0, SourceOffset = 6, Width = 2, Height = 1, Indices = [4, 19], Alpha = [0, 255] },
                    new PalFrame { Index = 1, SourceOffset = 18, Width = 1, Height = 2, Indices = [22, 79], Alpha = [128, 17] }
                ]
            };
            var expected = new PalSprite
            {
                SpriteId = 73, DecodedByteLength = 64, FrameCount = 2,
                Source = new Godot.Collections.Dictionary { ["archive"] = "literal-battle.mkf", ["chunk"] = 73 },
                Frames =
                [
                    new PalFrame { Index = 0, SourceOffset = 6, Width = 2, Height = 1, Indices = [4, 19], Alpha = [0, 255] },
                    new PalFrame { Index = 1, SourceOffset = 18, Width = 1, Height = 2, Indices = [22, 79], Alpha = [128, 17] }
                ]
            };
            AssertThat(ResourceSaver.Save(authored, uri)).IsEqual(Error.Ok);
            string actualHash = Godot.FileAccess.GetSha256(uri);
            AssertThat(actualHash.Length).IsEqual(64);
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal) { [uri] = actualHash };
            var clean = NativeBattleVisualProfile.LoadSprite(uri, expected, hashes);
            AssertThat(ReferenceEquals(clean, authored)).IsFalse();
            AssertThat(ReferenceEquals(clean, expected)).IsFalse();
            AssertThat(clean.SpriteId).IsEqual(73);
            AssertThat(clean.DecodedByteLength).IsEqual(64);
            AssertThat(clean.FrameCount).IsEqual(2);
            AssertThat(clean.Frames.Count).IsEqual(2);
            AssertThat(clean.Frames.Select(frame => frame.Index).Order().SequenceEqual(new[] { 0, 1 })).IsTrue();
            var first = clean.Frames.Single(frame => frame.Index == 0);
            AssertThat(first.SourceOffset).IsEqual(6);
            AssertThat(first.Width).IsEqual(2);
            AssertThat(first.Height).IsEqual(1);
            AssertThat(first.Indices.SequenceEqual(new byte[] { 4, 19 })).IsTrue();
            AssertThat(first.Alpha.SequenceEqual(new byte[] { 0, 255 })).IsTrue();
            AssertThat(first.Texture is null).IsTrue();
            var second = clean.Frames.Single(frame => frame.Index == 1);
            AssertThat(second.SourceOffset).IsEqual(18);
            AssertThat(second.Width).IsEqual(1);
            AssertThat(second.Height).IsEqual(2);
            AssertThat(second.Indices.SequenceEqual(new byte[] { 22, 79 })).IsTrue();
            AssertThat(second.Alpha.SequenceEqual(new byte[] { 128, 17 })).IsTrue();
            AssertThat(second.Texture is null).IsTrue();
            AssertThat(clean.Source.Count).IsEqual(2);
            AssertThat(clean.Source["archive"].VariantType).IsEqual(Variant.Type.String);
            AssertThat(clean.Source["archive"].AsString()).IsEqual("literal-battle.mkf");
            AssertThat(clean.Source["chunk"].VariantType).IsEqual(Variant.Type.Int);
            AssertThat(clean.Source["chunk"].AsInt32()).IsEqual(73);

            const string wrongHash = "0000000000000000000000000000000000000000000000000000000000000000";
            AssertThat(wrongHash == actualHash).IsFalse();
            // Only the root manifest value changes; the same valid file and independent expectation remain.
            hashes[uri] = wrongHash;
            AssertThat(Godot.FileAccess.GetSha256(uri)).IsEqual(actualHash);
            string failure = Failure(() => NativeBattleVisualProfile.LoadSprite(uri, expected, hashes));
            AssertThat(failure.Contains("SHA-256", StringComparison.Ordinal)).IsTrue();
            AssertThat(failure.Contains(uri, StringComparison.Ordinal)).IsTrue();
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [TestCase]
    public void StandaloneCheckedSpriteRejectsExtraManifestEntryAfterCleanRoundtrip()
    {
        string root = "res://Assets/RuntimeTests/checked-sprite-extra-manifest-" + Guid.NewGuid().ToString("N");
        string directory = ProjectSettings.GlobalizePath(root);
        string uri = root + "/Sprite.tres";
        try
        {
            Directory.CreateDirectory(directory);
            // Separate literal resources, Source dictionaries, frames and planes; no loaded-data oracle.
            var authored = new PalSprite
            {
                SpriteId = 73, DecodedByteLength = 64, FrameCount = 2,
                Source = new Godot.Collections.Dictionary { ["archive"] = "literal-battle.mkf", ["chunk"] = 73 },
                Frames =
                [
                    new PalFrame { Index = 0, SourceOffset = 6, Width = 2, Height = 1, Indices = [4, 19], Alpha = [0, 255] },
                    new PalFrame { Index = 1, SourceOffset = 18, Width = 1, Height = 2, Indices = [22, 79], Alpha = [128, 17] }
                ]
            };
            var expected = new PalSprite
            {
                SpriteId = 73, DecodedByteLength = 64, FrameCount = 2,
                Source = new Godot.Collections.Dictionary { ["archive"] = "literal-battle.mkf", ["chunk"] = 73 },
                Frames =
                [
                    new PalFrame { Index = 0, SourceOffset = 6, Width = 2, Height = 1, Indices = [4, 19], Alpha = [0, 255] },
                    new PalFrame { Index = 1, SourceOffset = 18, Width = 1, Height = 2, Indices = [22, 79], Alpha = [128, 17] }
                ]
            };
            AssertThat(ResourceSaver.Save(authored, uri)).IsEqual(Error.Ok);
            string rootHash = Godot.FileAccess.GetSha256(uri);
            AssertThat(rootHash.Length).IsEqual(64);
            AssertThat(rootHash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f')).IsTrue();
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal) { [uri] = rootHash };
            var clean = NativeBattleVisualProfile.LoadSprite(uri, expected, hashes);
            AssertThat(ReferenceEquals(clean, authored)).IsFalse();
            AssertThat(ReferenceEquals(clean, expected)).IsFalse();
            AssertThat(clean.SpriteId).IsEqual(73);
            AssertThat(clean.DecodedByteLength).IsEqual(64);
            AssertThat(clean.FrameCount).IsEqual(2);
            AssertThat(clean.Frames.Count).IsEqual(2);
            AssertThat(clean.Frames.Select(frame => frame.Index).Order().SequenceEqual(new[] { 0, 1 })).IsTrue();
            var first = clean.Frames.Single(frame => frame.Index == 0);
            AssertThat(first.SourceOffset).IsEqual(6);
            AssertThat(first.Width).IsEqual(2);
            AssertThat(first.Height).IsEqual(1);
            AssertThat(first.Indices.SequenceEqual(new byte[] { 4, 19 })).IsTrue();
            AssertThat(first.Alpha.SequenceEqual(new byte[] { 0, 255 })).IsTrue();
            AssertThat(first.Texture is null).IsTrue();
            var second = clean.Frames.Single(frame => frame.Index == 1);
            AssertThat(second.SourceOffset).IsEqual(18);
            AssertThat(second.Width).IsEqual(1);
            AssertThat(second.Height).IsEqual(2);
            AssertThat(second.Indices.SequenceEqual(new byte[] { 22, 79 })).IsTrue();
            AssertThat(second.Alpha.SequenceEqual(new byte[] { 128, 17 })).IsTrue();
            AssertThat(second.Texture is null).IsTrue();
            AssertThat(clean.Source.Count).IsEqual(2);
            AssertThat(clean.Source["archive"].VariantType).IsEqual(Variant.Type.String);
            AssertThat(clean.Source["archive"].AsString()).IsEqual("literal-battle.mkf");
            AssertThat(clean.Source["chunk"].VariantType).IsEqual(Variant.Type.Int);
            AssertThat(clean.Source["chunk"].AsInt32()).IsEqual(73);

            string unusedUri = root + "/Unused.tres";
            var unused = new PalSprite
            {
                SpriteId = 74, DecodedByteLength = 32, FrameCount = 1,
                Source = new Godot.Collections.Dictionary { ["archive"] = "unused-literal-battle.mkf", ["chunk"] = 74 },
                Frames = [new PalFrame { Index = 0, SourceOffset = 4, Width = 1, Height = 1, Indices = [31], Alpha = [255] }]
            };
            AssertThat(ResourceSaver.Save(unused, unusedUri)).IsEqual(Error.Ok);
            string unusedHash = Godot.FileAccess.GetSha256(unusedUri);
            AssertThat(unusedHash.Length).IsEqual(64);
            AssertThat(unusedHash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f')).IsTrue();
            // Saving an unrelated sibling does not add a root dependency or change its bytes/hash.
            string rootText = File.ReadAllText(ProjectSettings.GlobalizePath(uri));
            AssertThat(rootText.Contains("Unused.tres", StringComparison.Ordinal)).IsFalse();
            AssertThat(Godot.FileAccess.GetSha256(uri)).IsEqual(rootHash);
            AssertThat(hashes.Count).IsEqual(1);
            AssertThat(hashes[uri]).IsEqual(rootHash);
            // Only exact manifest membership changes; both entries contain their real file hashes.
            hashes.Add(unusedUri, unusedHash);
            AssertThat(hashes.Count).IsEqual(2);
            string failure = Failure(() => NativeBattleVisualProfile.LoadSprite(uri, expected, hashes));
            AssertThat(failure.Contains(uri, StringComparison.Ordinal)).IsTrue();
            AssertThat(failure.Contains("dependency", StringComparison.OrdinalIgnoreCase)
                || failure.Contains("dependencies", StringComparison.OrdinalIgnoreCase)
                || failure.Contains("manifest", StringComparison.OrdinalIgnoreCase)).IsTrue();
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [TestCase]
    public void StandaloneCheckedSpriteMatchesIndependentFieldsAndSourceBoundaries()
    {
        string root = "res://Assets/RuntimeTests/checked-sprite-fields-source-" + Guid.NewGuid().ToString("N");
        string directory = ProjectSettings.GlobalizePath(root);
        string uri = root + "/Sprite.tres";
        string variant = "clean-literal-control";
        var authored = Literal(true);
        var expected = Literal(false);
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);

        // Every call owns its resources, collections and planes. No target load supplies expectations.
        PalSprite Literal(bool actualOrder)
        {
            var zero = new PalFrame { Index = 0, SourceOffset = 6, Width = 2, Height = 1, Indices = [4, 19], Alpha = [0, 255] };
            var one = new PalFrame { Index = 1, SourceOffset = 18, Width = 1, Height = 2, Indices = [22, 79], Alpha = [128, 17] };
            var alias = new PalFrame { Index = 7, SourceOffset = 6, Width = 2, Height = 1, Indices = [4, 19], Alpha = [0, 255] };
            return new PalSprite
            {
                SpriteId = 73, DecodedByteLength = 64, FrameCount = 2,
                Source = new Godot.Collections.Dictionary
                {
                    ["archive"] = "literal-battle.mkf", ["chunk"] = 73,
                    ["meta"] = new Godot.Collections.Dictionary
                    {
                        ["enabled"] = true, ["nothing"] = default(Variant), ["weight"] = 1.5,
                        ["sequence"] = new Godot.Collections.Array { 73, "73", 73.0 }
                    },
                    ["quoted\"\\\n\t"] = "literal-escaped"
                },
                Frames = actualOrder ? [alias, one, zero] : [zero, one, alias]
            };
        }
        PalFrame Frame(PalSprite sprite, int index) => sprite.Frames.Single(frame => frame.Index == index);
        void AssertLiteral(PalSprite sprite)
        {
            AssertThat(ReferenceEquals(sprite, authored) || ReferenceEquals(sprite, expected)).IsFalse();
            AssertThat(sprite.SpriteId).IsEqual(73);
            AssertThat(sprite.DecodedByteLength).IsEqual(64);
            AssertThat(sprite.FrameCount).IsEqual(2);
            AssertThat(sprite.Frames.Count).IsEqual(3);
            AssertThat(sprite.Frames.Select(frame => frame.Index).Order().SequenceEqual(new[] { 0, 1, 7 })).IsTrue();
            foreach (int index in new[] { 0, 1, 7 })
            {
                var frame = Frame(sprite, index);
                AssertThat(frame.Index).IsEqual(index);
                AssertThat(frame.SourceOffset).IsEqual(index == 1 ? 18 : 6);
                AssertThat(frame.Width).IsEqual(index == 1 ? 1 : 2);
                AssertThat(frame.Height).IsEqual(index == 1 ? 2 : 1);
                AssertThat(frame.Indices.SequenceEqual(index == 1 ? new byte[] { 22, 79 } : new byte[] { 4, 19 })).IsTrue();
                AssertThat(frame.Alpha.SequenceEqual(index == 1 ? new byte[] { 128, 17 } : new byte[] { 0, 255 })).IsTrue();
                AssertThat(frame.Texture is null).IsTrue();
            }
            AssertThat(sprite.Source.Count).IsEqual(4);
            AssertThat(sprite.Source["archive"].VariantType).IsEqual(Variant.Type.String);
            AssertThat(sprite.Source["archive"].AsString()).IsEqual("literal-battle.mkf");
            AssertThat(sprite.Source["chunk"].VariantType).IsEqual(Variant.Type.Int);
            AssertThat(sprite.Source["chunk"].AsInt64()).IsEqual(73L);
            AssertThat(sprite.Source["meta"].VariantType).IsEqual(Variant.Type.Dictionary);
            var meta = sprite.Source["meta"].AsGodotDictionary();
            AssertThat(meta.Count).IsEqual(4);
            AssertThat(meta["enabled"].VariantType).IsEqual(Variant.Type.Bool);
            AssertThat(meta["enabled"].AsBool()).IsTrue();
            AssertThat(meta["nothing"].VariantType).IsEqual(Variant.Type.Nil);
            AssertThat(meta["weight"].VariantType).IsEqual(Variant.Type.Float);
            AssertThat(meta["weight"].AsDouble()).IsEqual(1.5);
            AssertThat(meta["sequence"].VariantType).IsEqual(Variant.Type.Array);
            var sequence = meta["sequence"].AsGodotArray();
            AssertThat(sequence.Count).IsEqual(3);
            AssertThat(sequence[0].VariantType).IsEqual(Variant.Type.Int);
            AssertThat(sequence[0].AsInt64()).IsEqual(73L);
            AssertThat(sequence[1].VariantType).IsEqual(Variant.Type.String);
            AssertThat(sequence[1].AsString()).IsEqual("73");
            AssertThat(sequence[2].VariantType).IsEqual(Variant.Type.Float);
            AssertThat(sequence[2].AsDouble()).IsEqual(73.0);
            AssertThat(sprite.Source["quoted\"\\\n\t"].VariantType).IsEqual(Variant.Type.String);
            AssertThat(sprite.Source["quoted\"\\\n\t"].AsString()).IsEqual("literal-escaped");
        }
        void SaveActualAndProve(Action<PalSprite> prove)
        {
            // A serializer/parser failure is a setup failure, never a checked-validator rejection.
            AssertThat(ResourceSaver.Save(authored, uri)).IsEqual(Error.Ok);
            string hash = Godot.FileAccess.GetSha256(uri);
            AssertThat(hash.Length).IsEqual(64);
            AssertThat(hash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f')).IsTrue();
            hashes = new Dictionary<string, string>(StringComparer.Ordinal) { [uri] = hash };
            var roundtrip = ResourceLoader.Load<PalSprite>(uri, cacheMode: ResourceLoader.CacheMode.IgnoreDeep)
                ?? throw new InvalidOperationException(variant + ": public typed roundtrip setup failed");
            prove(roundtrip);
        }
        void Reset(string name)
        {
            variant = name;
            authored = Literal(true);
            expected = Literal(false);
            SaveActualAndProve(AssertLiteral);
            AssertLiteral(NativeBattleVisualProfile.LoadSprite(uri, expected, hashes));
        }
        string Reject(string field, Action action)
        {
            string failure = Failure(action);
            if (!failure.Contains(uri, StringComparison.Ordinal) || !failure.Contains(field, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(variant + ": missing URI/" + field + " context: " + failure);
            return failure;
        }
        Godot.Collections.Dictionary DepthSource(int scalarDepth)
        {
            Variant value = 1;
            for (int depth = 1; depth < scalarDepth; depth++) value = new Godot.Collections.Array { value };
            return new Godot.Collections.Dictionary { ["depth"] = value };
        }
        Godot.Collections.Dictionary WorkSource(int scalars)
        {
            var values = new Godot.Collections.Array();
            for (int index = 0; index < scalars; index++) values.Add(1);
            // root dictionary + String key + array + scalars; keys consume work too.
            return new Godot.Collections.Dictionary { ["work"] = values };
        }
        void ProveDepth(PalSprite sprite, int scalarDepth)
        {
            AssertThat(sprite.Source.Count).IsEqual(1);
            Variant value = sprite.Source["depth"];
            for (int depth = 1; depth < scalarDepth; depth++)
            {
                AssertThat(value.VariantType).IsEqual(Variant.Type.Array);
                var array = value.AsGodotArray();
                AssertThat(array.Count).IsEqual(1);
                value = array[0];
            }
            AssertThat(value.VariantType).IsEqual(Variant.Type.Int);
            AssertThat(value.AsInt64()).IsEqual(1L);
        }
        void ProveWork(PalSprite sprite, int scalars)
        {
            AssertThat(sprite.Source.Count).IsEqual(1);
            AssertThat(sprite.Source["work"].VariantType).IsEqual(Variant.Type.Array);
            var values = sprite.Source["work"].AsGodotArray();
            AssertThat(values.Count).IsEqual(scalars);
            AssertThat(values.All(value => value.VariantType == Variant.Type.Int && value.AsInt64() == 1)).IsTrue();
        }
        try
        {
            Directory.CreateDirectory(directory);
            Reset("clean-literal-control");
            var clean = NativeBattleVisualProfile.LoadSprite(uri, expected, hashes);
            AssertLiteral(clean);
            // Mutating this returned Resource must not change the independent expectation or disk.
            clean.SpriteId = 99;
            Frame(clean, 0).Indices = [99, 19];
            Frame(clean, 1).Alpha = [0, 17];
            clean.Source["meta"].AsGodotDictionary()["sequence"].AsGodotArray()[0] = 99;
            AssertThat(Frame(expected, 0).Indices.SequenceEqual(new byte[] { 4, 19 })).IsTrue();
            AssertThat(expected.Source["meta"].AsGodotDictionary()["sequence"].AsGodotArray()[0].AsInt64()).IsEqual(73L);
            AssertLiteral(NativeBattleVisualProfile.LoadSprite(uri, expected, hashes));
            string cleanHash = hashes[uri];
            AssertThat(hashes.Count).IsEqual(1);
            AssertThat(hashes.Keys.Single()).IsEqual(uri);

            // First designated negative: ONLY expected.SpriteId changes, retaining the actual URI/bytes/manifest/Source.
            variant = "expected-SpriteId-74-versus-actual-73";
            expected.SpriteId = 74;
            AssertThat(Godot.FileAccess.GetSha256(uri)).IsEqual(cleanHash);
            string firstFailure = Failure(() => NativeBattleVisualProfile.LoadSprite(uri, expected, hashes));
            AssertThat(firstFailure.Contains(uri, StringComparison.Ordinal)).IsTrue();
            AssertThat(firstFailure.Contains("SpriteId", StringComparison.Ordinal)).IsTrue();

            // Everything below is intentionally unreachable at the first safe-false-positive RED.
            foreach (string name in new[] { "DecodedByteLength", "FrameCount", "alias-replaced", "alias-removed", "alias-added", "SourceOffset", "Width-Height", "Height", "Indices", "Alpha" })
            {
                Reset("expected-" + name);
                string field;
                switch (name)
                {
                    case "DecodedByteLength": expected.DecodedByteLength = 65; field = "DecodedByteLength"; break;
                    case "FrameCount": expected.FrameCount = 1; field = "FrameCount"; break;
                    case "alias-replaced": Frame(expected, 7).Index = 8; field = "Index"; break;
                    case "alias-removed": expected.Frames.Remove(Frame(expected, 7)); field = "Index"; break;
                    case "alias-added": expected.Frames.Add(new PalFrame { Index = 8, SourceOffset = 6, Width = 2, Height = 1, Indices = [4, 19], Alpha = [0, 255] }); field = "Index"; break;
                    case "SourceOffset": Frame(expected, 0).SourceOffset = 7; field = "SourceOffset"; break;
                    case "Width-Height": Frame(expected, 0).Width = 1; Frame(expected, 0).Height = 2; field = "Width"; break;
                    case "Height": Frame(expected, 0).Height = 2; Frame(expected, 0).Indices = [4, 19, 4, 19]; Frame(expected, 0).Alpha = [0, 255, 0, 255]; field = "Height"; break;
                    case "Indices": Frame(expected, 0).Indices = [5, 19]; field = "Indices"; break;
                    default: Frame(expected, 1).Alpha = [128, 18]; field = "Alpha"; break;
                }
                Reject(field, () => NativeBattleVisualProfile.LoadSprite(uri, expected, hashes));
            }
            foreach (string name in new[] { "replace", "remove", "add" })
            {
                Reset("actual-Index-set-" + name);
                if (name == "replace") Frame(authored, 7).Index = 8;
                else if (name == "remove") authored.Frames.Remove(Frame(authored, 7));
                else authored.Frames.Add(new PalFrame { Index = 8, SourceOffset = 6, Width = 2, Height = 1, Indices = [4, 19], Alpha = [0, 255] });
                SaveActualAndProve(sprite => AssertThat(sprite.Frames.Select(frame => frame.Index).Order().SequenceEqual(
                    name == "replace" ? new[] { 0, 1, 8 } : name == "remove" ? new[] { 0, 1 } : new[] { 0, 1, 7, 8 })).IsTrue());
                // These sets all remain structurally legal: nominal 0/1 plus optional aliases.
                AssertThat(NativeBattleVisualProfile.LoadSprite(uri).FrameCount).IsEqual(2);
                Reject("Index", () => NativeBattleVisualProfile.LoadSprite(uri, expected, hashes));
            }
            Reset("order-ignored-positive");
            expected.Frames = [Frame(expected, 1), Frame(expected, 7), Frame(expected, 0)];
            AssertLiteral(NativeBattleVisualProfile.LoadSprite(uri, expected, hashes));
            foreach (bool actualSide in new[] { false, true })
                foreach (string name in new[] { "duplicate", "negative", "overflow", "nominal-missing" })
                {
                    Reset((actualSide ? "actual-" : "expected-") + "invalid-Index-" + name);
                    var target = actualSide ? authored : expected;
                    if (name == "nominal-missing") target.Frames.Remove(Frame(target, 1));
                    else Frame(target, 7).Index = name == "duplicate" ? 0 : name == "negative" ? -1 : 65536;
                    if (actualSide) SaveActualAndProve(sprite =>
                        AssertThat(name == "nominal-missing" ? sprite.Frames.Count == 2 && !sprite.Frames.Any(frame => frame.Index == 1)
                            : sprite.Frames.Count == 3 && sprite.Frames.Count(frame => frame.Index == (name == "duplicate" ? 0 : name == "negative" ? -1 : 65536))
                                == (name == "duplicate" ? 2 : 1)).IsTrue());
                    Reject(name == "nominal-missing" ? "frame" : "Index", () => NativeBattleVisualProfile.LoadSprite(uri, expected, hashes));
                }

            foreach (string name in new[] { "String", "Int", "String-type", "Float-type", "Float-value", "Bool", "Nil", "nested-value", "array-order", "array-length", "missing-key", "extra-key", "escaped-key", "Vector2", "non-String-key" })
            {
                Reset("expected-Source-" + name);
                var meta = expected.Source["meta"].AsGodotDictionary();
                string path = "Source";
                switch (name)
                {
                    case "String": expected.Source["archive"] = "other.mkf"; path = "Source[\"archive\"]"; break;
                    case "Int": expected.Source["chunk"] = 74; path = "Source[\"chunk\"]"; break;
                    case "String-type": expected.Source["chunk"] = "73"; path = "Source[\"chunk\"]"; break;
                    case "Float-type": expected.Source["chunk"] = 73.0; path = "Source[\"chunk\"]"; break;
                    case "Float-value": meta["weight"] = 1.75; path = "Source[\"meta\"][\"weight\"]"; break;
                    case "Bool": meta["enabled"] = false; path = "Source[\"meta\"][\"enabled\"]"; break;
                    case "Nil": meta["nothing"] = false; path = "Source[\"meta\"][\"nothing\"]"; break;
                    case "nested-value": meta["sequence"].AsGodotArray()[1] = "74"; path = "Source[\"meta\"][\"sequence\"][1]"; break;
                    case "array-order": meta["sequence"] = new Godot.Collections.Array { "73", 73, 73.0 }; path = "Source[\"meta\"][\"sequence\"][0]"; break;
                    case "array-length": meta["sequence"].AsGodotArray().Add(74); path = "Source[\"meta\"][\"sequence\"]"; break;
                    case "missing-key": expected.Source.Remove("archive"); path = "Source[\"archive\"]"; break;
                    case "extra-key": expected.Source["extra"] = 1; path = "Source[\"extra\"]"; break;
                    case "escaped-key": expected.Source["quoted\"\\\n\t"] = "different"; path = "Source[\"quoted\\\"\\\\\\n\\t\"]"; break;
                    case "Vector2": meta["unsupported"] = new Vector2(2, 3); path = "Source[\"meta\"][\"unsupported\"]"; break;
                    default: expected.Source[7] = "non-string"; path = "Source[key]"; break;
                }
                Reject(path, () => NativeBattleVisualProfile.LoadSprite(uri, expected, hashes));
            }
            foreach (string name in new[] { "Vector2", "non-String-key" })
            {
                Reset("actual-Source-" + name);
                if (name == "Vector2") authored.Source["unsupported"] = new Vector2(2, 3);
                else authored.Source[7] = "non-string";
                SaveActualAndProve(sprite =>
                {
                    if (name == "Vector2")
                    {
                        AssertThat(sprite.Source["unsupported"].VariantType).IsEqual(Variant.Type.Vector2);
                        AssertThat(sprite.Source["unsupported"].AsVector2()).IsEqual(new Vector2(2, 3));
                    }
                    else AssertThat(sprite.Source.Any(entry => entry.Key.VariantType == Variant.Type.Int && entry.Key.AsInt64() == 7 && entry.Value.AsString() == "non-string")).IsTrue();
                });
                Reject("Source", () => NativeBattleVisualProfile.LoadSprite(uri, expected, hashes));
            }
            foreach (bool actualSide in new[] { false, true })
                foreach (double number in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
                {
                    Reset((actualSide ? "actual-" : "expected-") + "Source-nonfinite-" + number);
                    if (actualSide)
                    {
                        authored.Source["number"] = number;
                        // Do not credit this negative unless public Save/IgnoreDeep preserves the nonfinite value.
                        SaveActualAndProve(sprite =>
                        {
                            AssertThat(sprite.Source["number"].VariantType).IsEqual(Variant.Type.Float);
                            double saved = sprite.Source["number"].AsDouble();
                            AssertThat(double.IsNaN(number) ? double.IsNaN(saved) : saved == number).IsTrue();
                        });
                    }
                    else expected.Source["number"] = number;
                    Reject("Source[\"number\"]", () => NativeBattleVisualProfile.LoadSprite(uri, expected, hashes));
                }
            Reset("Source-dictionary-order-ignored-positive");
            expected.Source = new Godot.Collections.Dictionary
            {
                ["quoted\"\\\n\t"] = "literal-escaped",
                ["meta"] = new Godot.Collections.Dictionary
                {
                    ["sequence"] = new Godot.Collections.Array { 73, "73", 73.0 },
                    ["weight"] = 1.5, ["nothing"] = default(Variant), ["enabled"] = true
                },
                ["chunk"] = 73, ["archive"] = "literal-battle.mkf"
            };
            AssertLiteral(NativeBattleVisualProfile.LoadSprite(uri, expected, hashes));

            Reset("Source-depth64-both-independent-budgets-positive");
            authored.Source = DepthSource(64);
            expected.Source = DepthSource(64);
            SaveActualAndProve(sprite => ProveDepth(sprite, 64));
            ProveDepth(NativeBattleVisualProfile.LoadSprite(uri, expected, hashes), 64);
            foreach (bool actualSide in new[] { false, true })
            {
                Reset((actualSide ? "actual-" : "expected-") + "Source-depth65");
                expected.Source = DepthSource(64);
                authored.Source = DepthSource(actualSide ? 65 : 64);
                SaveActualAndProve(sprite => ProveDepth(sprite, actualSide ? 65 : 64));
                if (!actualSide) expected.Source = DepthSource(65);
                string failure = Reject("Source", () => NativeBattleVisualProfile.LoadSprite(uri, expected, hashes));
                AssertThat(failure.Contains("depth bound (possibly cyclic)", StringComparison.Ordinal)).IsTrue();
            }
            Reset("Source-work65536-both-independent-budgets-positive");
            authored.Source = WorkSource(65533);
            expected.Source = WorkSource(65533);
            SaveActualAndProve(sprite => ProveWork(sprite, 65533));
            ProveWork(NativeBattleVisualProfile.LoadSprite(uri, expected, hashes), 65533);
            foreach (bool actualSide in new[] { false, true })
            {
                Reset((actualSide ? "actual-" : "expected-") + "Source-work65537-including-key");
                authored.Source = WorkSource(actualSide ? 65534 : 65533);
                expected.Source = WorkSource(65533);
                SaveActualAndProve(sprite => ProveWork(sprite, actualSide ? 65534 : 65533));
                if (!actualSide) expected.Source = WorkSource(65534);
                string failure = Reject("Source", () => NativeBattleVisualProfile.LoadSprite(uri, expected, hashes));
                AssertThat(failure.Contains("work bound (possibly cyclic)", StringComparison.Ordinal)).IsTrue();
            }
            foreach (bool actualSide in new[] { false, true })
            {
                Reset((actualSide ? "actual-" : "expected-") + "Source-repeated-alias-by-value-positive");
                var shared = new Godot.Collections.Array { 1, "two" };
                var aliased = new Godot.Collections.Dictionary { ["repeat"] = new Godot.Collections.Array { shared, shared } };
                var separate = new Godot.Collections.Dictionary { ["repeat"] = new Godot.Collections.Array
                    { new Godot.Collections.Array { 1, "two" }, new Godot.Collections.Array { 1, "two" } } };
                authored.Source = actualSide ? aliased : separate;
                expected.Source = actualSide ? separate : aliased;
                void ProveRepeated(PalSprite sprite)
                {
                    var values = sprite.Source["repeat"].AsGodotArray();
                    AssertThat(values.Count).IsEqual(2);
                    foreach (var value in values)
                    {
                        AssertThat(value.VariantType).IsEqual(Variant.Type.Array);
                        var child = value.AsGodotArray();
                        AssertThat(child.Count).IsEqual(2);
                        AssertThat(child[0].VariantType).IsEqual(Variant.Type.Int);
                        AssertThat(child[0].AsInt64()).IsEqual(1L);
                        AssertThat(child[1].VariantType).IsEqual(Variant.Type.String);
                        AssertThat(child[1].AsString()).IsEqual("two");
                    }
                }
                SaveActualAndProve(ProveRepeated);
                ProveRepeated(NativeBattleVisualProfile.LoadSprite(uri, expected, hashes));
            }
            Reset("expected-Source-cycle-bounded-not-identity-detection");
            var cycle = new Godot.Collections.Array();
            try
            {
                cycle.Add(cycle);
                expected.Source["cycle"] = cycle;
                string failure = Reject("Source[\"cycle\"]", () => NativeBattleVisualProfile.LoadSprite(uri, expected, hashes));
                AssertThat(failure.Contains("bound (possibly cyclic)", StringComparison.Ordinal)).IsTrue();
            }
            finally { cycle.Clear(); }
            // No actual cyclic Save: public serialization has no established cyclic-collection roundtrip.
            foreach (string name in new[] { "expected-null", "expected-Source-null", "expected-Source-empty", "manifest-null", "expected-Frames-null", "expected-frame-null", "expected-Indices-null", "expected-Alpha-null" })
            {
                Reset(name);
                // Intentional invalid-input null! follows existing native tests, not a production warning waiver.
                string field = "Source";
                switch (name)
                {
                    case "expected-null": Reject("expected", () => NativeBattleVisualProfile.LoadSprite(uri, null!, hashes)); continue;
                    case "expected-Source-null": expected.Source = null!; break;
                    case "expected-Source-empty": expected.Source.Clear(); break;
                    case "manifest-null": Reject("manifest", () => NativeBattleVisualProfile.LoadSprite(uri, expected, null!)); continue;
                    case "expected-Frames-null": expected.Frames = null!; field = "Frames"; break;
                    case "expected-frame-null": expected.Frames[0] = null!; field = "frame"; break;
                    case "expected-Indices-null": Frame(expected, 0).Indices = null!; field = "Indices"; break;
                    default: Frame(expected, 0).Alpha = null!; field = "Alpha"; break;
                }
                Reject(field, () => NativeBattleVisualProfile.LoadSprite(uri, expected, hashes));
            }
            Reset("actual-Source-empty");
            authored.Source.Clear();
            SaveActualAndProve(sprite => AssertThat(sprite.Source.Count).IsEqual(0));
            Reject("Source", () => NativeBattleVisualProfile.LoadSprite(uri, expected, hashes));
            foreach (string? badUri in new string?[] { null, "", "user://outside.tres", root + "/../Sprite.tres" })
            {
                Reset("invalid-URI-" + (badUri ?? "null"));
                string failure = Failure(() => NativeBattleVisualProfile.LoadSprite(badUri!, expected, hashes));
                AssertThat(failure.Contains("URI", StringComparison.OrdinalIgnoreCase)
                    || failure.Contains("res://", StringComparison.Ordinal)).IsTrue();
                if (!string.IsNullOrEmpty(badUri)) AssertThat(failure.Contains(badUri, StringComparison.Ordinal)).IsTrue();
            }
            Reset("returned-resource-mutations-do-not-pollute-IgnoreDeep-reload");
            var isolated = NativeBattleVisualProfile.LoadSprite(uri, expected, hashes);
            isolated.DecodedByteLength = 65;
            isolated.FrameCount = 1;
            Frame(isolated, 7).SourceOffset = 7;
            Frame(isolated, 0).Width = 1;
            Frame(isolated, 0).Height = 2;
            Frame(isolated, 0).Indices = [5, 19];
            Frame(isolated, 1).Alpha = [128, 18];
            isolated.Source.Clear();
            isolated.Frames.Clear();
            AssertThat(Godot.FileAccess.GetSha256(uri)).IsEqual(hashes[uri]);
            AssertLiteral(NativeBattleVisualProfile.LoadSprite(uri, expected, hashes));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [TestCase]
    public void StandaloneCheckedSpriteMatchesTextureIdentityAndExactDependencies()
    {
        string root = "res://Assets/RuntimeTests/checked-sprite-texture-" + Guid.NewGuid().ToString("N");
        string directory = ProjectSettings.GlobalizePath(root);
        string uri = root + "/Sprite.tres";
        const string textureUri = NativeStoryProfileFixture.TextureSource;
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        Texture2D Texture() => ResourceLoader.Load<Texture2D>(textureUri, cacheMode: ResourceLoader.CacheMode.IgnoreDeep)
            ?? throw new InvalidOperationException("literal fixture texture missing");
        PalSprite Literal(Texture2D? texture) => new()
        {
            SpriteId = 73, DecodedByteLength = 512, FrameCount = 1,
            Source = new Godot.Collections.Dictionary { ["archive"] = "literal-textured.mkf", ["chunk"] = 73 },
            Frames = [new PalFrame { Index = 0, SourceOffset = 4, Width = 11, Height = 19,
                Indices = Enumerable.Repeat((byte)4, 209).ToArray(), Alpha = Enumerable.Repeat((byte)255, 209).ToArray(), Texture = texture! }]
        };
        var authored = Literal(Texture());
        var expected = Literal(Texture()); // Independent literal, never populated from the target sprite.
        void Save(params string[] dependencies)
        {
            AssertThat(ResourceSaver.Save(authored, uri)).IsEqual(Error.Ok);
            hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string path in new[] { uri }.Concat(dependencies)) hashes.Add(path, Godot.FileAccess.GetSha256(path));
        }
        string Reject(string field, string? target = null)
        {
            string failure = Failure(() => NativeBattleVisualProfile.LoadSprite(target ?? uri, expected, hashes));
            AssertThat(failure.Contains(target ?? uri, StringComparison.Ordinal)).IsTrue();
            AssertThat(failure.Contains(field, StringComparison.OrdinalIgnoreCase)).IsTrue();
            return failure;
        }
        void Prove(PalSprite sprite)
        {
            AssertThat(sprite.SpriteId).IsEqual(73);
            AssertThat(sprite.Frames[0].Texture.ResourcePath).IsEqual(textureUri);
            AssertThat(sprite.Frames[0].Texture.GetClass()).IsEqual("CompressedTexture2D");
            AssertThat(sprite.Frames[0].Texture.GetWidth()).IsEqual(11);
            AssertThat(sprite.Frames[0].Texture.GetHeight()).IsEqual(19);
            AssertThat(sprite.Frames[0].Indices.SequenceEqual(Enumerable.Repeat((byte)4, 209))).IsTrue();
            AssertThat(sprite.Frames[0].Alpha.SequenceEqual(Enumerable.Repeat((byte)255, 209))).IsTrue();
        }
        try
        {
            Directory.CreateDirectory(directory);
            Save(textureUri, textureUri + ".import");
            Prove(ResourceLoader.Load<PalSprite>(uri, cacheMode: ResourceLoader.CacheMode.IgnoreDeep));
            Prove(NativeBattleVisualProfile.LoadSprite(uri, expected, hashes));
            string unchangedRoot = hashes[uri];
            // Designated first RED: only expected optionality changes, not URI/file/manifest/planes/Source.
            expected.Frames[0].Texture = null!;
            AssertThat(Godot.FileAccess.GetSha256(uri)).IsEqual(unchangedRoot);
            Reject("Texture");

            expected = Literal(Texture());
            authored.Frames[0].Texture = null!;
            Save();
            AssertThat(ResourceLoader.Load<PalSprite>(uri, cacheMode: ResourceLoader.CacheMode.IgnoreDeep).Frames[0].Texture is null).IsTrue();
            Reject("Texture");
            expected.Frames[0].Texture = null!;
            AssertThat(NativeBattleVisualProfile.LoadSprite(uri, expected, hashes).Frames[0].Texture is null).IsTrue();
            authored = Literal(Texture()); expected = Literal(Texture());
            Save(textureUri, textureUri + ".import");
            foreach (string path in new[] { uri, textureUri, textureUri + ".import" })
            {
                string value = hashes[path];
                hashes.Remove(path); Reject("SHA-256"); hashes.Add(path, value);
                hashes[path] = new string('0', 64); Reject("SHA-256"); hashes[path] = value;
            }
            // The root's external frame and opaque metadata participate in the same exact graph.
            string frameUri = root + "/Frame.tres", opaqueUri = root + "/Opaque.tres";
            AssertThat(ResourceSaver.Save(authored.Frames[0], frameUri)).IsEqual(Error.Ok);
            authored.Frames[0] = ResourceLoader.Load<PalFrame>(frameUri, cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
            AssertThat(ResourceSaver.Save(new Resource(), opaqueUri)).IsEqual(Error.Ok);
            authored.SetMeta("opaque", ResourceLoader.Load<Resource>(opaqueUri, cacheMode: ResourceLoader.CacheMode.IgnoreDeep));
            Save(frameUri, opaqueUri, textureUri, textureUri + ".import");
            Prove(NativeBattleVisualProfile.LoadSprite(uri, expected, hashes));
            foreach (string path in new[] { frameUri, opaqueUri })
            {
                string value = hashes[path];
                hashes.Remove(path); Reject("SHA-256"); hashes.Add(path, value);
                hashes[path] = new string('0', 64); Reject("SHA-256"); hashes[path] = value;
            }
            string unused = root + "/Unused.tres";
            AssertThat(ResourceSaver.Save(new Resource(), unused)).IsEqual(Error.Ok);
            hashes.Add(unused, Godot.FileAccess.GetSha256(unused)); Reject("manifest"); hashes.Remove(unused);
            // These are ordinary native textures, not an invented PNG/import/cache certificate.
            using var image = Image.CreateEmpty(11, 19, false, Image.Format.Rgba8);
            image.Fill(Colors.Magenta);
            string firstUri = root + "/FirstTexture.tres", secondUri = root + "/SecondTexture.tres";
            AssertThat(ResourceSaver.Save(ImageTexture.CreateFromImage(image), firstUri)).IsEqual(Error.Ok);
            AssertThat(ResourceSaver.Save(ImageTexture.CreateFromImage(image), secondUri)).IsEqual(Error.Ok);
            authored = Literal(ResourceLoader.Load<Texture2D>(firstUri, cacheMode: ResourceLoader.CacheMode.IgnoreDeep));
            expected = Literal(ResourceLoader.Load<Texture2D>(secondUri, cacheMode: ResourceLoader.CacheMode.IgnoreDeep));
            Save(firstUri);
            Reject("Texture URI"); // Same type/dimensions, different normalized native URI.
            expected.Frames[0].Texture = ResourceLoader.Load<Texture2D>(firstUri, cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
            AssertThat(NativeBattleVisualProfile.LoadSprite(uri, expected, hashes).Frames[0].Texture.GetClass()).IsEqual("ImageTexture");
            var atlas = new AtlasTexture { Atlas = Texture(), Region = new Rect2(0, 0, 11, 19) };
            string typeUri = root + "/TypeTexture.tres";
            AssertThat(ResourceSaver.Save(atlas, typeUri)).IsEqual(Error.Ok);
            // Save does not establish ResourcePath on this newly created object in the actual runtime.
            // Read back the independently authored Atlas fixture before replacing its file type.
            var expectedAtlas = ResourceLoader.Load<AtlasTexture>(typeUri, cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
            AssertThat(expectedAtlas.ResourcePath).IsEqual(typeUri);
            AssertThat(expectedAtlas.GetClass()).IsEqual("AtlasTexture");
            AssertThat(expectedAtlas.GetWidth()).IsEqual(11);
            AssertThat(ResourceSaver.Save(ImageTexture.CreateFromImage(image), typeUri)).IsEqual(Error.Ok);
            authored = Literal(ResourceLoader.Load<Texture2D>(typeUri, cacheMode: ResourceLoader.CacheMode.IgnoreDeep));
            expected = Literal(expectedAtlas);
            Save(typeUri);
            AssertThat(authored.Frames[0].Texture.GetClass()).IsEqual("ImageTexture");
            Reject("native type"); // Same URI and dimensions, independently expected AtlasTexture type.
            expected.Frames[0].Texture = ImageTexture.CreateFromImage(image);
            Reject("Texture", "user://intentionally-invalid.tres"); // Invalid expected path wins before target I/O.
            using var small = Image.CreateEmpty(2, 1, false, Image.Format.Rgba8);
            string smallUri = root + "/Small.tres";
            AssertThat(ResourceSaver.Save(ImageTexture.CreateFromImage(small), smallUri)).IsEqual(Error.Ok);
            expected.Frames[0].Texture = ResourceLoader.Load<Texture2D>(smallUri, cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
            Reject("Texture", "user://intentionally-invalid.tres"); // Invalid expected dimensions also precede target I/O.
            expected = Literal(null); expected.SpriteId = -1;
            Reject("SpriteId", "user://intentionally-invalid.tres");
            expected = Literal(Texture());
            authored = Literal(ResourceLoader.Load<Texture2D>(smallUri, cacheMode: ResourceLoader.CacheMode.IgnoreDeep));
            Save(smallUri);
            AssertThat(ResourceLoader.Load<PalSprite>(uri, cacheMode: ResourceLoader.CacheMode.IgnoreDeep).Frames[0].Texture.GetWidth()).IsEqual(2);
            Reject("Texture dimensions");
            authored = Literal(ResourceLoader.Load<Texture2D>(firstUri, cacheMode: ResourceLoader.CacheMode.IgnoreDeep));
            expected = Literal(ResourceLoader.Load<Texture2D>(firstUri, cacheMode: ResourceLoader.CacheMode.IgnoreDeep));
            Save(firstUri);
            var isolated = NativeBattleVisualProfile.LoadSprite(uri, expected, hashes);
            ((ImageTexture)isolated.Frames[0].Texture).SetSizeOverride(new(2, 1));
            AssertThat(NativeBattleVisualProfile.LoadSprite(uri, expected, hashes).Frames[0].Texture.GetWidth()).IsEqual(11);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [TestCase]
    public void StandaloneCheckedSpriteRejectsUnsafeOpaqueAndImportGraphsBeforeLoading()
    {
        string root = "res://Assets/RuntimeTests/checked-sprite-graph-" + Guid.NewGuid().ToString("N");
        string directory = ProjectSettings.GlobalizePath(root);
        string uri = root + "/Sprite.tres", extra = root + "/Extra.tres", unused = root + "/Unused.tres";
        const string texture = NativeStoryProfileFixture.TextureSource;
        PalSprite Literal() => new() { SpriteId = 73, DecodedByteLength = 16, FrameCount = 1,
            Source = new Godot.Collections.Dictionary { ["archive"] = "literal-graph.mkf", ["chunk"] = 73 },
            Frames = [new PalFrame { Index = 0, SourceOffset = 4, Width = 2, Height = 1, Indices = [4, 19], Alpha = [0, 255] }] };
        var authored = Literal(); var expected = Literal();
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        string Plain(string target, string uid = "") => "[gd_resource type=\"Resource\" format=3]\n"
            + $"[ext_resource type=\"Resource\"{uid} path=\"{target}\" id=\"leaf\"]\n[resource]\nmetadata/leaf = ExtResource(\"leaf\")\n";
        void Hash(params string[] paths)
        {
            hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string path in paths) hashes.Add(path, Godot.FileAccess.GetSha256(path));
        }
        void WriteExtra(string text) => File.WriteAllText(ProjectSettings.GlobalizePath(extra), text);
        void Reject(string context)
        {
            string failure = Failure(() => NativeBattleVisualProfile.LoadSprite(uri, expected, hashes));
            AssertThat(failure.Contains(uri, StringComparison.Ordinal)).IsTrue();
            AssertThat(failure.Contains(context, StringComparison.OrdinalIgnoreCase)).IsTrue();
        }
        try
        {
            Directory.CreateDirectory(directory);
            AssertThat(ResourceSaver.Save(new Resource(), unused)).IsEqual(Error.Ok);
            WriteExtra(Plain(texture));
            authored.SetMeta("opaque", ResourceLoader.Load<Resource>(extra, cacheMode: ResourceLoader.CacheMode.IgnoreDeep));
            AssertThat(ResourceSaver.Save(authored, uri)).IsEqual(Error.Ok);
            Hash(uri, extra, texture, texture + ".import");
            var clean = NativeBattleVisualProfile.LoadSprite(uri, expected, hashes);
            AssertThat(clean.Frames[0].Indices.SequenceEqual(new byte[] { 4, 19 })).IsTrue();
            AssertThat(clean.GetMeta("opaque").AsGodotObject() is Resource).IsTrue();
            foreach (string invalid in new[] { "", "not-a-hash", new string('A', 64) })
            {
                string saved = hashes[extra]; hashes[extra] = invalid; Reject("SHA-256"); hashes[extra] = saved;
            }
            WriteExtra(Plain(extra)); Hash(uri, extra); Reject("cyclic native content");
            string unknown = root + "/Unknown.txt";
            File.WriteAllText(ProjectSettings.GlobalizePath(unknown), "not a supported native resource");
            WriteExtra(Plain(unknown)); Hash(uri, extra, unknown); Reject("unsupported native content dependency type");
            WriteExtra(Plain("user://outside.tres")); Hash(uri, extra); Reject("normalized res://");
            WriteExtra(Plain(root + "/Missing.tres")); Hash(uri, extra); Reject("SHA-256");
            foreach (bool matching in new[] { false, true })
            {
                long uid = ResourceUid.CreateId();
                try
                {
                    ResourceUid.AddId(uid, matching ? unused : root + "/Other.tres");
                    WriteExtra(Plain(unused, " uid=\"" + ResourceUid.IdToText(uid) + "\"")); Hash(uri, extra, unused);
                    if (matching) AssertThat(NativeBattleVisualProfile.LoadSprite(uri, expected, hashes).SpriteId).IsEqual(73);
                    else Reject("UID/fallback identity mismatch");
                }
                finally { if (ResourceUid.HasId(uid)) ResourceUid.RemoveId(uid); }
            }
            // Actual PNG bytes without a prepared remap must fail before the loader/importer can run.
            string png = root + "/Unprepared.png";
            using var image = Image.CreateEmpty(2, 1, false, Image.Format.Rgba8);
            image.Fill(Colors.Magenta);
            AssertThat(image.SavePng(ProjectSettings.GlobalizePath(png))).IsEqual(Error.Ok);
            AssertThat(Godot.FileAccess.FileExists(png + ".import")).IsFalse();
            WriteExtra(Plain(png)); Hash(uri, extra, png); Reject("Unprepared.png.import");
            // A native wrong root type with its own correct exact manifest is not a valid sprite.
            AssertThat(ResourceSaver.Save(new Resource(), uri)).IsEqual(Error.Ok);
            Hash(uri); Reject("expected PalSprite");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [TestCase]
    public void DeclaredNumberSpriteCannotEscapeExactHashClosure()
    {
        using var f = new NativeBattleVisualProfileFixture();
        f.Story.Save(f.Enemy, "Numbers.tres");
        f.Profile.NumberSpriteUri = f.Root + "/Numbers.tres";
        f.Save();
        AssertThat(f.Story.Read("Visual.tres").Contains("NumberSpriteUri = \"" + f.Root + "/Numbers.tres\"")).IsTrue();
        bool rejected = false;
        try { _ = f.Load(); }
        catch (InvalidDataException error) { rejected = error.Message.Contains("SHA-256"); }
        AssertThat(rejected).IsTrue();
    }

    [TestCase]
    public void AbsentNumbersRemainFormatValidButCannotSelectNumberPresentation()
    {
        using var f = new NativeBattleVisualProfileFixture();
        var visual = f.Load();
        AssertThat(visual.NumberSprite is null).IsTrue();
        AssertThat(visual.BlueNumberFrames.Count).IsEqual(0);
        AssertThat(Failure(visual.RequireNumberPresentation).Contains("NumberSpriteUri")).IsTrue();
    }

    [TestCase]
    public void NumberAliasesRetainThreeLiteralDigitGroupsAndIsolatedNativePlanes()
    {
        using var f = new NativeBattleVisualProfileFixture();
        f.AddNumberSprite();
        var visual = f.Load();
        visual.RequireNumberPresentation();
        AssertThat(visual.NumberSprite!.FrameCount).IsEqual(1);
        AssertThat(visual.YellowNumberFrames.Select(x => x.Index).SequenceEqual(new[] { 19,20,21,22,23,24,25,26,27,28 })).IsTrue();
        AssertThat(visual.BlueNumberFrames.Select(x => x.Index).SequenceEqual(new[] { 29,30,31,32,33,34,35,36,37,38 })).IsTrue();
        AssertThat(visual.CyanNumberFrames.Select(x => x.Index).SequenceEqual(new[] { 56,57,58,59,60,61,62,63,64,65 })).IsTrue();
        AssertThat(visual.BlueNumberFrames[0].Indices.SequenceEqual(new byte[] { 29, 79 })).IsTrue();
        AssertThat(visual.BlueNumberFrames[0].Alpha.SequenceEqual(new byte[] { 17, 255 })).IsTrue();
        AssertThat(ReferenceEquals(visual.NumberSprite.Frames.Single(x => x.Index == 29), visual.BlueNumberFrames[0])).IsTrue();
        AssertThat(((IList<PalFrame>)visual.BlueNumberFrames).IsReadOnly).IsTrue();
        AssertThat(visual.ResourceSha256[f.Profile.NumberSpriteUri]).IsEqual(Godot.FileAccess.GetSha256(f.Profile.NumberSpriteUri));
        visual.BlueNumberFrames[0].Indices[0] = 199;
        AssertThat(f.Load().BlueNumberFrames[0].Indices[0]).IsEqual((byte)29);
    }

    [TestCase(19)]
    [TestCase(38)]
    [TestCase(65)]
    public void DeclaredNumberSpriteRequiresAllThreeAddressableDigitGroups(int missing)
    {
        using var f = new NativeBattleVisualProfileFixture();
        f.AddNumberSprite();
        var sprite = f.Load().NumberSprite!;
        sprite.Frames.Remove(sprite.Frames.Single(x => x.Index == missing));
        f.Story.Save(sprite, "Numbers.tres");
        f.Profile.ResourceSha256[f.Profile.NumberSpriteUri] = Godot.FileAccess.GetSha256(f.Profile.NumberSpriteUri);
        f.Save();
        AssertThat(Failure(() => f.Load()).Contains("number sprite requires addressable")).IsTrue();
    }

    [TestCase]
    public void DeclaredAttackEffectCannotEscapeExactHashClosure()
    {
        using var f = new NativeBattleVisualProfileFixture();
        f.Story.Save(f.Enemy, "Effect.tres");
        f.Profile.Players[0].AttackEffectSpriteUri = f.Root + "/Effect.tres";
        f.Profile.Players[0].AttackEffectIndex = 0;
        f.Save();
        string serialized = f.Story.Read("Visual.tres");
        AssertThat(serialized.Contains("AttackEffectSpriteUri = \"" + f.Root + "/Effect.tres\"")).IsTrue();
        AssertThat(serialized.Contains("AttackEffectIndex = 0")).IsTrue();
        bool rejected = false;
        try { _ = f.Load(); }
        catch (InvalidDataException error) { rejected = error.Message.Contains("SHA-256"); }
        AssertThat(rejected).IsTrue();
    }

    [TestCase]
    public void MissingVisualRootIsRejectedThroughPublicLoader()
    {
        using var fixture = new NativeStoryProfileFixture();
        var story = NativeStoryProfile.Load(fixture.Uri);
        AssertThat(Failure(() => NativeBattleVisualProfile.Load(fixture.Root + "/Missing.tres", story)).Contains("missing")).IsTrue();
    }

    [TestCase]
    public void NativeSaveLoadBindsRoleTeamAndTargetWithoutConflatingSpriteIdentity()
    {
        using var f = new NativeBattleVisualProfileFixture();
        var visual = f.Load();
        AssertThat(visual.Players.Count).IsEqual(1);
        AssertThat(visual.Players[0].RoleId).IsEqual(0);
        AssertThat(visual.Players[0].PlayerSprite.SpriteId).IsEqual(41);
        var slot = visual.Teams[18].EnemySlots.Single();
        AssertThat(slot.TargetId).IsEqual(7);
        AssertThat(slot.EnemySprite.SpriteId).IsEqual(97);
        AssertThat(visual.Players[0].PlayerPosition).IsEqual(new Vector2I(240, 170));
        AssertThat(slot.EnemyPosition).IsEqual(new Vector2I(100, 120));
        AssertThat(slot.ActWaitFrames).IsEqual(0);
        AssertThat(visual.Teams[18].Background.Alpha[319]).IsEqual((byte)255);
        AssertThat(visual.Players[0].PlayerSprite.Frames[9].Indices.SequenceEqual(new byte[] { 4, 19 })).IsTrue();
        AssertThat(visual.Players[0].PlayerSprite.Frames[9].Alpha.SequenceEqual(new byte[] { 0, 255 })).IsTrue();
        AssertThat(visual.Players[0].PlayerSprite.Frames[9].Texture is null).IsTrue();
        AssertThat(visual.ResourceSha256.Keys.Order().SequenceEqual(f.ExpectedPaths.Order())).IsTrue();
        AssertThat(((IDictionary<int, CheckedBattleVisualTeam>)visual.Teams).IsReadOnly).IsTrue();
        AssertThat(((IDictionary<string, string>)visual.ResourceSha256).IsReadOnly).IsTrue();
        f.Player.Frames[9].Indices[0] = 99; f.Team.EnemySlots[0].TargetId = 44; f.Palette.Rgb[0] = 12; f.Profile.ResourceSha256.Clear();
        AssertThat(visual.Players[0].PlayerSprite.Frames[9].Indices[0]).IsEqual((byte)4);
        AssertThat(slot.TargetId).IsEqual(7);
        AssertThat(visual.Palette.Rgb[0]).IsEqual((byte)0);
        AssertThat(visual.ResourceSha256.Count).IsEqual(4);
        // A mutation of this isolated native instance must not poison a later load of the same URI.
        visual.Players[0].PlayerSprite.Frames[0].Indices[0] = 123;
        AssertThat(f.Load().Players[0].PlayerSprite.Frames[0].Indices[0]).IsEqual((byte)4);
    }

    private static void NativeSlotFrameMetadataNeverFallsBackToTeam()
    {
        using var f = new NativeBattleVisualProfileFixture(party: true);
        foreach (var slot in f.Team.EnemySlots)
        {
            slot.IdleFrames = 1; slot.IdleAnimSpeed = 0; slot.MagicFrames = 1;
            slot.AttackFrames = 1; slot.ActWaitFrames = 0;
        }
        f.Team.EnemySlots[1].IdleFrames = -1;
        f.Save();
        InvalidDataException? failure = null;
        try { _ = f.Load(); }
        catch (InvalidDataException error) { failure = error; }
        AssertThat(failure is not null)
            .OverrideFailureMessage("Native slot 1 must require its own complete frame metadata instead of accepting team metadata.")
            .IsTrue();
        AssertThat(failure!.Message.Contains("EnemySlots[1]")).IsTrue();
        AssertThat(failure.Message.Contains("metadata WORDs")).IsTrue();
    }

    private static void NativeSlotFrameMetadataRoundtripsIndependentlyAndChecksSecondSlotFrames()
    {
        using var f = new NativeBattleVisualProfileFixture(party: true);
        f.Team.EnemySlots[0].MagicFrames = 0; f.Team.EnemySlots[0].AttackFrames = 0;
        f.Team.EnemySlots[1].IdleAnimSpeed = 65535; f.Team.EnemySlots[1].ActWaitFrames = 65535;
        f.Save();
        var slots = f.Load().Teams[18].EnemySlots;
        AssertThat((slots[0].IdleFrames, slots[0].IdleAnimSpeed, slots[0].MagicFrames, slots[0].AttackFrames, slots[0].ActWaitFrames))
            .IsEqual((1, 0, 0, 0, 0));
        AssertThat((slots[1].IdleFrames, slots[1].IdleAnimSpeed, slots[1].MagicFrames, slots[1].AttackFrames, slots[1].ActWaitFrames))
            .IsEqual((1, 65535, 1, 1, 65535));
        AssertThat(slots[0].TargetId == 7 && slots[1].TargetId == 7).IsTrue();
        f.Team.EnemySlots[1].AttackFrames = 2; f.Save();
        string failure = Failure(() => f.Load());
        AssertThat(failure.Contains("EnemySlots[1]") && failure.Contains("unaddressable frames")).IsTrue();
        f.Team.EnemySlots[1].AttackFrames = 1; f.Save();
        AssertThat(f.Load().Teams[18].EnemySlots[1].AttackFrames).IsEqual(1);
        AssertThat(slots[1].AttackFrames).IsEqual(1);
    }

    private static void NativeSlotFrameMetadataRejectsMissingAndLegacyMixedDeclarations(string field)
    {
        using var f = new NativeBattleVisualProfileFixture(party: true);
        var slotProperty = typeof(PalBattleVisualEnemySlot).GetProperty(field)!;
        int original = (int)slotProperty.GetValue(f.Team.EnemySlots[1])!;
        slotProperty.SetValue(f.Team.EnemySlots[1], -1); f.Save();
        string missing = Failure(() => f.Load());
        AssertThat(missing.Contains("EnemySlots[1]") && missing.Contains("metadata WORDs"))
            .OverrideFailureMessage("EnemySlots[1]." + field + " missing: " + missing).IsTrue();
        slotProperty.SetValue(f.Team.EnemySlots[1], original);
        typeof(PalBattleVisualTeam).GetProperty(field)!.SetValue(f.Team, original); f.Save();
        string mixed = Failure(() => f.Load());
        AssertThat(mixed.Contains("legacy team frame metadata"))
            .OverrideFailureMessage("Legacy team " + field + " mixed with EnemySlots: " + mixed).IsTrue();
    }

    [TestCase]
    public void MissingExplicitVersionIsNotAcceptedAsVersionTwo()
    {
        using var f = new NativeBattleVisualProfileFixture();
        NativeProfileFixture.SaveResource(new PalBattleVisualProfile(), f.Uri);
        AssertThat(Failure(() => f.Load()).Contains("FormatVersion must be 2")).IsTrue();
    }

    [TestCase("version")]
    [TestCase("identity")]
    [TestCase("role")]
    [TestCase("missing-team")]
    [TestCase("extra-team")]
    [TestCase("duplicate-team")]
    [TestCase("target-is-sprite")]
    [TestCase("target-is-team")]
    [TestCase("player-position")]
    [TestCase("enemy-position")]
    [TestCase("idle-zero")]
    [TestCase("magic-negative")]
    [TestCase("attack-overflow")]
    [TestCase("wait-negative")]
    [TestCase("metadata-frames")]
    public void AuthoredMappingAndPresentationMetadataMustMatchSelectedStory(string fault)
    {
        using var f = new NativeBattleVisualProfileFixture();
        string expected;
        switch (fault)
        {
            case "version": f.Profile.FormatVersion = 1; expected = "FormatVersion"; break;
            case "identity": f.Profile.SourceIdentity = " "; expected = "SourceIdentity"; break;
            case "role": f.Profile.Players[0].RoleId = 41; expected = "Players[0].RoleId must match the battle party role"; break;
            case "missing-team": f.Profile.Teams = []; expected = "exactly cover"; break;
            case "extra-team": f.Profile.Teams = [f.Team, new PalBattleVisualTeam { TeamId = 19 }]; expected = "does not match a story battle team"; break;
            case "duplicate-team": f.Profile.Teams = [f.Team, f.Team]; expected = "duplicate"; break;
            case "target-is-sprite": f.Team.EnemySlots[0].TargetId = 97; expected = "TargetId"; break;
            case "target-is-team": f.Team.EnemySlots[0].TargetId = 18; expected = "TargetId"; break;
            case "player-position": f.Profile.Players[0].PlayerPosition = new(32768, 0); expected = "SHORT"; break;
            case "enemy-position": f.Team.EnemySlots[0].EnemyPosition = new(0, -32769); expected = "SHORT"; break;
            case "idle-zero": f.Team.EnemySlots[0].IdleFrames = 0; expected = "metadata WORDs"; break;
            case "magic-negative": f.Team.EnemySlots[0].MagicFrames = -1; expected = "metadata WORDs"; break;
            case "attack-overflow": f.Team.EnemySlots[0].AttackFrames = 65536; expected = "metadata WORDs"; break;
            case "wait-negative": f.Team.EnemySlots[0].ActWaitFrames = -1; expected = "metadata WORDs"; break;
            default: f.Team.EnemySlots[0].AttackFrames = 2; expected = "unaddressable frames"; break;
        }
        f.Save();
        AssertThat(Failure(() => f.Load()).Contains(expected)).IsTrue();
    }

    [TestCase("frame-missing")]
    [TestCase("frame-duplicate")]
    [TestCase("frame-index")]
    [TestCase("frame-width")]
    [TestCase("frame-indices")]
    [TestCase("frame-alpha")]
    [TestCase("frame-source-offset")]
    [TestCase("sprite-source")]
    [TestCase("sprite-id")]
    [TestCase("player-count")]
    [TestCase("palette-size")]
    [TestCase("palette-mismatch")]
    [TestCase("background-identity")]
    [TestCase("background-size")]
    [TestCase("background-plane")]
    public void NativeLeafPlanesFramesPaletteAndDeclarationsAreStrictlyChecked(string fault)
    {
        using var f = new NativeBattleVisualProfileFixture();
        string expected;
        switch (fault)
        {
            case "frame-missing": f.Player.Frames.RemoveAt(5); expected = "nominal frame missing"; break;
            case "frame-duplicate": f.Player.Frames[5].Index = 4; expected = "duplicate"; break;
            case "frame-index": f.Player.Frames[5].Index = 65536; expected = "Index"; break;
            case "frame-width": f.Player.Frames[5].Width = 0; expected = "planes"; break;
            case "frame-indices": f.Player.Frames[5].Indices = [4]; expected = "planes"; break;
            case "frame-alpha": f.Enemy.Frames[2].Alpha = []; expected = "planes"; break;
            case "frame-source-offset": f.Player.Frames[0].SourceOffset = -1; expected = "planes"; break;
            case "sprite-source": f.Player.Source.Clear(); expected = "source declaration"; break;
            case "sprite-id": f.Player.SpriteId = -1; expected = "SpriteId"; break;
            case "player-count": f.Player.FrameCount = 9; f.Player.Frames.RemoveAt(9); expected = "frames 0..9"; break;
            case "palette-size": f.Palette.Rgb = new byte[767]; expected = "RGB768"; break;
            case "palette-mismatch": f.Palette.Rgb[0] = 255; expected = "active story palette"; break;
            case "background-identity": f.Background.SourceIdentity = ""; expected = "SourceIdentity"; break;
            case "background-size": f.Background.Width = 1; f.Background.Height = 64000; expected = "320x200"; break;
            default: f.Background.Alpha = [255]; expected = "planes"; break;
        }
        f.SaveLeaves(); f.RefreshHashes(); f.Save();
        AssertThat(Failure(() => f.Load()).Contains(expected)).IsTrue();
    }

    [TestCase]
    public void ExtraAddressableAliasesZeroAttackAndSignedPositionsArePreserved()
    {
        using var f = new NativeBattleVisualProfileFixture();
        f.Player.Frames.Add(new PalFrame { Index = 65535, Width = 1, Height = 1, Indices = [22], Alpha = [128] });
        // Addressable aliases may satisfy presentation indices beyond the nominal source frame count.
        f.Player.FrameCount = 9;
        f.Enemy.FrameCount = 1;
        f.Team.EnemySlots[0].MagicFrames = 0; f.Team.EnemySlots[0].AttackFrames = 0;
        f.Team.EnemySlots[0].EnemyPosition = new(-32768, 32767);
        f.SaveLeaves(); f.RefreshHashes(); f.Save();
        var visual = f.Load();
        AssertThat(visual.Players[0].PlayerSprite.Frames.Last().Index).IsEqual(65535);
        AssertThat(visual.Teams[18].EnemySlots[0].AttackFrames).IsEqual(0);
        AssertThat(visual.Teams[18].EnemySlots[0].EnemyPosition).IsEqual(new Vector2I(-32768, 32767));
        f.Team.EnemySlots[0].MagicFrames = 1; f.Team.EnemySlots[0].AttackFrames = 1; f.Save();
        AssertThat(f.Load().Teams[18].EnemySlots[0].AttackFrames).IsEqual(1);
    }

    [TestCase]
    public void PartySlotsBindStoryEnemySlotsWithoutMergingDuplicateIds()
    {
        NativeSlotFrameMetadataNeverFallsBackToTeam();
        NativeSlotFrameMetadataRoundtripsIndependentlyAndChecksSecondSlotFrames();
        foreach (string field in new[] { "IdleFrames", "IdleAnimSpeed", "MagicFrames", "AttackFrames", "ActWaitFrames" })
            NativeSlotFrameMetadataRejectsMissingAndLegacyMixedDeclarations(field);
        using var f = new NativeBattleVisualProfileFixture(party: true);
        var visual = f.Load();
        var team = visual.Teams[18];
        AssertThat(team.EnemySlots.Count).IsEqual(2);
        AssertThat(team.EnemySlots.Select(s => s.TargetId).SequenceEqual(new[] { 7, 7 })).IsTrue();
        AssertThat(team.EnemySlots.Select(s => s.EnemyPosition).SequenceEqual(new[] { new Vector2I(100, 120), new Vector2I(128, 104) })).IsTrue();
        AssertThat(visual.Players.Count).IsEqual(2);
        AssertThat(visual.Players[0].PlayerPosition).IsEqual(new Vector2I(200, 176));
        AssertThat(visual.Players[1].PlayerPosition).IsEqual(new Vector2I(256, 152));
        AssertThat(((IList<CheckedBattleVisualEnemySlot>)team.EnemySlots).IsReadOnly).IsTrue();
        // The public checked contract must force every consumer to address an enemy slot.
        string[] enemyScalars = ["TargetId", "EnemyPosition", "EnemySprite", "IdleFrames", "IdleAnimSpeed",
            "MagicFrames", "AttackFrames", "ActWaitFrames"];
        AssertThat(typeof(CheckedBattleVisualTeam).GetProperties().Any(property => enemyScalars.Contains(property.Name)))
            .OverrideFailureMessage("Checked enemy visual data must exist only on EnemySlots, not team scalar projections.")
            .IsFalse();
        AssertThat(ReferenceEquals(team.EnemySlots[0], team.EnemySlots[1])).IsFalse();
        foreach (var slot in team.EnemySlots)
        {
            AssertThat(slot.EnemySprite.SpriteId).IsEqual(97);
            AssertThat((slot.IdleFrames, slot.IdleAnimSpeed, slot.MagicFrames, slot.AttackFrames, slot.ActWaitFrames))
                .IsEqual((1, 0, 1, 1, 0));
        }
        f.Team.EnemySlots[1].IdleFrames = 2; f.Team.EnemySlots[1].IdleAnimSpeed = 65535;
        f.Team.EnemySlots[1].EnemyPosition = new(0, 0);
        AssertThat(team.EnemySlots[1].EnemyPosition).IsEqual(new Vector2I(128, 104));
        AssertThat(team.EnemySlots[1].IdleFrames).IsEqual(1);
        AssertThat(team.EnemySlots[1].IdleAnimSpeed).IsEqual(0);
        team.EnemySlots[1].EnemySprite.Frames[0].Indices[0] = 123;
        var fresh = f.Load();
        AssertThat(fresh.Teams[18].EnemySlots.All(slot => slot.EnemySprite.Frames[0].Indices[0] == 4)).IsTrue();
    }

    [TestCase]
    public void ExplicitSingleSlotRequiresNativeSlotsAndRejectsLegacyMixing()
    {
        using var f = new NativeBattleVisualProfileFixture();
        var slot = new PalBattleVisualEnemySlot { TargetId = 7, EnemyPosition = new(100, 120),
            EnemySpriteUri = f.Team.EnemySlots[0].EnemySpriteUri,
            IdleFrames = 1, IdleAnimSpeed = 0, MagicFrames = 1, AttackFrames = 1, ActWaitFrames = 0 };
        f.Team.TargetId = -1;
        f.Team.EnemyPosition = new(int.MinValue, int.MinValue);
        f.Team.EnemySlots = [slot];
        f.Save();
        var visual = f.Load();
        AssertThat(visual.Teams[18].EnemySlots.Count).IsEqual(1);
        AssertThat(visual.Teams[18].EnemySlots[0].TargetId).IsEqual(7);
        AssertThat(visual.Teams[18].EnemySlots[0].EnemyPosition).IsEqual(new Vector2I(100, 120));

        f.Team.EnemySlots = [];
        f.Team.TargetId = 7;
        f.Team.EnemyPosition = new(100, 120);
        f.Save();
        InvalidDataException? missingSlots = null;
        try { _ = f.Load(); }
        catch (InvalidDataException error) { missingSlots = error; }
        AssertThat(missingSlots is not null)
            .OverrideFailureMessage("Single-enemy native visuals must reject the legacy pair without EnemySlots.")
            .IsTrue();
        AssertThat(missingSlots!.Message.Contains("requires visual EnemySlots")).IsTrue();
        f.Team.TargetId = -1;
        f.Team.EnemyPosition = new(int.MinValue, int.MinValue);
        f.Save();
        AssertThat(Failure(() => f.Load()).Contains("requires visual EnemySlots")).IsTrue();
        f.Team.EnemySlots = [slot];
        foreach (var pair in new[] { (7, new Vector2I(int.MinValue, int.MinValue)),
            (-1, new Vector2I(100, 120)), (7, new Vector2I(100, 120)) })
        {
            f.Team.TargetId = pair.Item1;
            f.Team.EnemyPosition = pair.Item2;
            f.Save();
            AssertThat(Failure(() => f.Load()).Contains("legacy TargetId/EnemyPosition")).IsTrue();
        }
    }

    [TestCase("legacy-pair")]
    [TestCase("slot-count")]
    [TestCase("slot-target")]
    [TestCase("slot-position")]
    [TestCase("player-origin")]
    [TestCase("single-origin")]
    public void PartyVisualSlotAndLayoutMetadataMustMatchSelectedStory(string fault)
    {
        using var f = new NativeBattleVisualProfileFixture(fault != "single-origin");
        string expected;
        switch (fault)
        {
            case "legacy-pair": f.Profile.Teams[0].EnemySlots = []; expected = "requires visual EnemySlots"; break;
            case "slot-count": f.Profile.Teams[0].EnemySlots = [f.Profile.Teams[0].EnemySlots[0]]; expected = "one-to-one"; break;
            case "slot-target": f.Profile.Teams[0].EnemySlots[1].TargetId = 8; expected = "EnemySlots TargetId"; break;
            case "slot-position": f.Profile.Teams[0].EnemySlots[0].EnemyPosition = new(0, -32769); expected = "SHORT"; break;
            case "player-origin": f.Profile.Players[0].PlayerPosition = new(240, 170); expected = "g_rgPlayerPos"; break;
            default: f.Profile.Players[0].PlayerPosition = new(200, 176); expected = "g_rgPlayerPos"; break;
        }
        f.Save();
        AssertThat(Failure(() => f.Load()).Contains(expected)).IsTrue();
    }

    [TestCase("missing-hash")]
    [TestCase("extra-hash")]
    [TestCase("changed-bytes")]
    [TestCase("missing-file")]
    [TestCase("wrong-type")]
    [TestCase("bad-uri")]
    [TestCase("root-self-hash")]
    public void ExactHashGraphRejectsMissingMixedAndUnclaimedResources(string fault)
    {
        using var f = new NativeBattleVisualProfileFixture();
        string path = f.Profile.Players[0].PlayerSpriteUri;
        string expected;
        switch (fault)
        {
            case "missing-hash": f.Profile.ResourceSha256.Remove(path); expected = "SHA-256"; break;
            case "extra-hash": f.Profile.ResourceSha256.Add(f.Story.Root + "/Numeric.tres", Godot.FileAccess.GetSha256(f.Story.Root + "/Numeric.tres")); expected = "exactly"; break;
            case "changed-bytes": File.AppendAllText(ProjectSettings.GlobalizePath(path), "\n;changed\n"); expected = "SHA-256"; break;
            case "missing-file": File.Delete(ProjectSettings.GlobalizePath(path)); expected = "missing"; break;
            case "wrong-type": f.Story.Save(new PalPalette(), "Player.tres"); f.RefreshHashes(); expected = "expected PalSprite"; break;
            case "bad-uri": f.Profile.Players[0].PlayerSpriteUri = f.Root + "/../Player.tres"; expected = "normalized res://"; break;
            default: f.Profile.ResourceSha256.Add(f.Uri, Godot.FileAccess.GetSha256(f.Uri)); expected = "SHA-256"; break;
        }
        f.Save();
        AssertThat(Failure(() => f.Load()).Contains(expected)).IsTrue();
    }

    [TestCase]
    public void ExternalFrameAndMetadataAreIncludedInExactManifest()
    {
        using var f = new NativeBattleVisualProfileFixture();
        f.Story.Save(f.Player.Frames[0], "Frame.tres");
        f.Player.Frames[0] = ResourceLoader.Load<PalFrame>(f.Root + "/Frame.tres", cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
        f.Story.Save(f.Team, "Team.tres");
        f.Profile.Teams = [ResourceLoader.Load<PalBattleVisualTeam>(f.Root + "/Team.tres", cacheMode: ResourceLoader.CacheMode.IgnoreDeep)];
        f.SaveLeaves(); f.RefreshHashes();
        foreach (string name in new[] { "Frame.tres", "Team.tres" }) f.Profile.ResourceSha256.Add(f.Root + "/" + name, Godot.FileAccess.GetSha256(f.Root + "/" + name));
        f.Save();
        var visual = f.Load();
        AssertThat(visual.ResourceSha256.Count).IsEqual(6);
        AssertThat(visual.Players[0].PlayerSprite.Frames[0].Alpha[1]).IsEqual((byte)255);
    }

    [TestCase("cycle")]
    [TestCase("unknown-type")]
    [TestCase("external-uri")]
    [TestCase("uid-mismatch")]
    [TestCase("uid-match")]
    [TestCase("missing-external-metadata")]
    public void ExternalDependencyGraphIsCheckedBeforeDeepLoading(string fault)
    {
        using var f = new NativeBattleVisualProfileFixture();
        string extra = f.Root + "/Extra.tres";
        f.Story.Write("Extra.tres", "[gd_resource type=\"Resource\" format=3]\n[resource]\n");
        string target = extra, uidPart = "", expected = "";
        long uid = -1;
        try
        {
            switch (fault)
            {
                case "cycle":
                    f.Story.Write("Extra.tres", $"[gd_resource type=\"Resource\" format=3]\n[ext_resource type=\"Resource\" path=\"{extra}\" id=\"self\"]\n[resource]\nmetadata/next = ExtResource(\"self\")\n"); expected = "cyclic native content"; break;
                case "unknown-type": f.Story.Write("Extra.txt", "not native data"); target = f.Root + "/Extra.txt"; expected = "unsupported native content dependency type"; break;
                case "external-uri": target = "user://outside.tres"; expected = "normalized res://"; break;
                case "missing-external-metadata": File.Delete(ProjectSettings.GlobalizePath(extra)); expected = "missing native resource"; break;
                default:
                    uid = ResourceUid.CreateId();
                    ResourceUid.AddId(uid, fault == "uid-match" ? extra : f.Root + "/Other.tres");
                    uidPart = " uid=\"" + ResourceUid.IdToText(uid) + "\"";
                    expected = "UID/fallback identity mismatch"; break;
            }
            // Attach opaque Resource metadata, not a new business entry or test hook.
            string text = f.Story.Read("Visual.tres");
            text = text.Insert(text.IndexOf('\n') + 1, $"\n[ext_resource type=\"Resource\"{uidPart} path=\"{target}\" id=\"extra\"]\n");
            f.Story.Write("Visual.tres", text + "\nmetadata/extra = ExtResource(\"extra\")\n");
            if (fault == "uid-match")
            {
                f.Profile.ResourceSha256.Add(extra, Godot.FileAccess.GetSha256(extra));
                f.Save();
                text = f.Story.Read("Visual.tres");
                text = text.Insert(text.IndexOf('\n') + 1, $"\n[ext_resource type=\"Resource\"{uidPart} path=\"{target}\" id=\"extra\"]\n");
                f.Story.Write("Visual.tres", text + "\nmetadata/extra = ExtResource(\"extra\")\n");
                AssertThat(f.Load().ResourceSha256.Count).IsEqual(5);
            }
            else AssertThat(Failure(() => f.Load()).Contains(expected)).IsTrue();
        }
        finally { if (uid != -1 && ResourceUid.HasId(uid)) ResourceUid.RemoveId(uid); }
    }

    [TestCase]
    public void WrongRootTypeAndUnselectedNumericTeamDoNotGrantVisualCapabilities()
    {
        using var f = new NativeBattleVisualProfileFixture();
        f.Story.Numeric.Teams = [f.Story.Numeric.Teams[0], new PalBattleTeam { TeamId = 19,
            Enemies = [new PalBattleEnemy { Id = 99, Hp = 43, PhysicalResistance = 1, Cash = 1, Statuses = new int[9], Poisons = [0] }] }];
        f.Story.Save(f.Story.Numeric, "Numeric.tres"); f.Story.RefreshHashes(); f.Story.SaveProfile();
        var story = NativeStoryProfile.Load(f.Story.Uri);
        AssertThat(NativeBattleVisualProfile.Load(f.Uri, story).Teams.Count).IsEqual(1);
        AssertThat(story.BattleContent.Teams[19].Cash).IsEqual(1);
        f.Story.Save(new Resource(), "Visual.tres");
        AssertThat(Failure(() => NativeBattleVisualProfile.Load(f.Uri, story)).Contains("expected PalBattleVisualProfile")).IsTrue();
    }

    [TestCase]
    public void LegacyScalarProfilesAreRejectedForExplicitRepreparation()
    {
        using var f = new NativeBattleVisualProfileFixture();
        var visual = f.Load();
        AssertThat(visual.Players[0].AttackEffectSprite is null && visual.Players[0].AttackEffectIndex == -1
            && visual.Players[0].AttackEffectFrames.Count == 0).IsTrue();
        AssertThat(Failure(visual.Players[0].RequireReferencePresentation).Contains("attack effect")).IsTrue();
        AssertThat(Failure(visual.RequireReferencePresentation).Contains("attack effect")).IsTrue();
        // Legacy scalar FormatVersion=1 declarations are rejected with a re-prepare diagnostic;
        // the runtime never folds or reinterprets them into the typed per-role form.
        f.Profile.FormatVersion = 1;
        f.Save();
        AssertThat(Failure(() => f.Load()).Contains("FormatVersion must be 2")).IsTrue();
        f.Profile.FormatVersion = 2;
        f.Profile.Players = [];
        f.Save();
        AssertThat(Failure(() => f.Load()).Contains("Players requires typed per-role bindings")).IsTrue();
        f.Profile.Players = [new PalBattleVisualPlayer { RoleId = 0, PlayerSpriteUri = f.Root + "/Player.tres",
            PlayerPosition = new(240, 170) }];
        AddEffect(f, 0); f.Team.EnemySlots[0].IdleAnimSpeed = -1; f.Save();
        AssertThat(Failure(() => f.Load()).Contains("metadata WORDs")).IsTrue();
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(65535)]
    public void EffectGroupsUseIntTriplesNotRoleIdsOrWordWrappedFrames(int group)
    {
        using var f = new NativeBattleVisualProfileFixture();
        AddEffect(f, group); f.Team.EnemySlots[0].IdleAnimSpeed = 0; f.Save();
        if (group == 65535)
        {
            AssertThat(Failure(() => f.Load()).Contains("unaddressable frames")).IsTrue();
            return;
        }
        var visual = f.Load(); visual.RequireReferencePresentation();
        AssertThat(visual.Players[0].AttackEffectIndex == group && visual.Players[0].AttackEffectSprite!.SpriteId == 97).IsTrue();
        AssertThat(visual.Players[0].AttackEffectFrames.Select(x => x.Index).SequenceEqual(group == 0 ? new[] { 0, 1, 2 } : new[] { 3, 4, 5 })).IsTrue();
        AssertThat(visual.Teams[18].EnemySlots[0].IdleAnimSpeed).IsEqual(0);
        AssertThat(((IList<PalFrame>)visual.Players[0].AttackEffectFrames).IsReadOnly).IsTrue();
        File.Delete(ProjectSettings.GlobalizePath(f.Root + "/Effect.tres"));
        AssertThat(visual.Players[0].AttackEffectFrames[0].Indices.SequenceEqual(new byte[] { 4, 19 })).IsTrue();
        AssertThat(visual.Players[0].AttackEffectFrames[0].Alpha.SequenceEqual(new byte[] { 0, 255 })).IsTrue();
    }

    [TestCase("uri-only")]
    [TestCase("index-only")]
    [TestCase("negative")]
    [TestCase("overflow")]
    [TestCase("missing-frame")]
    [TestCase("wrong-type")]
    [TestCase("changed")]
    [TestCase("idle-negative")]
    [TestCase("idle-overflow")]
    public void PresentEffectAndIdleMetadataAreValidatedEvenWithoutCapabilitySelection(string fault)
    {
        using var f = new NativeBattleVisualProfileFixture(); AddEffect(f, 0);
        string expected;
        switch (fault)
        {
            case "uri-only": f.Profile.Players[0].AttackEffectIndex = -1; expected = "paired"; break;
            case "index-only": f.Profile.Players[0].AttackEffectSpriteUri = ""; expected = "paired"; break;
            case "negative": f.Profile.Players[0].AttackEffectIndex = -2; expected = "WORD"; break;
            case "overflow": f.Profile.Players[0].AttackEffectIndex = 65536; expected = "WORD"; break;
            case "missing-frame": f.Profile.Players[0].AttackEffectIndex = 1; expected = "unaddressable"; break;
            case "wrong-type": f.Story.Save(f.Palette, "Effect.tres"); f.Profile.ResourceSha256[f.Root + "/Effect.tres"] = Godot.FileAccess.GetSha256(f.Root + "/Effect.tres"); expected = "expected PalSprite"; break;
            case "changed": File.AppendAllText(ProjectSettings.GlobalizePath(f.Root + "/Effect.tres"), "\n; changed"); expected = "SHA-256"; break;
            case "idle-negative": f.Team.EnemySlots[0].IdleAnimSpeed = -2; expected = "WORD"; break;
            default: f.Team.EnemySlots[0].IdleAnimSpeed = 65536; expected = "WORD"; break;
        }
        f.Save(); AssertThat(Failure(() => f.Load()).Contains(expected)).IsTrue();
    }

    [TestCase]
    public void TypedPerRolePlayerBindingsFollowPartyOrderWithoutLeaderFallback()
    {
        using var f = new NativeBattleVisualProfileFixture(party: true);
        // Independent second-member literal: role1 draws its own planes, never the head's.
        var second = new PalSprite { SpriteId = 42, FrameCount = 10, DecodedByteLength = 200,
            Source = new Godot.Collections.Dictionary { ["archive"] = "synthetic second member frames", ["chunk"] = 42 } };
        for (int i = 0; i < 10; i++) second.Frames.Add(new PalFrame { Index = i, SourceOffset = 2 + i * 4,
            Width = 2, Height = 1, Indices = [19, 4], Alpha = [255, 0] });
        f.Story.Save(second, "Player2.tres");
        f.Story.Save(f.Enemy, "Effect.tres");
        f.Profile.FormatVersion = 2;
        f.Profile.Players =
        [
            new PalBattleVisualPlayer { RoleId = 0, PlayerSpriteUri = f.Root + "/Player.tres",
                PlayerPosition = new(200, 176), AttackEffectSpriteUri = f.Root + "/Effect.tres", AttackEffectIndex = 0 },
            new PalBattleVisualPlayer { RoleId = 1, PlayerSpriteUri = f.Root + "/Player2.tres",
                PlayerPosition = new(256, 152), AttackEffectSpriteUri = f.Root + "/Effect.tres", AttackEffectIndex = 0 }
        ];
        f.Profile.ResourceSha256[f.Root + "/Player2.tres"] = Godot.FileAccess.GetSha256(f.Root + "/Player2.tres");
        f.Profile.ResourceSha256[f.Root + "/Effect.tres"] = Godot.FileAccess.GetSha256(f.Root + "/Effect.tres");
        f.Save();
        // The new full-party public seam authenticates every party position's RoleId.
        var visual = NativeBattleVisualProfile.Load(f.Uri, new[] { 0, 1 },
            f.CheckedStory.BattleContent.EnemyTeams, f.CheckedStory.Requirements.BattleTeamIds, f.CheckedStory.Palette);
        AssertThat(visual.Players.Count).IsEqual(2);
        AssertThat(visual.Players[0].RoleId).IsEqual(0);
        AssertThat(visual.Players[0].PlayerSprite.SpriteId).IsEqual(41);
        AssertThat(visual.Players[0].PlayerPosition).IsEqual(new Vector2I(200, 176));
        AssertThat(visual.Players[1].RoleId).IsEqual(1);
        AssertThat(visual.Players[1].PlayerSprite.SpriteId).IsEqual(42);
        AssertThat(visual.Players[1].PlayerSprite.Frames[9].Indices.SequenceEqual(new byte[] { 19, 4 })).IsTrue();
        AssertThat(visual.Players[1].PlayerPosition).IsEqual(new Vector2I(256, 152));
        AssertThat(visual.PlayerOrigins(2).SequenceEqual(new[] { new Vector2I(200, 176), new Vector2I(256, 152) })).IsTrue();
        visual.RequireReferencePresentation();
        AssertThat(visual.Players[0].AttackEffectFrames.Select(x => x.Index).SequenceEqual(new[] { 0, 1, 2 })).IsTrue();
        AssertThat(visual.Players[1].AttackEffectFrames.Select(x => x.Index).SequenceEqual(new[] { 0, 1, 2 })).IsTrue();
        // The story overload authenticates the same full party order, not head+count.
        AssertThat(f.Load().Players[1].PlayerSprite.SpriteId).IsEqual(42);
        // No leader fallback: swapped roles reject at the first mismatched position.
        f.Profile.Players[0].RoleId = 1; f.Profile.Players[1].RoleId = 0; f.Save();
        AssertThat(Failure(() => f.Load()).Contains("Players[0].RoleId must match the battle party role")).IsTrue();
        f.Profile.Players[0].RoleId = 0; f.Profile.Players[1].RoleId = 1;
        f.Profile.Players = [f.Profile.Players[0]]; f.Save();
        AssertThat(Failure(() => f.Load()).Contains("one-to-one")).IsTrue();
        f.Profile.Players =
        [
            new PalBattleVisualPlayer { RoleId = 0, PlayerSpriteUri = f.Root + "/Player.tres",
                PlayerPosition = new(200, 176), AttackEffectSpriteUri = f.Root + "/Effect.tres", AttackEffectIndex = 0 },
            new PalBattleVisualPlayer { RoleId = 1, PlayerSpriteUri = f.Root + "/Player2.tres",
                PlayerPosition = new(256, 152), AttackEffectSpriteUri = f.Root + "/Effect.tres", AttackEffectIndex = 0 }
        ];
        f.Profile.Players[1].PlayerPosition = new(256, 151); f.Save();
        AssertThat(Failure(() => f.Load()).Contains("g_rgPlayerPos")).IsTrue();
        f.Profile.Players[1].PlayerPosition = new(256, 152); f.Save();
        // Each member's own frame range is required; a missing role1 frame rejects that entry.
        second.Frames.RemoveAt(9); second.FrameCount = 9;
        f.Story.Save(second, "Player2.tres");
        f.Profile.ResourceSha256[f.Root + "/Player2.tres"] = Godot.FileAccess.GetSha256(f.Root + "/Player2.tres");
        f.Save();
        AssertThat(Failure(() => f.Load()).Contains("frames 0..9")).IsTrue();
        // Sharing one legal sprite between two roles stays two distinct bindings.
        second.Frames.Add(new PalFrame { Index = 9, SourceOffset = 38, Width = 2, Height = 1, Indices = [19, 4], Alpha = [255, 0] });
        second.FrameCount = 10;
        f.Story.Save(second, "Player2.tres");
        f.Profile.ResourceSha256[f.Root + "/Player2.tres"] = Godot.FileAccess.GetSha256(f.Root + "/Player2.tres");
        f.Profile.Players[1].PlayerSpriteUri = f.Profile.Players[0].PlayerSpriteUri;
        // The exact manifest must drop the now-unreferenced member sprite; sharing stays two bindings.
        f.Profile.ResourceSha256.Remove(f.Root + "/Player2.tres");
        f.Save();
        var shared = f.Load();
        AssertThat(shared.Players.Count).IsEqual(2);
        AssertThat(shared.Players[0].PlayerSprite.SpriteId).IsEqual(41);
        AssertThat(shared.Players[1].PlayerSprite.SpriteId).IsEqual(41);
        AssertThat(ReferenceEquals(shared.Players[0], shared.Players[1])).IsFalse();
    }

    private static void AddEffect(NativeBattleVisualProfileFixture f, int group)
    {
        if (group == 1)
            for (int i = 3; i < 6; i++) f.Enemy.Frames.Add(new PalFrame { Index = i, Width = 2, Height = 1, Indices = [4, 19], Alpha = [0, 255] });
        f.Story.Save(f.Enemy, "Effect.tres");
        f.Profile.Players[0].AttackEffectSpriteUri = f.Root + "/Effect.tres"; f.Profile.Players[0].AttackEffectIndex = group;
        f.Profile.ResourceSha256.Add(f.Root + "/Effect.tres", Godot.FileAccess.GetSha256(f.Root + "/Effect.tres"));
    }

    private static string Failure(Action action)
    {
        try { action(); }
        catch (InvalidDataException error) { return error.Message; }
        throw new InvalidOperationException("Expected InvalidDataException");
    }
}
