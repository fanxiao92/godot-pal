using System.Buffers.Binary;
using System.Text.Json.Nodes;
using Pal.Extract;

namespace Pal.Extract.Tests;

// Shared in-project harness for the opcode counter-example skeleton (#118): the
// 18 Opcode*Tests.cs batches reference these members through `using static`
// instead of copying them, and the data-table tests share Words. This file stays
// inside Pal.Extract.UnitTests - project-internal sharing only; unit-boundary.md
// forbids cross-project source links, not same-project files.
internal static class OpcodeCounterexampleHarness
{
    internal const int ChunkCount = 8;

    // Labels for a single-file collection covering addresses 0..7, like a tiny shared.json.
    internal static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> Labels =
        new Dictionary<string, IReadOnlyDictionary<string, int>>
        {
            ["shared"] = Enumerable.Range(0, ChunkCount).ToDictionary(OpcodeRegistry.LabelAt, address => address),
        };

    // Hand-built little-endian WORD bytes; test expectations are hand-computed
    // literals, never output of the code under test.
    internal static byte[] Words(params int[] values)
    {
        var bytes = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), unchecked((ushort)values[i]));
        return bytes;
    }

    // Decode must keep taking the opcode as a 0x… literal call argument: the coverage
    // machine check (OpcodeRegistryCompletenessTests) scans the Opcode*Tests.cs
    // sources for `Decode(<index>, 0xOPCODE, ...)` shapes; the harness must not
    // absorb the opcode literal into an indirect layer.
    internal static JsonObject Decode(int index, params int[] instruction)
    {
        ushort opcode = unchecked((ushort)instruction[0]);
        ushort a = unchecked((ushort)instruction[1]), b = unchecked((ushort)instruction[2]), c = unchecked((ushort)instruction[3]);
        return OpcodeRegistry.DecodeCommand(index, ChunkCount, opcode, a, b, c);
    }

    internal static byte[] Reencode(JsonObject command)
    {
        OpcodeRegistry.ValidateCommand(command, "shared", ChunkCount);
        var bytes = new byte[8];
        OpcodeRegistry.EncodeCommand(command, "shared", Labels, bytes);
        return bytes;
    }

    internal static byte[] EditAndReencode(JsonObject command, string field, JsonNode? value)
    {
        command[field] = value;
        return Reencode(command);
    }
}
