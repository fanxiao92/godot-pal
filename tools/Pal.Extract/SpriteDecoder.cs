using System.Buffers.Binary;

namespace Pal.Extract;

/// <summary>Decodes raw GOP sprites or already-decompressed MGO sprites; does not decompress archives.</summary>
public static class SpriteDecoder
{
    /// <summary>PAL_SpriteGetNumFrames's nominal count (first word minus one).</summary>
    public static int GetFrameCount(ReadOnlySpan<byte> sprite) => TableWordCount(sprite) - 1;

    /// <summary>
    /// Also permits the extra table entry accepted by PAL_SpriteGetFrame, if it points to image data.
    /// A final zero or end-of-buffer entry is a sentinel, not a decodable frame. Aliases retain their requested index.
    /// </summary>
    public static IndexedImage DecodeFrame(ReadOnlySpan<byte> sprite, int frameIndex, int maxPixels = IndexedImage.MaxPixels)
    {
        int words = TableWordCount(sprite);
        if (frameIndex < 0 || frameIndex >= words)
            throw new InvalidDataException("Sprite frame index is outside the offset table.");
        int offset = Offset(sprite, frameIndex);
        int end = sprite.Length;
        for (int i = 0; i < words; i++)
        {
            int candidate = Offset(sprite, i);
            if (candidate > offset && candidate < end) end = candidate;
        }
        if (offset == 0 || offset == sprite.Length)
            throw new InvalidDataException("Sprite frame offset points to a sentinel, not image data.");
        var rle = sprite.Slice(offset, end - offset);
        int position = 0;
        if (rle.Length >= 4 && rle[..4].SequenceEqual(new byte[] { 2, 0, 0, 0 })) position = 4;
        if (rle.Length - position < 4)
            throw new InvalidDataException("Sprite RLE dimensions are truncated.");
        int width = BinaryPrimitives.ReadUInt16LittleEndian(rle[position..]);
        int height = BinaryPrimitives.ReadUInt16LittleEndian(rle[(position + 2)..]);
        position += 4;
        long size = (long)width * height;
        if (width == 0 || height == 0 || maxPixels <= 0 || size > Math.Min(maxPixels, IndexedImage.MaxPixels))
            throw new InvalidDataException("Sprite dimensions exceed the pixel limit or are empty.");
        var pixels = new byte[(int)size];
        var alpha = new byte[(int)size];
        int output = 0;
        while (output < pixels.Length)
        {
            if (position >= rle.Length) throw new InvalidDataException("Sprite RLE command is truncated.");
            byte command = rle[position++];
            // PAL_RLEBlitToSurface: high-bit commands above 0x80 + width are opaque runs, not skips.
            bool skip = (command & 0x80) != 0 && command <= 0x80 + width;
            int count = skip ? command - 0x80 : command;
            if (count == 0 || count > pixels.Length - output)
                throw new InvalidDataException("Sprite RLE run makes no progress or exceeds the image.");
            if (!skip)
            {
                if (count > rle.Length - position) throw new InvalidDataException("Sprite RLE pixels are truncated.");
                rle.Slice(position, count).CopyTo(pixels.AsSpan(output));
                alpha.AsSpan(output, count).Fill(255);
                position += count;
            }
            output += count;
        }
        // Frame starts are word offsets; one unused byte may align the next frame.
        if (rle.Length - position > 1)
            throw new InvalidDataException("Sprite RLE has data beyond its final run and word padding.");
        return new IndexedImage(width, height, pixels, alpha, frameIndex, offset);
    }

    private static int TableWordCount(ReadOnlySpan<byte> sprite)
    {
        if (sprite.Length < 2) throw new InvalidDataException("Sprite offset table is truncated.");
        int words = BinaryPrimitives.ReadUInt16LittleEndian(sprite);
        if (words == 0 || words * 2 > sprite.Length)
            throw new InvalidDataException("Sprite offset table length is invalid.");
        for (int i = 0; i < words; i++)
        {
            int offset = Offset(sprite, i);
            if (i == words - 1 && offset == 0) continue;
            if (offset < words * 2 || offset > sprite.Length)
                throw new InvalidDataException("Sprite frame offset is outside the frame data.");
        }
        return words;
    }

    private static int Offset(ReadOnlySpan<byte> sprite, int index)
    {
        int offset = BinaryPrimitives.ReadUInt16LittleEndian(sprite[(index * 2)..]) * 2;
        // PAL_SpriteGetFrame's exact Bloody-Mouth Bug repair; not a general 16-bit wrap.
        return offset == 0x18444 ? (ushort)offset : offset;
    }
}
