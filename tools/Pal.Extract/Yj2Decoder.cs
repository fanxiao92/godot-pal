using System.Buffers.Binary;

namespace Pal.Extract;

public static class Yj2Decoder
{
    /// <summary>
    /// Decodes one bounded Windows YJ2 chunk. The little-endian declared length
    /// must fit both the explicit output limit and a managed byte array.
    /// Requires a terminator and an exact output length; padding after the
    /// terminator is ignored, as in the reference decoder.
    /// </summary>
    /// <exception cref="InvalidDataException">Unsupported YJ1 or malformed/over-limit YJ2 data.</exception>
    public static byte[] Decode(ReadOnlySpan<byte> source, int maximumOutputSize)
    {
        if (source.Length < 4)
            throw new InvalidDataException("Truncated YJ2 length header.");
        if (source[..4].SequenceEqual("YJ_1"u8))
            throw new InvalidDataException("YJ1 compression is not supported; expected YJ2.");
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(source);
        if (maximumOutputSize < 0 || length > (uint)maximumOutputSize || length > Array.MaxLength)
            throw new InvalidDataException("YJ2 declared length exceeds the output limit.");
        var bits = new BitReader(source[4..]);
        var tree = new Tree();
        var output = new byte[(int)length];
        int written = 0;
        while (true)
        {
            int symbol = tree.ReadSymbol(ref bits);
            tree.Update(symbol);
            if (symbol <= 0xff)
            {
                if (written == output.Length)
                    throw new InvalidDataException("YJ2 literal exceeds the declared output length.");
                output[written++] = (byte)symbol;
            }
            else
            {
                int position = ReadPosition(ref bits);
                if (position == 0xfff)
                {
                    if (written != output.Length)
                        throw new InvalidDataException("YJ2 actual length differs from the declared output length.");
                    return output;
                }
                int count = symbol - 0xfd;
                int distance = position + 1;
                if (distance > written)
                    throw new InvalidDataException("YJ2 backreference precedes the output start.");
                if (count > output.Length - written)
                    throw new InvalidDataException("YJ2 match exceeds the declared output length.");
                // Bytewise copying deliberately permits overlapping LZ matches.
                for (int i = 0; i < count; i++)
                {
                    output[written] = output[written - distance];
                    written++;
                }
            }
        }
    }

    // Position tables and tree/bit order follow reference/sdlpal/yj1.c (YJ2).
    private static ReadOnlySpan<byte> PositionHigh =>
    [
        0x3f, 0x0b, 0x17, 0x03, 0x2f, 0x0a, 0x16, 0x00, 0x2e, 0x09, 0x15, 0x02, 0x2d, 0x01, 0x08, 0x00,
        0x3e, 0x07, 0x14, 0x03, 0x2c, 0x06, 0x13, 0x00, 0x2b, 0x05, 0x12, 0x02, 0x2a, 0x01, 0x04, 0x00,
        0x3d, 0x0b, 0x11, 0x03, 0x29, 0x0a, 0x10, 0x00, 0x28, 0x09, 0x0f, 0x02, 0x27, 0x01, 0x08, 0x00,
        0x3c, 0x07, 0x0e, 0x03, 0x26, 0x06, 0x0d, 0x00, 0x25, 0x05, 0x0c, 0x02, 0x24, 0x01, 0x04, 0x00,
        0x3b, 0x0b, 0x17, 0x03, 0x23, 0x0a, 0x16, 0x00, 0x22, 0x09, 0x15, 0x02, 0x21, 0x01, 0x08, 0x00,
        0x3a, 0x07, 0x14, 0x03, 0x20, 0x06, 0x13, 0x00, 0x1f, 0x05, 0x12, 0x02, 0x1e, 0x01, 0x04, 0x00,
        0x39, 0x0b, 0x11, 0x03, 0x1d, 0x0a, 0x10, 0x00, 0x1c, 0x09, 0x0f, 0x02, 0x1b, 0x01, 0x08, 0x00,
        0x38, 0x07, 0x0e, 0x03, 0x1a, 0x06, 0x0d, 0x00, 0x19, 0x05, 0x0c, 0x02, 0x18, 0x01, 0x04, 0x00,
        0x37, 0x0b, 0x17, 0x03, 0x2f, 0x0a, 0x16, 0x00, 0x2e, 0x09, 0x15, 0x02, 0x2d, 0x01, 0x08, 0x00,
        0x36, 0x07, 0x14, 0x03, 0x2c, 0x06, 0x13, 0x00, 0x2b, 0x05, 0x12, 0x02, 0x2a, 0x01, 0x04, 0x00,
        0x35, 0x0b, 0x11, 0x03, 0x29, 0x0a, 0x10, 0x00, 0x28, 0x09, 0x0f, 0x02, 0x27, 0x01, 0x08, 0x00,
        0x34, 0x07, 0x0e, 0x03, 0x26, 0x06, 0x0d, 0x00, 0x25, 0x05, 0x0c, 0x02, 0x24, 0x01, 0x04, 0x00,
        0x33, 0x0b, 0x17, 0x03, 0x23, 0x0a, 0x16, 0x00, 0x22, 0x09, 0x15, 0x02, 0x21, 0x01, 0x08, 0x00,
        0x32, 0x07, 0x14, 0x03, 0x20, 0x06, 0x13, 0x00, 0x1f, 0x05, 0x12, 0x02, 0x1e, 0x01, 0x04, 0x00,
        0x31, 0x0b, 0x11, 0x03, 0x1d, 0x0a, 0x10, 0x00, 0x1c, 0x09, 0x0f, 0x02, 0x1b, 0x01, 0x08, 0x00,
        0x30, 0x07, 0x0e, 0x03, 0x1a, 0x06, 0x0d, 0x00, 0x19, 0x05, 0x0c, 0x02, 0x18, 0x01, 0x04, 0x00
    ];

