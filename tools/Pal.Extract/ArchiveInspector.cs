using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Pal.Extract;

internal static class ArchiveInspector
{
    private static readonly string[] RequiredNames =
        ["data.mkf", "gop.mkf", "map.mkf", "mgo.mkf", "pat.mkf", "sss.mkf"];

    internal static InspectionReport Inspect(string input)
    {
        string[] files;
        try
        {
            files = Directory.GetFiles(input);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new InvalidDataException($"Input directory could not be read ({exception.GetType().Name}).", exception);
        }
        var archives = new List<ArchiveData>();
        foreach (var name in RequiredNames)
        {
            var matches = files.Where(file => string.Equals(Path.GetFileName(file), name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length == 0)
            {
                throw new InvalidDataException($"{name}: required archive is missing.");
            }
            if (matches.Length > 1)
            {
                throw new InvalidDataException($"{name}: ambiguous case-insensitive file names.");
            }
            try
            {
                archives.Add(ReadArchive(matches[0], name));
            }
            catch (IOException exception)
            {
                throw new InvalidDataException($"{name}: unable to read archive ({exception.GetType().Name}).", exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                throw new InvalidDataException($"{name}: unable to read archive ({exception.GetType().Name}).", exception);
            }
        }

        var versionHint = GetVersionHint(archives);
        return new InspectionReport(1, versionHint, archives.Select(archive => new ArchiveReport(
            archive.Name, archive.ByteLength, archive.Sha256, archive.Chunks.Count,
            archive.Chunks.Count(chunk => chunk.ByteLength == 0),
            archive.Chunks.Select(chunk => DescribeChunk(archive.Name, chunk, versionHint)).ToArray())).ToArray());
    }

    private static ArchiveData ReadArchive(string path, string name)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (stream.Length < 4)
        {
            throw new InvalidDataException($"{name}: header is truncated (requires 4 bytes).");
        }
        var firstOffset = reader.ReadUInt32();
        if (firstOffset < 4)
        {
            throw new InvalidDataException($"{name}: offset table must contain at least one DWORD.");
        }
        if (firstOffset % 4 != 0)
        {
            throw new InvalidDataException($"{name}: offset table is not DWORD-aligned.");
        }
        if (firstOffset > stream.Length)
        {
            throw new InvalidDataException($"{name}: offset table extends beyond end of file.");
        }
        var chunks = new List<ChunkData>();
        var previous = firstOffset;
        for (var index = 0; index < firstOffset / 4 - 1; index++)
        {
            var next = reader.ReadUInt32();
            if (next < previous)
            {
                throw new InvalidDataException($"{name}: chunk {index}: offsets are not ordered.");
            }
            if (next > stream.Length)
            {
                throw new InvalidDataException($"{name}: chunk {index}: offset exceeds end of file.");
            }
            var tablePosition = stream.Position;
            var length = next - previous;
            stream.Position = previous;
            var prefix = reader.ReadBytes((int)Math.Min(8, length));
            stream.Position = tablePosition;
            chunks.Add(new ChunkData(index, previous, length, prefix));
            previous = next;
        }
        if (previous != stream.Length)
        {
            throw new InvalidDataException($"{name}: final offset does not match file length.");
        }
        stream.Position = 0;
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(stream));
        return new ArchiveData(name, stream.Length, sha256, chunks);
    }

    private static string GetVersionHint(List<ArchiveData> archives)
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

    private static ChunkReport DescribeChunk(string name, ChunkData chunk, string versionHint)
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

    private sealed record ArchiveData(string Name, long ByteLength, string Sha256, List<ChunkData> Chunks);
    private sealed record ChunkData(int Index, uint Offset, uint ByteLength, byte[] Prefix);
}

internal sealed record InspectionReport(int FormatVersion, string VersionHint, ArchiveReport[] Archives);
internal sealed record ArchiveReport(string Name, long ByteLength, string Sha256, int ChunkCount,
    int EmptyChunkCount, ChunkReport[] Chunks);
internal sealed record ChunkReport(int Index, uint Offset, uint ByteLength, string CompressionHint,
    uint? DeclaredDecompressedByteLength);
