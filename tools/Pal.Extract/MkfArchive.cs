using System.Security.Cryptography;

namespace Pal.Extract;

// Validates the complete offset table once; reads selected chunks only within an explicit budget.
internal sealed class MkfArchive : IDisposable
{
    private readonly FileStream stream;
    internal string Name { get; }
    internal long ByteLength => stream.Length;
    internal string Sha256 { get; }
    internal List<ChunkData> Chunks { get; }

    private MkfArchive(string name, FileStream stream, string sha256, List<ChunkData> chunks)
    { Name = name; this.stream = stream; Sha256 = sha256; Chunks = chunks; }

    internal byte[] ReadChunk(int index, int maximumBytes)
    {
        if (index < 0 || index >= Chunks.Count)
            throw new InvalidDataException($"{Name}: chunk {index}: index is outside the archive.");
        var chunk = Chunks[index];
        if (chunk.ByteLength > maximumBytes)
            throw new InvalidDataException($"{Name}: chunk {index}: exceeds the resource limit.");
        var bytes = new byte[(int)chunk.ByteLength];
        stream.Position = chunk.Offset;
        stream.ReadExactly(bytes);
        return bytes;
    }

    public void Dispose() => stream.Dispose();

    internal static List<MkfArchive> OpenRequired(string input, string[] names, int maximumChunks = int.MaxValue)
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
        var archives = new List<MkfArchive>();
        try
        {
            foreach (var name in names)
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
                    archives.Add(ReadArchive(matches[0], name, maximumChunks));
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

            return archives;
        }
        catch { foreach (var archive in archives) archive.Dispose(); throw; }
    }

    private static MkfArchive ReadArchive(string path, string name, int maximumChunks)
    {
        var stream = File.OpenRead(path);
        try
        {
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
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
            if (firstOffset / 4 - 1 > maximumChunks)
                throw new InvalidDataException($"{name}: offset table exceeds the chunk limit.");
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
            return new MkfArchive(name, stream, sha256, chunks);
        }
        catch { stream.Dispose(); throw; }
    }

    internal sealed record ChunkData(int Index, uint Offset, uint ByteLength, byte[] Prefix);
}
