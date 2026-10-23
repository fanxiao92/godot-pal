using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;
using Godot.Collections;
using Pal.Game.Resources;

namespace Pal.Game.Editor;

/// <summary>Explicit offline conversion from pal-extract dialogue v1 files to native resources.</summary>
public partial class PrepareDialogue : Node
{
    private string _output = "";

    public override void _Ready()
    {
        try
        {
            string[] args = OS.GetCmdlineUserArgs();
            Require(args.Length == 4 && args[0] == "--prepare-input" && args[2] == "--prepare-output", "invalid dialogue importer arguments");
            string input = Path.GetFullPath(args[1]);
            _output = ProjectSettings.LocalizePath(Path.GetFullPath(args[3])).TrimEnd('/');
            Require(_output == "res://Assets" || _output.StartsWith("res://Assets/", StringComparison.Ordinal), "native output must be under project Assets");
            ValidateFileSet(input);
            byte[] indexBytes = File.ReadAllBytes(Path.Combine(input, "index.json"));
            using JsonDocument index = JsonDocument.Parse(indexBytes);
            JsonElement root = index.RootElement;
            Properties(root, "formatVersion", "defaultLanguage", "languages", "messageReferences");
            Require(Int(root, "formatVersion") == 1 && Text(root, "defaultLanguage") == "zh-Hans"
                && Text(root, "messageReferences") == "original-start-message-id-and-span", "unsupported dialogue package manifest");
            Require(root.GetProperty("languages").EnumerateArray().Select(x => x.GetString()).Order().SequenceEqual(new[] { "en", "zh-Hans" }.Order()), "dialogue language set mismatch");
            string packageId = Hash(indexBytes);
            byte[] imageManifestBytes = File.ReadAllBytes(Path.Combine(input, "images", "index.json"));
            using JsonDocument imageManifest = JsonDocument.Parse(imageManifestBytes);
            Require(Int(imageManifest.RootElement, "formatVersion") == 1 && Text(imageManifest.RootElement, "palette") == "current-scene-palette", "unsupported dialogue image manifest");
            string imageId = Hash(imageManifestBytes);
            JsonElement imageRoot = imageManifest.RootElement;
            Properties(imageRoot, "formatVersion", "palette", "sources", "portrait", "prompt");
            JsonElement portraitRecord = imageRoot.GetProperty("portrait");
            Properties(portraitRecord, "sourceArchive", "sourceChunk", "file");
            Require(Text(portraitRecord, "sourceArchive") == "RGM.MKF" && Int(portraitRecord, "sourceChunk") == 59 && Text(portraitRecord, "file") == "portrait-059.pimg", "dialogue portrait reference mismatch");
            JsonElement promptRecord = imageRoot.GetProperty("prompt");
            Properties(promptRecord, "sourceArchive", "sourceChunk", "frames");
            Require(Text(promptRecord, "sourceArchive") == "DATA.MKF" && Int(promptRecord, "sourceChunk") == 12
                && promptRecord.GetProperty("frames").EnumerateArray().Select(value => value.GetInt32()).SequenceEqual(new[] { 0, 1, 2 }), "dialogue prompt frame references mismatch");
            JsonElement imageSources = imageRoot.GetProperty("sources");
            Require(imageSources.GetArrayLength() == 2, "dialogue image source set is incomplete");
            CheckSource(imageSources[0], "RGM.MKF", "byteLength", "sha256"); CheckSource(imageSources[1], "DATA.MKF", "byteLength", "sha256");
            PalDialogueLanguage chinese = Language(input, "zh-Hans", packageId);
            PalDialogueLanguage english = Language(input, "en", packageId);
            PalDialogueFont font = Font(input, packageId);
            PalDialogueIndexedImage portrait = Image(input, "portrait-059", imageId);
            PalDialogueIndexedImage[] prompts = Enumerable.Range(0, 3).Select(frame => Image(input, $"prompt-0{frame}", imageId)).ToArray();
            Save(chinese, "Language.zh-Hans.tres"); Save(english, "Language.en.tres"); Save(font, "Font.tres");
            Save(portrait, "Portrait-059.tres");
            for (int frame = 0; frame < prompts.Length; frame++) Save(prompts[frame], $"Prompt-{frame:00}.tres");
            VerifySavedResources(input, packageId, imageId);
            GD.Print("PAL_PREPARE_DIALOGUE_NATIVE");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PrintErr("Dialogue native import failed: " + exception.Message);
            GetTree().Quit(1);
        }
    }

