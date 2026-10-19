using Xunit;

namespace Pal.Extract.Tests;

public class SpriteDecoderTests
{
    [Fact]
    public void DecodeFrame_handles_optional_header_and_nonzero_word_padding()
    {
        byte[] sprite = [2, 0, 7, 0, 2, 0, 0, 0, 1, 0, 1, 0, 0x81, 0xAB];
        var image = SpriteDecoder.DecodeFrame(sprite, 0);
        Assert.Equal(1, image.Width);
        Assert.Equal(1, image.Height);
        Assert.Equal(new byte[] { 0 }, image.Pixels);
        Assert.Equal(new byte[] { 0 }, image.Alpha);
    }

    [Fact]
    public void DecodeFrame_handles_aliases_nonmonotonic_offsets_and_the_extra_reference_entry()
    {
        // Nominal count 3. Entries 1 and 3 alias byte 14, entry 2 returns to byte 8.
        byte[] sprite = [4, 0, 7, 0, 4, 0, 7, 0, 1, 0, 1, 0, 1, 11, 1, 0, 1, 0, 1, 22];
        Assert.Equal(3, SpriteDecoder.GetFrameCount(sprite));
        Assert.Equal(new byte[] { 11 }, SpriteDecoder.DecodeFrame(sprite, 2).Pixels);
        var alias = SpriteDecoder.DecodeFrame(sprite, 3);
        Assert.Equal(3, alias.FrameIndex);
        Assert.Equal(14, alias.SourceOffset);
        Assert.Equal(new byte[] { 22 }, alias.Pixels);
        Assert.Equal(SpriteDecoder.DecodeFrame(sprite, 1).Pixels, alias.Pixels);
    }

    [Fact]
    public void DecodeFrame_applies_only_the_exact_reference_known_offset_repair()
    {
        var sprite = new byte[0x844A];
        sprite[0] = 2;
        sprite[2] = 0x22;
        sprite[3] = 0xC2; // word 0xC222 => byte 0x18444 => reference repair 0x8444
        // Only decode the repaired extra entry: the unrequested frame is not decoded.
        new byte[] { 1, 0, 1, 0, 1, 73 }.CopyTo(sprite, 0x8444);
        var image = SpriteDecoder.DecodeFrame(sprite, 1);
        Assert.Equal(0x8444, image.SourceOffset);
        Assert.Equal(new byte[] { 73 }, image.Pixels);
    }

    [Fact]
    public void DecodeFrame_distinguishes_transparent_threshold_from_long_opaque_runs_across_rows()
    {
        // Width 2: 0x82 is skip 2, but 0x83 is an opaque run of 131, crossing rows.
        var sprite = new byte[140];
        new byte[] { 2, 0, 0, 0, 2, 0, 67, 0, 0x83 }.CopyTo(sprite, 0);
        Array.Fill(sprite, (byte)37, 9, 131);
        // Need 134 pixels: append one opaque index zero and a skip of two.
        sprite = [.. sprite, 1, 0, 0x82, 0];
        var image = SpriteDecoder.DecodeFrame(sprite, 0);
        Assert.Equal(134, image.Pixels.Length);
        Assert.All(image.Pixels[..131], value => Assert.Equal(37, value));
        Assert.Equal(new byte[] { 0, 0, 0 }, image.Pixels[131..]);
        Assert.Equal(new byte[] { 255, 0, 0 }, image.Alpha[131..]);
        Assert.All(image.Alpha[..131], value => Assert.Equal(255, value));
    }

    public static TheoryData<byte[]> InvalidSprites => new()
    {
        Array.Empty<byte>(), new byte[] {0, 0}, new byte[] {3, 0, 0, 0}, // missing/zero/truncated table
        new byte[] {2, 0, 1, 0, 1, 0, 1, 0, 1, 1}, // offset into table
        new byte[] {2, 0, 255, 255, 1, 0, 1, 0, 1, 1}, // offset beyond buffer
        new byte[] {2, 0, 0, 0, 1, 0}, // dimensions truncated
        new byte[] {2, 0, 0, 0, 0, 0, 1, 0}, // zero width
        new byte[] {2, 0, 0, 0, 1, 0, 0, 0}, // zero height
        new byte[] {2, 0, 0, 0, 255, 255, 255, 255}, // multiplication/resource excess
        new byte[] {2, 0, 0, 0, 1, 0, 1, 0}, // command missing
        new byte[] {2, 0, 0, 0, 1, 0, 1, 0, 0}, // zero opaque progress
        new byte[] {2, 0, 0, 0, 1, 0, 1, 0, 0x80}, // zero skip progress
        new byte[] {2, 0, 0, 0, 1, 0, 1, 0, 2, 7, 8}, // opaque overflow
        new byte[] {2, 0, 0, 0, 2, 0, 1, 0, 2, 7}, // missing literal
        new byte[] {2, 0, 0, 0, 2, 0, 1, 0, 1, 7, 0x82}, // skip overflow
        new byte[] {3, 0, 6, 0, 9, 0, 2, 0, 1, 0, 2, 7, 1, 0, 1, 0, 1, 42} // must not read next frame as literal
    };

