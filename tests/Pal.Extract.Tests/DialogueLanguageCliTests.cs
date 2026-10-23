using System.Text.Json;
using Xunit;

namespace Pal.Extract.Tests;

public class DialogueLanguageCliTests
{
    [Fact]
    public async Task Extract_dialogue_preserves_each_language_message_range_blank_entries_and_clear_controls()
    {
        string root = Path.Combine(Path.GetTempPath(), $"pal-dialogue-{Guid.NewGuid():N}");
        string input = Path.Combine(root, "input");
        string output = Path.Combine(root, "output");
        string reference = Path.Combine(root, "reference");
        Directory.CreateDirectory(input);
        WriteReference(reference);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(input, "m_chs.slf"),
                "[BEGIN SETTING]\nUseISOFont=1\n[END SETTING]\n[BEGIN MESSAGE] 61\nline one\n\n[CLEAR MESSAGE]\nline two\n[END MESSAGE] 62\n", new System.Text.UTF8Encoding(false));
            await File.WriteAllTextAsync(Path.Combine(input, "m_eng.slf"),
                "[BEGIN MESSAGE] 61\nfirst\nsecond\n[END MESSAGE] 62\n", new System.Text.UTF8Encoding(false));
            WriteDialogueArchives(input);

            var result = await CliProcess.Run(root, ["extract-dialogue", "--input", input, "--reference", reference, "--output", output]);

            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Stderr);
            Assert.Empty(result.Stdout);
            using var chinese = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output, "zh-Hans.json")));
            using var english = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output, "en.json")));
            Assert.Equal("zh-Hans", chinese.RootElement.GetProperty("language").GetString());
            Assert.Equal("en", english.RootElement.GetProperty("language").GetString());
            var zhBlock = Assert.Single(chinese.RootElement.GetProperty("messages").EnumerateArray());
            Assert.Equal(61, zhBlock.GetProperty("startMessageId").GetInt32());
            Assert.Equal(62, zhBlock.GetProperty("endMessageId").GetInt32());
            var entries = zhBlock.GetProperty("entries").EnumerateArray().ToArray();
            Assert.Equal(4, entries.Length);
            Assert.Equal("text", entries[0].GetProperty("kind").GetString());
            Assert.Equal("line one", entries[0].GetProperty("text").GetString());
            Assert.Equal(string.Empty, entries[1].GetProperty("text").GetString());
            Assert.Equal("clear", entries[2].GetProperty("kind").GetString());
            Assert.Equal("line two", entries[3].GetProperty("text").GetString());
            Assert.Equal(2, Assert.Single(english.RootElement.GetProperty("messages").EnumerateArray())
                .GetProperty("entries").GetArrayLength());
            Assert.Equal(18, new FileInfo(Path.Combine(output, "images", "portrait-059.pimg")).Length);
            Assert.Equal(18, new FileInfo(Path.Combine(output, "images", "prompt-00.pimg")).Length);
            Assert.Equal(18, new FileInfo(Path.Combine(output, "images", "prompt-01.pimg")).Length);
            Assert.Equal(18, new FileInfo(Path.Combine(output, "images", "prompt-02.pimg")).Length);
            using var imageIndex = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output, "images", "index.json")));
            Assert.Equal(new[] { 0, 1, 2 }, imageIndex.RootElement.GetProperty("prompt").GetProperty("frames").EnumerateArray().Select(value => value.GetInt32()));
            using var font = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output, "font.json")));
            var isoGlyph = Assert.Single(font.RootElement.GetProperty("glyphs").EnumerateArray()
                .Where(glyph => glyph.GetProperty("codepoint").GetInt32() == 'l'));
            Assert.Equal("ISO", isoGlyph.GetProperty("source").GetString());
            Assert.Equal(8, isoGlyph.GetProperty("width").GetInt32());
            Assert.Equal(15, isoGlyph.GetProperty("height").GetInt32());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Extract_dialogue_rejects_missing_or_malformed_language_content_without_fallback()
    {
        string root = Path.Combine(Path.GetTempPath(), $"pal-dialogue-{Guid.NewGuid():N}");
        string input = Path.Combine(root, "input");
        string reference = Path.Combine(root, "reference");
        Directory.CreateDirectory(input);
        WriteReference(reference);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(input, "m_chs.slf"), "[BEGIN MESSAGE] 61\ntext\n[END MESSAGE] 62\n");
            WriteDialogueArchives(input);
            var missing = await CliProcess.Run(root, ["extract-dialogue", "--input", input, "--reference", reference, "--output", Path.Combine(root, "missing")]);
            Assert.Equal(1, missing.ExitCode);
            Assert.Contains("m_eng.slf", missing.Stderr);
            Assert.Empty(missing.Stdout);

            await File.WriteAllTextAsync(Path.Combine(input, "m_eng.slf"), "[BEGIN MESSAGE] 61\ninvalid\n[END MESSAGE] 60\n");
            var malformed = await CliProcess.Run(root, ["extract-dialogue", "--input", input, "--reference", reference, "--output", Path.Combine(root, "invalid")]);
            Assert.Equal(1, malformed.ExitCode);
            Assert.Contains("range", malformed.Stderr, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(malformed.Stdout);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteReference(string reference)
    {
        Directory.CreateDirectory(reference);
        string iso = string.Join(",", Enumerable.Repeat("0", 256 * 15));
        File.WriteAllText(Path.Combine(reference, "ascii.h"), $"static unsigned char iso_font[] = {{{iso}}};");
        File.WriteAllText(Path.Combine(reference, "fontglyph.h"), "// fixture reference tables");
    }

    private static void WriteDialogueArchives(string input)
    {
        var rgmChunks = Enumerable.Range(0, 60).Select(_ => Array.Empty<byte>()).ToArray();
        rgmChunks[59] = [1, 0, 1, 0, 1, 5];
        MkfFixture.Write(input, "RGM.MKF", rgmChunks);
        var dataChunks = Enumerable.Range(0, 13).Select(_ => Array.Empty<byte>()).ToArray();
        dataChunks[12] = [3, 0, 3, 0, 6, 0, 1, 0, 1, 0, 1, 7, 1, 0, 1, 0, 1, 8];
        MkfFixture.Write(input, "DATA.MKF", dataChunks);
    }
}
