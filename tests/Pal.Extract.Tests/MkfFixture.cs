namespace Pal.Extract.Tests;

internal static class MkfFixture
{
    internal static void Write(string input, string name, params byte[][] chunks)
    {
        using var writer = new BinaryWriter(File.Create(Path.Combine(input, name)));
        int offset = (chunks.Length + 1) * 4;
        writer.Write(offset);
        foreach (var chunk in chunks) { offset += chunk.Length; writer.Write(offset); }
        foreach (var chunk in chunks) writer.Write(chunk);
    }
}