    [Theory]
    [MemberData(nameof(InvalidSprites))]
    public void DecodeFrame_rejects_malformed_or_excessive_data(byte[] sprite) =>
        Assert.Throws<InvalidDataException>(() => SpriteDecoder.DecodeFrame(sprite, 0));

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void DecodeFrame_rejects_out_of_table_indices(int index) =>
        Assert.Throws<InvalidDataException>(() => SpriteDecoder.DecodeFrame(new byte[] { 2, 0, 0, 0, 1, 0, 1, 0, 1, 42 }, index));

    [Fact]
    public void DecodeFrame_honors_the_callers_smaller_pixel_budget() =>
        Assert.Throws<InvalidDataException>(() => SpriteDecoder.DecodeFrame(
            new byte[] { 2, 0, 0, 0, 2, 0, 1, 0, 2, 7, 8 }, 0, maxPixels: 1));

    [Fact]
    public void DecodeFrame_rejects_unexplained_trailing_data_but_accepts_word_padding()
    {
        Assert.Equal(new byte[] { 42 }, SpriteDecoder.DecodeFrame(
            new byte[] { 2, 0, 0, 0, 1, 0, 1, 0, 1, 42 }, 0).Pixels);
        Assert.Throws<InvalidDataException>(() => SpriteDecoder.DecodeFrame(
            new byte[] { 2, 0, 0, 0, 1, 0, 1, 0, 1, 42, 99, 99 }, 0));
    }

    [Fact]
    public void DecodeFrame_retains_the_last_actual_frame_before_a_zero_terminal_slot_but_rejects_middle_zero_offsets()
    {
        // Independent miniature of GOP12: two actual frames followed by a zero table slot, not EOF.
        byte[] sprite = [3, 0, 6, 0, 0, 0, 1, 0, 1, 0, 1, 11, 1, 0, 1, 0, 1, 22];
        Assert.Equal(2, SpriteDecoder.GetFrameCount(sprite));
        var last = SpriteDecoder.DecodeFrame(sprite, 1);
        Assert.Equal(1, last.FrameIndex);
        Assert.Equal(12, last.SourceOffset);
        Assert.Equal(new byte[] { 22 }, last.Pixels);
        Assert.Equal(new byte[] { 255 }, last.Alpha);
        Assert.Throws<InvalidDataException>(() => SpriteDecoder.DecodeFrame(sprite, 2));
        sprite[2] = 0; // Zero is valid only in the final slot, never in the middle.
        Assert.Throws<InvalidDataException>(() => SpriteDecoder.GetFrameCount(sprite));
        Assert.Throws<InvalidDataException>(() => SpriteDecoder.DecodeFrame(sprite, 0));
    }

    [Fact]
    public void DecodeFrame_accepts_zero_terminal_word_without_treating_it_as_a_frame()
    {
        byte[] sprite = [2, 0, 0, 0, 1, 0, 1, 0, 1, 42];
        Assert.Equal(1, SpriteDecoder.GetFrameCount(sprite));
        Assert.Equal(new byte[] { 42 }, SpriteDecoder.DecodeFrame(sprite, 0).Pixels);
        Assert.Throws<InvalidDataException>(() => SpriteDecoder.DecodeFrame(sprite, 1));
    }

    [Fact]
    public void DecodeFrame_preserves_index_zero_and_independent_transparent_pixels()
    {
        // Two table words: one frame at byte 4 and end at byte 16.
        // 3x2: [opaque 0, opaque 7, skip], [skip, opaque 9, opaque 0].
        byte[] sprite = [2, 0, 8, 0, 3, 0, 2, 0, 2, 0, 7, 0x82, 2, 9, 0, 0];
        var image = SpriteDecoder.DecodeFrame(sprite, 0);
        Assert.Equal(1, SpriteDecoder.GetFrameCount(sprite));
        Assert.Equal(0, image.FrameIndex);
        Assert.Equal(4, image.SourceOffset);
        Assert.Equal(3, image.Width);
        Assert.Equal(2, image.Height);
        Assert.Equal(new byte[] { 0, 7, 0, 0, 9, 0 }, image.Pixels);
        Assert.Equal(new byte[] { 255, 255, 0, 0, 255, 255 }, image.Alpha);
    }
}
