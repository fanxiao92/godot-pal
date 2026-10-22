using System.Buffers.Binary;
using System.Text.Json;

namespace Pal.Extract;

internal static class DialogueImageExporter
{
    private const int MaximumImageBytes = 16 * 1024 * 1024;

    internal static void Export(string input, string destination)
    {
        using var archives = new DisposableArchives(MkfArchive.OpenRequired(input, ["RGM.MKF", "DATA.MKF"], maximumChunks: 4096));
        MkfArchive rgm = archives.Items[0];
        MkfArchive data = archives.Items[1];
        byte[] portraitChunk = rgm.ReadChunk(59, MaximumImageBytes);
        IndexedImage portrait = SpriteDecoder.DecodeRle(portraitChunk);
        byte[] promptChunk = data.ReadChunk(12, MaximumImageBytes);
        int frameCount = SpriteDecoder.GetFrameCount(promptChunk);
        if (frameCount < 2) throw new InvalidDataException("DATA.MKF chunk 12 must contain prompt frames 1 and 2.");
        IndexedImage firstPrompt = SpriteDecoder.DecodeFrame(promptChunk, 1);
        IndexedImage secondPrompt = SpriteDecoder.DecodeFrame(promptChunk, 2);

        string images = Path.Combine(destination, "images");
        Directory.CreateDirectory(images);
        WriteImage(images, "portrait-059", portrait);
        WriteImage(images, "prompt-01", firstPrompt);
        WriteImage(images, "prompt-02", secondPrompt);
        WriteJson(Path.Combine(images, "index.json"), new
        {
            formatVersion = 1,
            palette = "current-scene-palette",
            sources = new[]
            {
                new { file = rgm.Name, byteLength = rgm.ByteLength, sha256 = rgm.Sha256 },
                new { file = data.Name, byteLength = data.ByteLength, sha256 = data.Sha256 }
            },
            portrait = new { sourceArchive = rgm.Name, sourceChunk = 59, file = "portrait-059.pimg" },
            prompt = new { sourceArchive = data.Name, sourceChunk = 12, frames = new[] { 1, 2 } }
        });
    }

    private static void WriteImage(string directory, string id, IndexedImage image)
    {
        string path = Path.Combine(directory, id + ".pimg");
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write("PALIMG1\0"u8);
        Span<byte> dimensions = stackalloc byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(dimensions, image.Width);
        BinaryPrimitives.WriteInt32LittleEndian(dimensions[4..], image.Height);
        stream.Write(dimensions);
        for (int i = 0; i < image.Pixels.Length; i++)
        {
            stream.WriteByte(image.Pixels[i]);
            stream.WriteByte(image.Alpha[i]);
        }
    }

    private static void WriteJson(string path, object document)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, document, new JsonSerializerOptions { WriteIndented = true });
    }

    private sealed class DisposableArchives(List<MkfArchive> items) : IDisposable
    {
        internal List<MkfArchive> Items { get; } = items;
        public void Dispose() { foreach (MkfArchive archive in Items) archive.Dispose(); }
    }
}