    private static void ValidateFileSet(string input)
    {
        Require(Directory.Exists(input), "dialogue input directory is missing");
        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            "index.json", "zh-Hans.json", "en.json", "font.json", "images/index.json", "images/portrait-059.pimg",
            "images/prompt-00.pimg", "images/prompt-01.pimg", "images/prompt-02.pimg"
        };
        var actual = Directory.EnumerateFiles(input, "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(input, path).Replace('\\', '/')).ToHashSet(StringComparer.Ordinal);
        Require(actual.SetEquals(expected), "dialogue input file set is incomplete or contains unexpected files");
        foreach (string file in actual)
        {
            string path = Path.Combine(input, file);
            Require((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0, "dialogue input contains a link/reparse point");
            Require(new FileInfo(path).Length <= 64 * 1024 * 1024, "dialogue input file exceeds the size limit");
        }
        string[] directories = Directory.EnumerateDirectories(input, "*", SearchOption.AllDirectories).ToArray();
        Require(directories.Select(path => Path.GetRelativePath(input, path).Replace('\\', '/')).SequenceEqual(new[] { "images" }), "dialogue input directory set is incomplete or contains unexpected directories");
        foreach (string directory in directories)
            Require((File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0, "dialogue input contains a link/reparse point");
    }

    private PalDialogueLanguage Language(string input, string language, string packageId)
    {
        string path = Path.Combine(input, language + ".json");
        byte[] bytes = File.ReadAllBytes(path);
        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement root = document.RootElement;
        Properties(root, "formatVersion", "language", "source", "messages");
        Require(Int(root, "formatVersion") == 1 && Text(root, "language") == language, "dialogue language identity or version mismatch");
        JsonElement source = root.GetProperty("source");
        CheckSource(source, language == "en" ? "m_eng.slf" : "m_chs.slf");
        var resource = new PalDialogueLanguage { Language = language, SourceIdentity = $"package:{packageId};language:{Hash(bytes)};source:{Text(source, "sha256")}" };
        foreach (JsonElement item in root.GetProperty("messages").EnumerateArray())
        {
            Properties(item, "startMessageId", "endMessageId", "entries");
            var range = new PalDialogueMessageRange { StartMessageId = Int(item, "startMessageId"), EndMessageId = Int(item, "endMessageId") };
            Require(range.StartMessageId >= 0 && range.EndMessageId >= range.StartMessageId && range.EndMessageId <= ushort.MaxValue, "invalid dialogue message range");
            foreach (JsonElement entry in item.GetProperty("entries").EnumerateArray())
            {
                string kind = Text(entry, "kind");
                Properties(entry, "kind", "text");
                if (kind == "text") range.Entries.Add(new PalDialogueMessageEntry { Kind = PalDialogueMessageEntry.EntryKind.Text, Text = entry.GetProperty("text").GetString() ?? throw new InvalidDataException("null dialogue text") });
                else if (kind == "clear")
                {
                    Require(entry.GetProperty("text").ValueKind == JsonValueKind.Null, "CLEAR entry text must be null");
                    range.Entries.Add(new PalDialogueMessageEntry { Kind = PalDialogueMessageEntry.EntryKind.Clear });
                }
                else throw new InvalidDataException("unsupported dialogue entry kind");
            }
            Require(range.Entries.Count > 0, "dialogue range has no entries");
            resource.Messages.Add(range);
        }
        _ = resource.ToCorePackage();
        return resource;
    }

    private PalDialogueFont Font(string input, string packageId)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(input, "font.json"));
        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement root = document.RootElement;
        Properties(root, "formatVersion", "source", "glyphs");
        Require(Int(root, "formatVersion") == 1, "unsupported dialogue font version");
        JsonElement sources = root.GetProperty("source");
        Require(sources.GetArrayLength() == 2, "dialogue font source set is incomplete");
        CheckSource(sources[0], "ascii.h", "role", "sha256"); CheckSource(sources[1], "fontglyph.h", "role", "sha256");
        Require(Text(sources[0], "role") == "ISO" && Text(sources[1], "role") == "Unifont", "dialogue font source roles mismatch");
        var font = new PalDialogueFont { SourceIdentity = $"package:{packageId};font:{Hash(bytes)}" };
        var seen = new HashSet<int>();
        foreach (JsonElement item in root.GetProperty("glyphs").EnumerateArray())
        {
            Properties(item, "codepoint", "source", "width", "height", "rowBytes", "bitmap");
            int codepoint = Int(item, "codepoint"), width = Int(item, "width"), height = Int(item, "height"), rowBytes = Int(item, "rowBytes");
            byte[] bitmap;
            try { bitmap = Convert.FromHexString(Text(item, "bitmap")); }
            catch (FormatException error) { throw new InvalidDataException("dialogue glyph bitmap is not hexadecimal", error); }
            string source = Text(item, "source");
            bool iso = source == "ISO" && codepoint <= 255 && width == 8 && height == 15 && rowBytes == 1;
            bool unifont = source == "Unifont" && codepoint is >= 256 and <= 0xFFFF && !(codepoint is >= 0xD800 and < 0xF900)
                && height == 16 && rowBytes is 1 or 2 && width == rowBytes * 8;
            Require(codepoint is >= 0 and <= 0xFFFF && seen.Add(codepoint) && (iso || unifont)
                && bitmap.Length == rowBytes * height, "invalid or duplicate dialogue glyph");
            font.Glyphs.Add(new PalDialogueGlyph { Codepoint = codepoint, Width = width, Height = height, RowBytes = rowBytes, Bitmap = bitmap });
        }
        Require(font.Glyphs.Count > 0, "dialogue font has no glyphs");
        return font;
    }

