using Xunit;

namespace Pal.Extract.Tests;

public class Yj2DecoderTests
{
    [Theory]
    [InlineData("")]
    [InlineData("00")]
    [InlineData("000000")]
    [InlineData("01000080")]
    [InlineData("FFFFFFFF")]
    [InlineData("01000000")]
    public void Rejects_truncated_headers_and_declared_sizes_over_the_limit(string hex)
    {
        Assert.Throws<InvalidDataException>(() => Yj2Decoder.Decode(Convert.FromHexString(hex), 0));
    }

    [Theory]
    [InlineData("000000007E003F")]
    [InlineData("000000007D003F")]
    public void Decodes_empty_stream_with_an_explicit_terminator(string hex)
    {
        // Initial symbols 0x100 and 0x140 have paths 01111110 and 10111110.
        // Position 0xfff is prefix 00000000 followed by six 1 bits, all
        // packed least-bit first. Either match symbol can precede the marker.
        Assert.Empty(Yj2Decoder.Decode(Convert.FromHexString(hex), 0));
    }

    [Fact]
    public void Stops_at_the_terminator_allowing_chunk_padding()
    {
        Assert.Empty(Yj2Decoder.Decode(Convert.FromHexString("000000007E003FFFFFFFFF"), 0));
    }

    [Fact]
    public void Decodes_literals_as_exact_bytes_with_adaptive_tree_updates()
    {
        byte[] expected = [0x41, 0x00, 0xff, 0x41, 0x42, 0x41, 0x42, 0x41];
        var fixture = new Yj2TestData(expected.Length);
        foreach (byte value in expected)
            fixture.Literal(value);

        Assert.Equal(expected, Yj2Decoder.Decode(fixture.End(), expected.Length));
    }

    [Fact]
    public void Decodes_every_literal_byte_value_without_text_conversion()
    {
        byte[] expected = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();
        var fixture = new Yj2TestData(expected.Length);
        foreach (byte value in expected)
            fixture.Literal(value);

        Assert.Equal(expected, Yj2Decoder.Decode(fixture.End(), 256));
    }

    [Fact]
    public void Decodes_a_backreference_that_overlaps_its_destination()
    {
        byte[] source = new Yj2TestData(9).Literal(0x41).Literal(0x42).Literal(0x43)
            .Match(length: 6, distance: 3).End();

        Assert.Equal("ABCABCABC"u8.ToArray(), Yj2Decoder.Decode(source, 9));
    }

