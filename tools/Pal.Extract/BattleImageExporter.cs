using System.Text.Json.Nodes;
using static Pal.Extract.ExportSupport;

namespace Pal.Extract;

// export-battle-images (#92 abc, #93 ball, #94 f): decodes every chunk of the
// selected battle image MKF into a complete image package: index.json manifest, one
// sprite document per chunk with authoritative indices/alpha planes, and PNG frames
// colorized with pat.mkf chunk 0 day colors. abc and f chunks are the Windows YJ2
// enemy/player battle sprites read through PAL_MKFDecompressChunk (battle.c
// PAL_LoadBattleSprites by enemyId; battle.c/fight.c by rgwSpriteNumInBattle and
// wSummonEffect+10); ball chunks are the raw item bitmaps itemmenu.c reads
// un-decompressed through item.wBitmap. The verb and manifest shape stay frozen for
// #95 (fbp).
internal static class BattleImageExporter
{
    private const int MaximumChunkBytes = 8 * 1024 * 1024;
    private const int MaximumDecodedChunkBytes = 8 * 1024 * 1024;
    private const int MaximumAggregateDecodedBytes = 256 * 1024 * 1024;
    private const int MaximumAggregatePixels = 128 * 1024 * 1024;
    internal static void Export(string input, string output, string archive)
    {
        string[] requiredNames = archive switch
        {
            "abc" => ["abc.mkf", "pat.mkf"],
            "ball" => ["ball.mkf", "pat.mkf"],
            "f" => ["f.mkf", "pat.mkf"],
            _ => throw new InvalidOperationException($"Unknown battle image archive '{archive}'."),
        };
        output = ExportPaths.Validate(input, output, requiredNames);
        var archives = MkfArchive.OpenRequired(input, requiredNames, 65536);
        string staging = Path.Combine(Path.GetTempPath(), $"pal-battle-{Guid.NewGuid():N}");
        try
        {
            var source = archives[0];
            var palette = DecodeResource("pat.mkf", 0, () => PaletteDecoder.Decode(archives[1].ReadChunk(0, 1536), night: false));
            // f.mkf is read with PAL_MKFDecompressChunk exactly like abc.mkf (battle.c
            // PAL_LoadBattleSprites, fight.c summon animation), so both take the Windows
            // YJ2 sprite path; only ball.mkf stores its bitmaps uncompressed.
            bool compressedSprites = archive is "abc" or "f";
            Directory.CreateDirectory(staging);
            var entries = new List<JsonObject>();
            int decodedChunks = 0, emptyChunks = 0, frameTotal = 0, fileTotal = 0;
            int remainingDecoded = MaximumAggregateDecodedBytes, remainingPixels = MaximumAggregatePixels;
            foreach (var chunk in source.Chunks)
            {
                byte[] raw = source.ReadChunk(chunk.Index, MaximumChunkBytes);
                if (raw.Length == 0)
                {
                    entries.Add(compressedSprites
                        ? BattleImageDecoder.DescribeChunk(chunk.Index, raw, null)
                        : BallImageDecoder.DescribeChunk(chunk.Index, raw, null));
                    emptyChunks++;
                    continue;
                }
                var sprite = DecodeResource(requiredNames[0], chunk.Index, () => compressedSprites
                    ? BattleImageDecoder.Decode(raw, Math.Min(remainingDecoded, MaximumDecodedChunkBytes), remainingPixels)
                    : BallImageDecoder.Decode(raw, remainingPixels));
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
                    compressedSprites
                        ? BattleImageDecoder.DescribeSprite(chunk.Index, source.Name, sprite)
                        : BallImageDecoder.DescribeSprite(chunk.Index, source.Name, sprite));
                fileTotal++;
                entries.Add(compressedSprites
                    ? BattleImageDecoder.DescribeChunk(chunk.Index, raw, sprite)
                    : BallImageDecoder.DescribeChunk(chunk.Index, raw, sprite));
                decodedChunks++;
                frameTotal += sprite.Frames.Length;
            }
            var index = new JsonObject
            {
                ["formatVersion"] = 1,
                ["profile"] = "faithful",
                ["command"] = "export-battle-images",
                ["parameters"] = new JsonObject { ["archive"] = archive },
                ["sources"] = new JsonArray(archives.Select(a => new JsonObject
                {
                    ["name"] = a.Name, ["byteLength"] = a.ByteLength, ["sha256"] = a.Sha256,
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
                    ["chunks"] = source.Chunks.Count,
                    ["decodedChunks"] = decodedChunks,
                    ["emptyChunks"] = emptyChunks,
                    ["frames"] = frameTotal,
                    ["files"] = fileTotal,
                },
            };
            // Detect source changes on platforms that do not enforce FileShare.Read against writers.
            var after = MkfArchive.OpenRequired(input, requiredNames, 65536);
            try
            {
                if (!archives.Select(a => (a.Name, a.ByteLength, a.Sha256)).SequenceEqual(after.Select(a => (a.Name, a.ByteLength, a.Sha256))))
                    throw new InvalidDataException("Input archives changed during export.");
            }
            finally { foreach (var reopened in after) reopened.Dispose(); }
            ExportPaths.Validate(input, output, requiredNames);
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
            foreach (var opened in archives) opened.Dispose();
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }
}