    private PalDialogueIndexedImage Image(string input, string name, string manifestId)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(input, "images", name + ".pimg"));
        Require(bytes.Length >= 16 && bytes.AsSpan(0, 8).SequenceEqual("PALIMG1\0"u8), "invalid indexed image header");
        int width = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8, 4));
        int height = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12, 4));
        Require(width > 0 && height > 0 && (long)width * height <= 16 * 1024 * 1024 && bytes.Length == 16L + (long)width * height * 2, "indexed image dimensions or byte length mismatch");
        byte[] indices = new byte[width * height], alpha = new byte[indices.Length];
        for (int i = 0; i < indices.Length; i++) { indices[i] = bytes[16 + i * 2]; alpha[i] = bytes[17 + i * 2]; }
        return new PalDialogueIndexedImage { Width = width, Height = height, SourceIdentity = $"images:{manifestId};pimg:{Hash(bytes)}", PaletteIndices = indices, Alpha = alpha };
    }

    private static void ValidateGlyphCoverage(PalDialogueLanguage language, PalDialogueFont font)
    {
        var available = font.Glyphs.Select(glyph => glyph.Codepoint).ToHashSet();
        foreach (PalDialogueMessageRange range in language.Messages)
        foreach (PalDialogueMessageEntry entry in range.Entries)
            if (entry.Kind == PalDialogueMessageEntry.EntryKind.Text)
                foreach (Rune rune in entry.Text.EnumerateRunes())
                    if (!available.Contains(rune.Value))
                        throw new InvalidDataException($"Dialogue font has no glyph for U+{rune.Value:X4}.");
    }

    private static string ImageIdentity(string input, string name, string manifestId) =>
        $"images:{manifestId};pimg:{Hash(File.ReadAllBytes(Path.Combine(input, "images", name + ".pimg")))}";

    private static string SourceHash(string input, string language)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(input, language + ".json")));
        return Text(document.RootElement.GetProperty("source"), "sha256");
    }

    private void VerifySavedResources(string input, string packageId, string imageId)
    {
        PalDialogueLanguage chinese = ResourceLoader.Load<PalDialogueLanguage>(_output + "/Dialogue/Language.zh-Hans.tres")
            ?? throw new InvalidDataException("saved Chinese language resource could not be loaded");
        PalDialogueLanguage english = ResourceLoader.Load<PalDialogueLanguage>(_output + "/Dialogue/Language.en.tres")
            ?? throw new InvalidDataException("saved English language resource could not be loaded");
        PalDialogueFont font = ResourceLoader.Load<PalDialogueFont>(_output + "/Dialogue/Font.tres")
            ?? throw new InvalidDataException("saved dialogue font resource could not be loaded");
        PalDialogueIndexedImage portrait = ResourceLoader.Load<PalDialogueIndexedImage>(_output + "/Dialogue/Portrait-059.tres")
            ?? throw new InvalidDataException("saved portrait resource could not be loaded");
        string chineseIdentity = $"package:{packageId};language:{Hash(File.ReadAllBytes(Path.Combine(input, "zh-Hans.json")))};source:{SourceHash(input, "zh-Hans")}";
        string englishIdentity = $"package:{packageId};language:{Hash(File.ReadAllBytes(Path.Combine(input, "en.json")))};source:{SourceHash(input, "en")}";
        string fontIdentity = $"package:{packageId};font:{Hash(File.ReadAllBytes(Path.Combine(input, "font.json")))}";
        Require(chinese.SourceIdentity == chineseIdentity && english.SourceIdentity == englishIdentity
            && font.SourceIdentity == fontIdentity, "native language/font source identities do not match their manifests");
        Require(portrait.SourceIdentity == ImageIdentity(input, "portrait-059", imageId), "native portrait source identity does not match its manifest");
        _ = chinese.ToCorePackage().Resolve(61, 1);
        _ = english.ToCorePackage().Resolve(61, 1);
        ValidateGlyphCoverage(chinese, font);
        ValidateGlyphCoverage(english, font);
        portrait.Validate();
        for (int frame = 0; frame < 3; frame++)
        {
            PalDialogueIndexedImage prompt = ResourceLoader.Load<PalDialogueIndexedImage>(_output + $"/Dialogue/Prompt-{frame:00}.tres")
                ?? throw new InvalidDataException("saved prompt resource could not be loaded");
            prompt.Validate();
            Require(prompt.SourceIdentity == ImageIdentity(input, $"prompt-0{frame}", imageId), "native prompt source identity does not match its manifest");
        }
    }

    private void Save(Resource resource, string name)
    {
        string uri = _output + "/Dialogue/" + name;
        string full = ProjectSettings.GlobalizePath(uri);
        Require(!File.Exists(full), "native dialogue resource already exists: " + name);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        Require(ResourceSaver.Save(resource, uri) == Error.Ok, "could not save native dialogue resource: " + name);
    }

    private static void CheckSource(JsonElement source, string expectedFile, params string[] extraProperties)
    {
        Properties(source, new[] { "file", "sha256" }.Concat(extraProperties).ToArray());
        Require(Text(source, "file") == expectedFile, "dialogue source filename mismatch");
        string hash = Text(source, "sha256");
        Require(hash.Length == 64 && hash.All(Uri.IsHexDigit) && hash == hash.ToLowerInvariant(), "invalid dialogue source SHA-256");
        if (source.TryGetProperty("byteLength", out JsonElement length))
            Require(length.ValueKind == JsonValueKind.Number && length.GetInt64() > 0, "invalid source byte length");
    }

    private static void Properties(JsonElement value, params string[] allowed)
    {
        Require(value.ValueKind == JsonValueKind.Object, "dialogue manifest value must be an object");
        var names = new HashSet<string>(StringComparer.Ordinal);
        var known = new HashSet<string>(allowed, StringComparer.Ordinal);
        foreach (JsonProperty property in value.EnumerateObject())
            Require(names.Add(property.Name) && known.Contains(property.Name), "dialogue manifest has a duplicate or unexpected field");
        Require(known.SetEquals(names), "dialogue manifest is missing a required field");
    }

    private static int Int(JsonElement element, string name) => element.GetProperty(name).GetInt32();
    private static string Text(JsonElement element, string name) => element.GetProperty(name).GetString() ?? throw new InvalidDataException("null string in dialogue package");
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
