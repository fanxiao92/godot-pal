using System.Text.Json.Nodes;
using static Pal.Extract.ExportSupport;

namespace Pal.Extract;

// export-battle-images (#92): decodes every abc.mkf chunk - the enemy battle sprites
// battle.c PAL_LoadBattleSprites reads by enemy definition ID - into a complete image
// package: index.json manifest, one sprite document per chunk with authoritative
// indices/alpha planes, and PNG frames colorized with pat.mkf chunk 0 day colors.
// The verb and manifest shape are frozen for #93-#95 (ball/f/fbp).
internal static class BattleImageExporter
{
    internal const string Archive = "abc";
    private const int MaximumChunkBytes = 8 * 1024 * 1024;
    private const int MaximumDecodedChunkBytes = 8 * 1024 * 1024;
    private const int MaximumAggregateDecodedBytes = 256 * 1024 * 1024;
    private const int MaximumAggregatePixels = 128 * 1024 * 1024;
    private static readonly string[] RequiredNames = ["abc.mkf", "pat.mkf"];

    internal static void Export(string input, string output)
    {
        output = ExportPaths.Validate(input, output, RequiredNames);
        var archives = MkfArchive.OpenRequired(input, RequiredNames, 65536);
        string staging = Path.Combine(Path.GetTempPath(), $"pal-battle-{Guid.NewGuid():N}");
        try
        {
            var abc = archives[0];
            var palette = DecodeResource("pat.mkf", 0, () => PaletteDecoder.Decode(archives[1].ReadChunk(0, 1536), night: false));
            Directory.CreateDirectory(staging);
            var entries = new List<JsonObject>();
            int decodedChunks = 0, emptyChunks = 0, frameTotal = 0, fileTotal = 0;
            int remainingDecoded = MaximumAggregateDecodedBytes, remainingPixels = MaximumAggregatePixels;
            foreach (var chunk in abc.Chunks)
            {
                byte[] raw = abc.ReadChunk(chunk.Index, MaximumChunkBytes);
                if (raw.Length == 0)
                {
                    entries.Add(BattleImageDecoder.DescribeChunk(chunk.Index, raw, null));
                    emptyChunks++;
                    continue;
                }
                var sprite = DecodeResource("abc.mkf", chunk.Index,
                    () => BattleImageDecoder.Decode(raw, Math.Min(remainingDecoded, MaximumDecodedChunkBytes), remainingPixels));
                remainingDecoded -= sprite.DecodedByteLength;
                remainingPixels -= sprite.Frames.Sum(frame => frame.Pixels.Length);
                Directory.CreateDirectory(Path.Combine(staging, "sprites", $"{chunk.Index}"));
                foreach (var frame in sprite.Frames)
                {
                    File.WriteAllBytes(Path.Combine(staging, "sprites", $"{chunk.Index}", $"{frame.FrameIndex}.png"),
                        PngWriter.Encode(frame, palette.Colors));
                    fileTotal++;
                }
                WriteJson(Path.Combine(staging, "sprites"), $"{chunk.Index}.json",
                    BattleImageDecoder.DescribeSprite(chunk.Index, abc.Name, sprite));
                fileTotal++;
                entries.Add(BattleImageDecoder.DescribeChunk(chunk.Index, raw, sprite));
                decodedChunks++;
                frameTotal += sprite.Frames.Length;
            }
            var index = new JsonObject
            {
                ["formatVersion"] = 1,
                ["profile"] = "faithful",
                ["command"] = "export-battle-images",
                ["parameters"] = new JsonObject { ["archive"] = Archive },
                ["sources"] = new JsonArray(archives.Select(archive => new JsonObject
                {
                    ["name"] = archive.Name, ["byteLength"] = archive.ByteLength, ["sha256"] = archive.Sha256,
                }).ToArray()),
                ["palette"] = new JsonObject
                {
                    ["archive"] = archives[1].Name,
                    ["chunk"] = 0,
                    ["lighting"] = "day",
                    ["usage"] = "PNG colorization only; frame indices and alpha are authoritative",
                },
                ["chunks"] = new JsonArray(entries.ToArray()),
                ["totals"] = new JsonObject
                {
                    ["chunks"] = abc.Chunks.Count,
                    ["decodedChunks"] = decodedChunks,
                    ["emptyChunks"] = emptyChunks,
                    ["frames"] = frameTotal,
                    ["files"] = fileTotal,
                },
            };
            // Detect source changes on platforms that do not enforce FileShare.Read against writers.
            var after = MkfArchive.OpenRequired(input, RequiredNames, 65536);
            try
            {
                if (!archives.Select(a => (a.Name, a.ByteLength, a.Sha256)).SequenceEqual(after.Select(a => (a.Name, a.ByteLength, a.Sha256))))
                    throw new InvalidDataException("Input archives changed during export.");
            }
            finally { foreach (var archive in after) archive.Dispose(); }
            ExportPaths.Validate(input, output, RequiredNames);
            Directory.CreateDirectory(output);
            foreach (string path in Directory.GetFiles(staging, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(output, Path.GetRelativePath(staging, path));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(path, target, overwrite: false);
            }
            // Publish the manifest last; an earlier copy failure must not advertise a complete package.
            WriteJson(output, "index.json", index);
        }
        finally
        {
            foreach (var archive in archives) archive.Dispose();
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }
}
