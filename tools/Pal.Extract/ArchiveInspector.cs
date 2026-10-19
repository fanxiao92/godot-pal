using System.Buffers.Binary;

namespace Pal.Extract;

internal static class ArchiveInspector
{
    private static readonly string[] RequiredNames =
        ["data.mkf", "gop.mkf", "map.mkf", "mgo.mkf", "pat.mkf", "sss.mkf"];

    internal static InspectionReport Inspect(string input)
    {
        var archives = MkfArchive.OpenRequired(input, RequiredNames);
        try
        {
            var versionHint = GetVersionHint(archives);
            return new InspectionReport(1, versionHint, archives.Select(archive => new ArchiveReport(
                archive.Name, archive.ByteLength, archive.Sha256, archive.Chunks.Count,
                archive.Chunks.Count(chunk => chunk.ByteLength == 0),
                archive.Chunks.Select(chunk => DescribeChunk(archive.Name, chunk, versionHint)).ToArray())).ToArray());
        }
        finally { foreach (var archive in archives) archive.Dispose(); }
    }

    private static string GetVersionHint(List<MkfArchive> archives)
    {
        var sss = archives.Single(archive => archive.Name == "sss.mkf");
        var objectSize = sss.Chunks.Count > 2 ? sss.Chunks[2].ByteLength : 0;
        var windowsObjects = objectSize > 0 && objectSize % 14 == 0 && objectSize % 12 != 0;
        var dosObjects = objectSize > 0 && objectSize % 12 == 0 && objectSize % 14 != 0;
        var probes = archives.Where(archive => archive.Name is "map.mkf" or "mgo.mkf")
            .Select(archive => archive.Chunks.FirstOrDefault(chunk => chunk.Prefix.Length >= 4)).ToArray();
        if (probes.Any(probe => probe is null))
        {
            return "unknown";
        }
        var dosCount = probes.Count(probe => IsYj1(probe!.Prefix));
        if (dosCount == 1 || (dosCount == 2 && windowsObjects) || (dosCount == 0 && dosObjects))
        {
            return "inconsistent";
        }
        return dosCount == 2 ? "dos" : windowsObjects ? "windows" : "unknown";
    }

    private static ChunkReport DescribeChunk(string name, MkfArchive.ChunkData chunk, string versionHint)
    {
        string hint;
        uint? declaredLength = null;
        if (chunk.ByteLength == 0)
        {
            hint = "empty";
        }
        else if (name is not ("map.mkf" or "mgo.mkf"))
        {
            hint = "raw";
        }
        else if (IsYj1(chunk.Prefix))
        {
            hint = "yj1";
            if (chunk.Prefix.Length >= 8)
            {
                declaredLength = BinaryPrimitives.ReadUInt32LittleEndian(chunk.Prefix.AsSpan(4));
            }
        }
        else if (versionHint == "windows" && chunk.Prefix.Length >= 4)
        {
            hint = "yj2-candidate";
            declaredLength = BinaryPrimitives.ReadUInt32LittleEndian(chunk.Prefix);
        }
        else
        {
            hint = "unknown";
        }
        return new ChunkReport(chunk.Index, chunk.Offset, chunk.ByteLength, hint, declaredLength);
    }

    private static bool IsYj1(byte[] prefix) => prefix.AsSpan().StartsWith("YJ_1"u8);
}

internal sealed record InspectionReport(int FormatVersion, string VersionHint, ArchiveReport[] Archives);
internal sealed record ArchiveReport(string Name, long ByteLength, string Sha256, int ChunkCount,
    int EmptyChunkCount, ChunkReport[] Chunks);
internal sealed record ChunkReport(int Index, uint Offset, uint ByteLength, string CompressionHint,
    uint? DeclaredDecompressedByteLength);
