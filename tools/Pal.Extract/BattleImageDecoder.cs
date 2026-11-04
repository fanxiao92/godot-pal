using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Pal.Extract;

/// <summary>Complete decoded frame set of one battle image chunk (#92 abc.mkf and #94 f.mkf Windows YJ2 battle sprites, #93 ball.mkf raw item bitmap, #95 fbp.mkf raw 320x200 background).</summary>
public sealed record DecodedBattleSprite(int DecodedByteLength, int FrameCount, IndexedImage[] Frames);

/// <summary>
/// Decodes one Windows YJ2 battle image chunk (abc.mkf enemy sprites #92, f.mkf player
/// and summoned-god sprites #94) into its complete frame set. The YJ2 payload
/// decompresses to a PAL sprite (WORD offset table + RLE frames). PAL_SpriteGetFrame
/// addresses frame f through table word f for f &lt; the raw first word, one index beyond
/// PAL_SpriteGetNumFrames' count: that final table word is
/// a real follow-on frame for some sprites and a zero sentinel or a dangling pointer
/// into an earlier frame's data for others, so it is exported only when the whole
/// sprite decodes with it as a frame boundary.
/// </summary>
public static class BattleImageDecoder
{
    /// <exception cref="InvalidDataException">Chunk is not decodable Windows YJ2 sprite data.</exception>
    public static DecodedBattleSprite Decode(ReadOnlySpan<byte> chunk, int maximumDecodedBytes, int maximumPixels)
    {
        var sprite = Yj2Decoder.Decode(chunk, maximumDecodedBytes);
        int count = SpriteDecoder.GetFrameCount(sprite);
        if (count is < 1 or > 4096)
            throw new InvalidDataException("Battle sprite frame count is empty or exceeds the resource limit.");
        int extraOffset = FrameOffset(sprite, count);
        if (extraOffset == 0 || extraOffset == sprite.Length)
            return DecodeFrames(sprite, count, includeExtraEntry: false, maximumPixels);
        try
        {
            return DecodeFrames(sprite, count, includeExtraEntry: true, maximumPixels);
        }
        catch (InvalidDataException)
        {
            // The final word is a dangling pointer into earlier frame data, not a
            // follow-on frame; PAL_SpriteGetFrame would return it, never a real image.
            // A genuinely corrupt sprite fails the nominal-only decode below as well.
            return DecodeFrames(sprite, count, includeExtraEntry: false, maximumPixels);
        }
    }

    private static DecodedBattleSprite DecodeFrames(byte[] sprite, int count, bool includeExtraEntry, int maximumPixels)
    {
        int frameTotal = count + (includeExtraEntry ? 1 : 0);
        var frames = new IndexedImage[frameTotal];
        int remainingPixels = maximumPixels;
        for (int index = 0; index < frameTotal; index++)
        {
            int offset = FrameOffset(sprite, index);
            // A frame ends at the nearest later frame start; the optional final entry is
            // a boundary only while it decodes as one, so it is excluded on the retry.
            int end = sprite.Length;
            for (int other = 0; other <= count; other++)
            {
                if (other == count && !includeExtraEntry) continue;
                int candidate = FrameOffset(sprite, other);
                if (candidate > offset && candidate < end) end = candidate;
            }
            var image = SpriteDecoder.DecodeRle(sprite.AsSpan(offset, end - offset), remainingPixels);
            remainingPixels -= image.Pixels.Length;
            frames[index] = new IndexedImage(image.Width, image.Height, image.Pixels, image.Alpha, index, offset);
        }
        return new DecodedBattleSprite(sprite.Length, count, frames);
    }

    private static int FrameOffset(byte[] sprite, int frameIndex) =>
        BinaryPrimitives.ReadUInt16LittleEndian(sprite.AsSpan(2 * frameIndex)) * 2;

    /// <summary>index.json manifest entry; empty chunks carry no decoded fields, as PAL_MKFDecompressChunk returns no sprite for them.</summary>
    public static JsonObject DescribeChunk(int index, ReadOnlySpan<byte> chunk, DecodedBattleSprite? sprite) =>
        DescribeChunk(index, chunk, sprite, chunk.Length == 0 ? "empty" : "yj2");

    internal static JsonObject DescribeChunk(int index, ReadOnlySpan<byte> chunk, DecodedBattleSprite? sprite, string compression)
    {
        var entry = new JsonObject
        {
            ["index"] = index,
            ["byteLength"] = chunk.Length,
            ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(chunk)),
            ["compression"] = compression,
        };
        if (sprite is null) return entry;
        entry["decodedByteLength"] = sprite.DecodedByteLength;
        entry["frameCount"] = sprite.FrameCount;
        entry["decodedFrameCount"] = sprite.Frames.Length;
        entry["sprite"] = $"sprites/{index}.json";
        return entry;
    }

    /// <summary>sprites/&lt;chunk&gt;.json document; indices and alpha are the authoritative planes, the PNG is a colorized view.</summary>
    public static JsonObject DescribeSprite(int chunkIndex, string archiveName, DecodedBattleSprite sprite) =>
        DescribeSprite(chunkIndex, archiveName, sprite, "Windows YJ2", "PAL sprite WORD offset table + RLE frames");

    internal static JsonObject DescribeSprite(int chunkIndex, string archiveName, DecodedBattleSprite sprite, string encoding, string frameLayout)
    {
        var frames = new JsonArray();
        foreach (var frame in sprite.Frames)
        {
            frames.Add(new JsonObject
            {
                ["index"] = frame.FrameIndex,
                ["sourceOffset"] = frame.SourceOffset,
                ["width"] = frame.Width,
                ["height"] = frame.Height,
                ["png"] = $"sprites/{chunkIndex}/{frame.FrameIndex}.png",
                ["indices"] = Convert.ToBase64String(frame.Pixels),
                ["alpha"] = Convert.ToBase64String(frame.Alpha),
            });
        }
        return new JsonObject
        {
            ["formatVersion"] = 1,
            ["profile"] = "faithful",
            ["chunk"] = chunkIndex,
            ["source"] = new JsonObject
            {
                ["archive"] = archiveName,
                ["chunk"] = chunkIndex,
                ["encoding"] = encoding,
                ["frameLayout"] = frameLayout,
            },
            ["decodedByteLength"] = sprite.DecodedByteLength,
            ["frameCount"] = sprite.FrameCount,
            ["frames"] = frames,
        };
    }
}