    [Fact]
    public void Decodes_past_multiple_adaptive_tree_rescales()
    {
        // 105,000 literal symbols crosses 0x8000 root weight repeatedly,
        // with unequal and odd leaf weights rather than a uniform histogram.
        byte[] expected = System.Text.Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("BANANA!", 15_000)));
        var fixture = new Yj2TestData(expected.Length);
        foreach (byte value in expected)
            fixture.Literal(value);

        Assert.Equal(expected, Yj2Decoder.Decode(fixture.End(), expected.Length));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(67)]
    public void Supports_minimum_and_maximum_match_lengths_at_distance_one(int length)
    {
        byte[] source = new Yj2TestData(length + 1).Literal(0x58).Match(length, 1).End();

        Assert.Equal(Enumerable.Repeat((byte)0x58, length + 1), Yj2Decoder.Decode(source, length + 1));
    }

    public static TheoryData<int> PositionHighCodes => new(Enumerable.Range(0, 64));

    [Theory]
    [MemberData(nameof(PositionHighCodes))]
    public void Decodes_every_position_prefix_code(int high)
    {
        // 64-byte runs of byte values 0..63. Distance high*64+32 points
        // into the middle of the run whose literal byte value is 63-high.
        var fixture = new Yj2TestData(4098);
        byte[] expected = new byte[4098];
        for (int i = 0; i < 4095; i++)
        {
            expected[i] = (byte)(i / 64);
            fixture.Literal(expected[i]);
        }
        expected[4095] = expected[4096] = expected[4097] = (byte)(63 - high);

        Assert.Equal(expected, Yj2Decoder.Decode(fixture.Match(3, high * 64 + 32).End(), 4098));
    }

    [Fact]
    public void Decodes_the_largest_nonterminating_distance()
    {
        var fixture = new Yj2TestData(4098).Literal(0x41).Literal(0x42).Literal(0x43);
        byte[] expected = new byte[4098];
        "ABC"u8.CopyTo(expected);
        for (int i = 3; i < 4095; i++)
            fixture.Literal(0);
        "ABC"u8.CopyTo(expected.AsSpan(4095));

        Assert.Equal(expected, Yj2Decoder.Decode(fixture.Match(3, 4095).End(), 4098));
    }

    [Theory]
    [InlineData("00000000")]
    [InlineData("000000007E")]
    [InlineData("000000007E00")]
    public void Rejects_truncated_symbol_or_position_bits(string hex)
    {
        Assert.Throws<InvalidDataException>(() => Yj2Decoder.Decode(Convert.FromHexString(hex), 0));
    }

    [Fact]
    public void Never_reads_beyond_the_supplied_bounded_span()
    {
        byte[] source = new Yj2TestData(9).Literal(0x41).Literal(0x42).Literal(0x43).Match(6, 3).End();
        for (int length = 0; length < source.Length; length++)
        {
            int boundedLength = length;
            Assert.Throws<InvalidDataException>(() => Yj2Decoder.Decode(source.AsSpan(0, boundedLength), 9));
        }
    }

    [Fact]
    public void Requires_a_terminator_even_after_the_declared_output_is_complete()
    {
        byte[] source = new Yj2TestData(1).Literal(0x41).Bytes();
        Assert.Throws<InvalidDataException>(() => Yj2Decoder.Decode(source, 1));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    public void Rejects_a_backreference_before_available_output(int literals, int distance)
    {
        var fixture = new Yj2TestData(literals + 3);
        for (int i = 0; i < literals; i++)
            fixture.Literal(0x41);

        var error = Assert.Throws<InvalidDataException>(() => Yj2Decoder.Decode(fixture.Match(3, distance).End(), 4));
        Assert.Contains("backreference", error.Message);
    }

    [Fact]
    public void Rejects_literal_overflow_before_writing()
    {
        byte[] source = new Yj2TestData(0).Literal(0x41).End();
        var error = Assert.Throws<InvalidDataException>(() => Yj2Decoder.Decode(source, 10));
        Assert.Contains("literal", error.Message);
    }

    [Fact]
    public void Rejects_match_overflow_before_copying()
    {
        byte[] source = new Yj2TestData(3).Literal(0x41).Match(3, 1).End();
        var error = Assert.Throws<InvalidDataException>(() => Yj2Decoder.Decode(source, 10));
        Assert.Contains("match", error.Message);
    }

    [Fact]
    public void Rejects_early_termination_instead_of_returning_an_incomplete_output()
    {
        byte[] source = new Yj2TestData(2).Literal(0x41).End();
        var error = Assert.Throws<InvalidDataException>(() => Yj2Decoder.Decode(source, 2));
        Assert.Contains("actual length", error.Message);
    }

    [Fact]
    public void Checks_the_output_limit_before_allocating_the_declared_length()
    {
        Assert.Throws<InvalidDataException>(() => Yj2Decoder.Decode(Convert.FromHexString("FFFFFF7F"), 16));
        Assert.Throws<InvalidDataException>(() => Yj2Decoder.Decode(Convert.FromHexString("000000007E003F"), -1));
        byte[] source = new Yj2TestData(3).Literal(0x41).Literal(0x42).Literal(0x43).End();
        Assert.Throws<InvalidDataException>(() => Yj2Decoder.Decode(source, 2));
        Assert.Equal("ABC"u8.ToArray(), Yj2Decoder.Decode(source, 3));
    }

    [Fact]
    public void Rejects_lengths_larger_than_a_managed_byte_array_even_with_a_larger_limit()
    {
        Assert.Throws<InvalidDataException>(() => Yj2Decoder.Decode(Convert.FromHexString("FFFFFF7F"), int.MaxValue));
    }

    [Fact]
    public void Rejects_YJ1_with_an_explicit_diagnostic()
    {
        var error = Assert.Throws<InvalidDataException>(() => Yj2Decoder.Decode("YJ_1"u8, 1024));
        Assert.Contains("YJ1", error.Message);
    }
}
