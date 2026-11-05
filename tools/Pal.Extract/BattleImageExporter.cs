using System.Text.Json.Nodes;
using static Pal.Extract.ExportSupport;

namespace Pal.Extract;

// export-battle-images (#92 abc, #93 ball, #94 f, #95 fbp): decodes every chunk of
// the selected battle image MKF into a complete image package: index.json manifest,
// one sprite document per chunk with authoritative indices/alpha planes, and PNG
// frames colorized with pat.mkf chunk 0 day colors. abc and f chunks are the Windows
// YJ2 enemy/player battle sprites read through PAL_MKFDecompressChunk (battle.c
// PAL_LoadBattleSprites by enemyId; battle.c/fight.c by rgwSpriteNumInBattle and
// wSummonEffect+10); ball chunks are the raw item bitmaps itemmenu.c reads
// un-decompressed through item.wBitmap; fbp chunks are the YJ2 battle backgrounds
// battle.c PAL_LoadBattleBackground reads by wNumBattleField and PAL_FBPBlitToSurface
// blits as one raw 320x200 screen.
internal static class BattleImageExporter
{
    private const int MaximumChunkBytes = 8 * 1024 * 1024;
    private const int MaximumDecodedChunkBytes = 8 * 1024 * 1024;
    private const int MaximumAggregateDecodedBytes = 256 * 1024 * 1024;
    private const int MaximumAggregatePixels = 128 * 1024 * 1024;

    // One strategy record per battle image archive (#92 abc, #93 ball, #94 f, #95 fbp):
    // the required archives plus the decode/describe paths, so registering an archive
    // is one record here instead of parallel switches in Export plus a vocabulary
    // check in Program.cs. f.mkf is read with PAL_MKFDecompressChunk exactly like
    // abc.mkf (battle.c PAL_LoadBattleSprites, fight.c summon animation), so both take
    // the Windows YJ2 sprite path; ball.mkf stores its bitmaps uncompressed; fbp.mkf
    // is decompressed too, but into one raw 320x200 screen rather than a sprite.
    // DescribeChunk/DescribeSprite are lambdas because their decoders take a
    // ReadOnlySpan<byte>, which cannot be a delegate type argument.
    private sealed record ArchiveStrategy(
        string[] RequiredNames,
        Func<byte[], int, int, DecodedBattleSprite> Decode,
        Func<int, byte[], DecodedBattleSprite?, JsonObject> DescribeChunk,
        Func<int, string, DecodedBattleSprite, JsonObject> DescribeSprite);

    private static readonly IReadOnlyDictionary<string, ArchiveStrategy> Archives = new Dictionary<string, ArchiveStrategy>
    {
        ["abc"] = new(["abc.mkf", "pat.mkf"],
            (raw, remainingDecoded, remainingPixels) => BattleImageDecoder.Decode(raw, Math.Min(remainingDecoded, MaximumDecodedChunkBytes), remainingPixels),
            (index, raw, sprite) => BattleImageDecoder.DescribeChunk(index, raw, sprite),
            (chunkIndex, archiveName, sprite) => BattleImageDecoder.DescribeSprite(chunkIndex, archiveName, sprite)),
        ["ball"] = new(["ball.mkf", "pat.mkf"],
            (raw, _, remainingPixels) => BallImageDecoder.Decode(raw, remainingPixels),
            (index, raw, sprite) => BallImageDecoder.DescribeChunk(index, raw, sprite),
            (chunkIndex, archiveName, sprite) => BallImageDecoder.DescribeSprite(chunkIndex, archiveName, sprite)),
        ["f"] = new(["f.mkf", "pat.mkf"],
            (raw, remainingDecoded, remainingPixels) => BattleImageDecoder.Decode(raw, Math.Min(remainingDecoded, MaximumDecodedChunkBytes), remainingPixels),
            (index, raw, sprite) => BattleImageDecoder.DescribeChunk(index, raw, sprite),
            (chunkIndex, archiveName, sprite) => BattleImageDecoder.DescribeSprite(chunkIndex, archiveName, sprite)),
        ["fbp"] = new(["fbp.mkf", "pat.mkf"],
            (raw, remainingDecoded, _) => FbpImageDecoder.Decode(raw, Math.Min(remainingDecoded, MaximumDecodedChunkBytes)),
            (index, raw, sprite) => FbpImageDecoder.DescribeChunk(index, raw, sprite),
            (chunkIndex, archiveName, sprite) => FbpImageDecoder.DescribeSprite(chunkIndex, archiveName, sprite)),
    };

    internal static bool IsKnownArchive(string archive) => Archives.ContainsKey(archive);

    internal static void Export(string input, string output, string archive)
    {
        if (!Archives.TryGetValue(archive, out ArchiveStrategy? strategy))
            throw new InvalidOperationException($"Unknown battle image archive '{archive}'.");
        string[] requiredNames = strategy.RequiredNames;
        output = ExportPaths.Validate(input, output, requiredNames);
        var archives = MkfArchive.OpenRequired(input, requiredNames, 65536);
        string staging = Path.Combine(Path.GetTempPath(), $"pal-battle-{Guid.NewGuid():N}");
        try
        {
            var source = archives[0];
            var palette = DecodeResource("pat.mkf", 0, () => PaletteDecoder.Decode(archives[1].ReadChunk(0, 1536), night: false));
            Directory.CreateDirectory(staging);
            var entries = new List<JsonObject>();
            int decodedChunks = 0, emptyChunks = 0, frameTotal = 0, fileTotal = 0;
            int remainingDecoded = MaximumAggregateDecodedBytes, remainingPixels = MaximumAggregatePixels;
            foreach (var chunk in source.Chunks)
            {
                byte[] raw = source.ReadChunk(chunk.Index, MaximumChunkBytes);
                if (raw.Length == 0)
                {
                    entries.Add(strategy.DescribeChunk(chunk.Index, raw, null));
                    emptyChunks++;
                    continue;
                }
                var sprite = DecodeResource(requiredNames[0], chunk.Index,
                    () => strategy.Decode(raw, remainingDecoded, remainingPixels));
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
                    strategy.DescribeSprite(chunk.Index, source.Name, sprite));
                fileTotal++;
                entries.Add(strategy.DescribeChunk(chunk.Index, raw, sprite));
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
