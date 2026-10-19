using System.Buffers.Binary;

namespace Pal.Extract.Tests;

// Independently authored synthetic encoder: object-identity tree links and a separate
// weight-order list, not the decoder's array-slot swaps. It never calls Yj2Decoder.
// Symbols are supplied by the test; expected output is literal test data, not decoded
// or expanded here. Wire rules are from reference/sdlpal/yj1.c's YJ2 section.
internal sealed class Yj2TestData
{
    private readonly int declaredLength;
    private readonly List<int> bits = [];
    private readonly List<Vertex> order = [];
    private readonly Vertex[] symbols;
    private readonly Vertex root;

    public Yj2TestData(int declaredLength)
    {
        this.declaredLength = declaredLength;
        for (int i = 0; i < 321; i++)
            order.Add(new Vertex { Weight = 1, Position = i });
        symbols = order.ToArray();
        for (int i = 0; order.Count < 641; i += 2)
        {
            var parent = new Vertex
            {
                Left = order[i], Right = order[i + 1], Position = order.Count,
                Weight = order[i].Weight + order[i + 1].Weight
            };
            parent.Left.Parent = parent.Right.Parent = parent;
            order.Add(parent);
        }
        root = order[^1];
    }

    public Yj2TestData Literal(byte value)
    {
        Symbol(value);
        return this;
    }

    public Yj2TestData Match(int length, int distance)
    {
        if (length is < 3 or > 67 || distance is < 1 or > 4095)
            throw new ArgumentOutOfRangeException();
        Symbol(length + 253);
        Position(distance - 1);
        return this;
    }

    public byte[] End()
    {
        Symbol(256);
        Position(4095);
        return Bytes();
    }

    public byte[] Bytes()
    {
        var result = new byte[4 + (bits.Count + 7) / 8];
        BinaryPrimitives.WriteInt32LittleEndian(result, declaredLength);
        for (int i = 0; i < bits.Count; i++)
            result[4 + i / 8] |= (byte)(bits[i] << (i % 8));
        return result;
    }

    private void Symbol(int value)
    {
        var path = new Stack<int>();
        for (Vertex vertex = symbols[value]; vertex.Parent is not null; vertex = vertex.Parent)
            path.Push(vertex.Parent.Right == vertex ? 1 : 0);
        bits.AddRange(path);
        // Rescaling happens after reading the path but before updating that symbol.
        if (root.Weight == 32768)
        {
            foreach (Vertex leaf in symbols)
                if (leaf.Weight % 2 == 1)
                    Increase(leaf);
            foreach (Vertex vertex in order)
                vertex.Weight /= 2;
        }
        Increase(symbols[value]);
    }

    private void Increase(Vertex vertex)
    {
        while (vertex != root)
        {
            int last = vertex.Position;
            while (order[last + 1].Weight == vertex.Weight)
                last++;
            Vertex peer = order[last];
            if (peer != vertex)
            {
                Vertex parent = vertex.Parent!;
                Vertex peerParent = peer.Parent!;
                bool left = parent.Left == vertex;
                bool peerLeft = peerParent.Left == peer;
                if (left) parent.Left = peer; else parent.Right = peer;
                if (peerLeft) peerParent.Left = vertex; else peerParent.Right = vertex;
                (vertex.Parent, peer.Parent) = (peerParent, parent);
                int old = vertex.Position;
                order[old] = peer;
                order[last] = vertex;
                (vertex.Position, peer.Position) = (last, old);
            }
            vertex.Weight++;
            vertex = vertex.Parent!;
        }
        root.Weight++;
    }

    private void Position(int value)
    {
        // Explicit LSB-first prefix codewords for the high six position bits.
        // Unlike the decoder, no 256-entry lookahead/length tables are used.
        ReadOnlySpan<byte> codes =
        [
            0x07, 0x0d, 0x0b, 0x03, 0x1e, 0x19, 0x15, 0x11,
            0x0e, 0x09, 0x05, 0x01, 0x3a, 0x36, 0x32, 0x2a,
            0x26, 0x22, 0x1a, 0x16, 0x12, 0x0a, 0x06, 0x02,
            0x7c, 0x78, 0x74, 0x6c, 0x68, 0x64, 0x5c, 0x58,
            0x54, 0x4c, 0x48, 0x44, 0x3c, 0x38, 0x34, 0x2c,
            0x28, 0x24, 0x1c, 0x18, 0x14, 0x0c, 0x08, 0x04,
            0xf0, 0xe0, 0xd0, 0xc0, 0xb0, 0xa0, 0x90, 0x80,
            0x70, 0x60, 0x50, 0x40, 0x30, 0x20, 0x10, 0x00
        ];
        int high = value >> 6;
        int length = high switch { 0 => 3, < 4 => 4, < 12 => 5, < 24 => 6, < 48 => 7, _ => 8 };
        Write(codes[high], length);
        Write(value & 63, 6);
    }

    private void Write(int value, int count)
    {
        for (int i = 0; i < count; i++)
            bits.Add((value >> i) & 1);
    }

    private sealed class Vertex
    {
        public int Position;
        public int Weight;
        public Vertex? Left;
        public Vertex? Right;
        public Vertex? Parent;
    }
}