    private static ReadOnlySpan<byte> PositionPrefixLength =>
    [
        0x08, 0x05, 0x06, 0x04, 0x07, 0x05, 0x06, 0x03, 0x07, 0x05, 0x06, 0x04, 0x07, 0x04, 0x05, 0x03
    ];

    private static int ReadPosition(ref BitReader bits)
    {
        int lowByte = bits.Read(8);
        int prefixLength = PositionPrefixLength[lowByte & 15];
        int code = lowByte | (bits.Read(prefixLength - 2) << 8);
        return ((code >> prefixLength) & 63) | (PositionHigh[lowByte] << 6);
    }

    private ref struct BitReader(ReadOnlySpan<byte> source)
    {
        private readonly ReadOnlySpan<byte> source = source;
        private int byteOffset;
        private int bitOffset;

        public int Read(int count)
        {
            int value = 0;
            for (int i = 0; i < count; i++)
            {
                if (byteOffset == source.Length)
                    throw new InvalidDataException("Truncated YJ2 bitstream.");
                value |= ((source[byteOffset] >> bitOffset) & 1) << i;
                if (++bitOffset == 8)
                {
                    bitOffset = 0;
                    byteOffset++;
                }
            }
            return value;
        }
    }

    private sealed class Tree
    {
        private readonly Node[] nodes = new Node[641];
        private readonly int[] leaves = new int[321];

        public Tree()
        {
            for (int i = 0; i < nodes.Length; i++)
                nodes[i] = new Node { Value = i, Weight = 1 };
            for (int i = 0; i < leaves.Length; i++)
                leaves[i] = i;
            nodes[640].Parent = 640;
            for (int child = 0, parent = 321; parent <= 640; child += 2, parent++)
            {
                nodes[parent].Left = child;
                nodes[parent].Right = child + 1;
                nodes[child].Parent = nodes[child + 1].Parent = parent;
                nodes[parent].Weight = nodes[child].Weight + nodes[child + 1].Weight;
            }
        }

        public void Update(int value)
        {
            // SDLPal rescales after decoding the symbol, before its update.
            if (nodes[640].Weight == 0x8000)
            {
                for (int i = 0; i < leaves.Length; i++)
                    if ((nodes[leaves[i]].Weight & 1) != 0)
                        Adjust(i);
                for (int i = 0; i < nodes.Length; i++)
                    nodes[i].Weight >>= 1;
            }
            Adjust(value);
        }

        private void Adjust(int value)
        {
            int current = leaves[value];
            while (current != 640)
            {
                int last = current;
                while (nodes[last + 1].Weight == nodes[current].Weight)
                    last++;
                if (last != current)
                {
                    Node first = nodes[current];
                    Node second = nodes[last];
                    (first.Parent, second.Parent) = (second.Parent, first.Parent);
                    Relocate(first, last);
                    Relocate(second, current);
                    nodes[current] = second;
                    nodes[last] = first;
                    current = last;
                }
                nodes[current].Weight++;
                current = nodes[current].Parent;
            }
            nodes[640].Weight++;
        }

        private void Relocate(Node node, int index)
        {
            if (node.Value <= 320)
                leaves[node.Value] = index;
            else
                nodes[node.Left].Parent = nodes[node.Right].Parent = index;
        }

        public int ReadSymbol(ref BitReader bits)
        {
            int current = 640;
            while (nodes[current].Value > 320)
                current = bits.Read(1) == 0 ? nodes[current].Left : nodes[current].Right;
            return nodes[current].Value;
        }
    }

    private struct Node
    {
        public int Value;
        public int Weight;
        public int Parent;
        public int Left;
        public int Right;
    }
}
