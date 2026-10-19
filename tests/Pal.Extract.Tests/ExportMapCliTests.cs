using System.Buffers.Binary;
using System.Text.Json;
using Xunit;

namespace Pal.Extract.Tests;

public class ExportMapCliTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"pal-map-test-{Guid.NewGuid():N}");
    private readonly string input;
    private readonly string output;
    private readonly byte[] map = new byte[65536];

    public ExportMapCliTests()
    {
        input = Path.Combine(directory, "input 数据");
        output = Path.Combine(directory, "output");
        Directory.CreateDirectory(input);
        // Literal worked DWORD: bottom frame 0, height 10, blocked; top frame 0, height 11.
        SetCell(0, 0x0B012A00);
        SetCell(1, 0x00000101); // [0,0,1]: bottom frame 1, height 1, not blocked.
        SetCell(2, 0x00000001); // [0,1,0]
        WriteMap();
        // Two 2x2 frames, each exactly ten bytes. Frame 0 includes opaque index 0 and a skip.
        WriteArchive("Gop.mkf", [], [3,0, 8,0, 13,0, 2,0,2,0, 2,0,1, 0x81,1,2,
            2,0,2,0, 4,3,3,3,3,0]);
        var palette = new byte[768];
        palette[0] = 3; palette[1] = 4; palette[2] = 5;
        palette[3] = 63; palette[7] = 32; palette[11] = 63;
        WriteArchive("Pat.mkf", palette);
    }

    [Fact]
    public async Task Export_map_preserves_original_cell_order_fields_and_explicit_palette_without_other_archives()
    {
        var result = await Export();
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Stderr);
        using var json = ReadJson("map.json");
        var root = json.RootElement;
        Assert.Equal(1, root.GetProperty("formatVersion").GetInt32());
        Assert.Equal(1, root.GetProperty("mapId").GetInt32());
        Assert.Equal(64, root.GetProperty("width").GetInt32());
        Assert.Equal(128, root.GetProperty("height").GetInt32());
        Assert.Equal(2, root.GetProperty("halfRows").GetInt32());
        Assert.Equal("[y,x,h]", root.GetProperty("cellOrder").GetString());
        var cells = root.GetProperty("cells");
        Assert.Equal(16384, cells.GetArrayLength());
        var first = cells[0];
        Assert.Equal(0x0B012A00u, first.GetProperty("raw").GetUInt32());
        Assert.Equal(0, first.GetProperty("bottomFrame").GetInt32());
        Assert.Equal(0, first.GetProperty("topFrame").GetInt32());
        Assert.Equal(10, first.GetProperty("bottomHeight").GetInt32());
        Assert.Equal(11, first.GetProperty("topHeight").GetInt32());
        Assert.True(first.GetProperty("blocked").GetBoolean());
        Assert.Equal(1, cells[1].GetProperty("bottomFrame").GetInt32());
        Assert.Equal(1, cells[1].GetProperty("bottomHeight").GetInt32());
        Assert.False(cells[1].GetProperty("blocked").GetBoolean());
        Assert.Equal(JsonValueKind.Null, cells[1].GetProperty("topFrame").ValueKind);
        Assert.Equal(1, cells[2].GetProperty("x").GetInt32());
        Assert.Equal(0, cells[2].GetProperty("h").GetInt32());
        Assert.Equal(1, cells[128].GetProperty("y").GetInt32());
        using var pat = ReadJson("palette.json");
        Assert.Equal(0, pat.RootElement.GetProperty("paletteId").GetInt32());
        Assert.Equal("day", pat.RootElement.GetProperty("lighting").GetString());
        Assert.Equal("day", pat.RootElement.GetProperty("selectedLighting").GetString());
        var color = pat.RootElement.GetProperty("colors")[0];
        Assert.Equal(12, color.GetProperty("r").GetInt32());
        Assert.Equal(16, color.GetProperty("g").GetInt32());
        Assert.Equal(20, color.GetProperty("b").GetInt32());
        Assert.DoesNotContain(directory, File.ReadAllText(Path.Combine(output, "map.json")));
    }

    [Fact]
    public async Task Export_map_writes_indexed_tiles_and_png_pixels_with_map_coordinates_and_layer_order()
    {
        SetCell(130, 0x00010001); // [1,1,0] at (16,8), top frame 0 over bottom frame 1.
        WriteMap();
        var result = await Export();
        Assert.Equal(0, result.ExitCode);
        using var json = ReadJson("tiles.json");
        Assert.Equal(1, json.RootElement.GetProperty("formatVersion").GetInt32());
        var frames = json.RootElement.GetProperty("frames");
        Assert.Equal(2, frames.GetArrayLength());
        Assert.Equal(0, frames[0].GetProperty("index").GetInt32());
        Assert.Equal(6, frames[0].GetProperty("sourceOffset").GetInt32());
        Assert.Equal(16, frames[1].GetProperty("sourceOffset").GetInt32());
        Assert.Equal(2, frames[0].GetProperty("width").GetInt32());
        Assert.Equal(2, frames[0].GetProperty("height").GetInt32());
        Assert.Equal("tiles/0.png", frames[0].GetProperty("png").GetString());
        Assert.Equal(new byte[] { 0, 1, 0, 2 }, frames[0].GetProperty("indices").GetBytesFromBase64());
        Assert.Equal(new byte[] { 255, 255, 0, 255 }, frames[0].GetProperty("alpha").GetBytesFromBase64());
        var tile = ReadPng("tiles/0.png");
        Assert.Equal((2, 2), (tile.Width, tile.Height));
        Assert.Equal(new byte[] { 12, 16, 20, 255, 252, 0, 0, 255, 12, 16, 20, 0, 0, 128, 0, 255 }, tile.Rgba);
        var preview = ReadPng("preview.png");
        Assert.Equal((2048, 2048), (preview.Width, preview.Height));
        Assert.Equal(new byte[] { 0, 0, 252, 255 }, Pixel(preview, 0, 0));
        Assert.Equal(new byte[] { 12, 16, 20, 255 }, Pixel(preview, 16, 8));
        Assert.Equal(new byte[] { 252, 0, 0, 255 }, Pixel(preview, 17, 8));
        Assert.Equal(new byte[] { 0, 0, 252, 255 }, Pixel(preview, 16, 9)); // Transparent top leaves bottom visible.
        Assert.Equal(new byte[] { 0, 128, 0, 255 }, Pixel(preview, 17, 9));
    }

    [Fact]
    public async Task Export_map_preserves_ninth_frame_bits_and_top_zero_means_absent()
    {
        // 257 nominal aliases of one frame, plus end offset. Independently worked bit fields:
        // bit12 -> bottom256; high word 0x1001 -> top256 (encoded257 minus1).
        var sprite = new byte[522];
        for (int i = 0; i < 257; i++) BinaryPrimitives.WriteUInt16LittleEndian(sprite.AsSpan(i * 2), 258);
        BinaryPrimitives.WriteUInt16LittleEndian(sprite.AsSpan(514), 261); // End sentinel at byte 522.
        new byte[] { 1, 0, 1, 0, 1, 3 }.CopyTo(sprite, 516);
        WriteArchive("Gop.mkf", [], sprite);
        SetCell(130, 0x10011000);
        WriteMap();
        Assert.Equal(0, (await Export()).ExitCode);
        using var json = ReadJson("map.json");
        var cell = json.RootElement.GetProperty("cells")[130];
        Assert.Equal(256, cell.GetProperty("bottomFrame").GetInt32());
        Assert.Equal(256, cell.GetProperty("topFrame").GetInt32());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("cells")[1].GetProperty("topFrame").ValueKind);
    }

    [Theory]
    [InlineData("offsets", "gop.mkf", "offset")]
    [InlineData("rle", "gop.mkf", "RLE")]
    [InlineData("yj2", "map.mkf", "YJ2")]
    [InlineData("yj1", "map.mkf", "YJ1")]
    [InlineData("dimensions", "map.mkf", "65536")]
    [InlineData("bottom-reference", "gop.mkf", "frame 7")]
    [InlineData("top-reference", "gop.mkf", "frame 7")]
    [InlineData("palette", "pat.mkf", "palette")]
    [InlineData("chunk", "map.mkf", "chunk 1")]
    public async Task Export_map_rejects_malformed_resources_without_partial_reports(string kind, string resource, string error)
    {
        switch (kind)
        {
            case "offsets": WriteArchive("Gop.mkf", [], [3, 0, 1, 0, 0, 0]); break;
            case "rle": WriteArchive("Gop.mkf", [], [2, 0, 0, 0, 1, 0, 1, 0, 2, 3]); break;
            case "yj2": WriteArchive("Map.mkf", [], [0, 0, 1, 0]); break;
            case "yj1": WriteArchive("Map.mkf", [], "YJ_1"u8.ToArray()); break;
            case "dimensions": WriteArchive("Map.mkf", [], new Yj2TestData(1).Literal(0).End()); break;
            case "bottom-reference": SetCell(7, 7); WriteMap(); break;
            case "top-reference": SetCell(7, 0x00080000); WriteMap(); break;
            case "palette": WriteArchive("Pat.mkf", new byte[767]); break;
            case "chunk": WriteArchive("Map.mkf", []); break;
        }
        var result = await Export();
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Contains(resource, result.Stderr);
        Assert.Contains(error, result.Stderr, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(directory, result.Stderr);
        Assert.DoesNotContain("   at ", result.Stderr);
        Assert.False(Directory.Exists(output));
    }

    [Fact]
    public async Task Preview_draws_y_then_h_then_x_with_complete_top_layer_after_bottom()
    {
        Array.Clear(map);
        SetCell(130, 1); // A [1,1,0] red at (16,8).
        SetCell(129, 2); // B [1,0,1] blue at (0,16), drawn AFTER A despite storage order.
        SetCell(258, 3); // C [2,1,0] green at (16,24), drawn after B.
        SetCell(3, 0x00020000); // Top red [0,1,1] at (32,0), drawn after ALL bottom rows.
        WriteMap();
        WriteSprite(TransparentFrame(32, 16), SolidFrame(64, 32, 1), SolidFrame(64, 32, 3), SolidFrame(64, 32, 2));
        Assert.Equal(0, (await Export()).ExitCode);
        var preview = ReadPng("preview.png");
        Assert.Equal(new byte[] { 0, 0, 252, 255 }, Pixel(preview, 16, 16));
        Assert.Equal(new byte[] { 0, 128, 0, 255 }, Pixel(preview, 16, 24));
        Assert.Equal(new byte[] { 252, 0, 0, 255 }, Pixel(preview, 32, 24));
    }

    [Fact]
    public async Task Preview_uses_map_c_bottom_fallback_at_the_viewport_boundary()
    {
        Array.Clear(map);
        SetCell(254, 1); // [1,63,0] red bitmap extends past the right grid boundary.
        WriteMap();
        WriteSprite(SolidFrame(32, 16, 3), SolidFrame(64, 16, 1));
        Assert.Equal(0, (await Export()).ExitCode);
        var preview = ReadPng("preview.png");
        Assert.Equal(new byte[] { 252, 0, 0, 255 }, Pixel(preview, 2031, 8));
        Assert.Equal(new byte[] { 0, 0, 252, 255 }, Pixel(preview, 2032, 8)); // x=64 bottom fallback, not transparent.
    }

    [Fact]
    public async Task Export_map_bounds_total_preview_work_even_for_large_transparent_tiles()
    {
        Array.Clear(map);
        WriteMap();
        WriteSprite(TransparentFrame(256, 256));
        var result = await Export();
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Contains("preview", result.Stderr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("limit", result.Stderr, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(output));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    [InlineData("map-zero")]
    [InlineData("map-negative")]
    [InlineData("palette-negative")]
    [InlineData("lighting")]
    [InlineData("empty")]
    public async Task Export_map_requires_all_explicit_arguments_without_echoing_local_paths(string kind)
    {
        var args = new List<string> { "export-map", "--input", input, "--output", output,
            "--map", "1", "--palette", "0", "--lighting", "day" };
        switch (kind)
        {
            case "missing": args.RemoveRange(9, 2); break;
            case "duplicate": args[9] = "--map"; break;
            case "unknown": args[9] = "--unknown"; break;
            case "map-zero": args[6] = "0"; break;
            case "map-negative": args[6] = "-1"; break;
            case "palette-negative": args[8] = "-1"; break;
            case "lighting": args[10] = "guess"; break;
            case "empty": args[4] = ""; break;
        }
        var result = await CliProcess.Run(directory, args.ToArray());
        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Contains("Use --help", result.Stderr);
        Assert.DoesNotContain(directory, result.Stderr);
        Assert.False(Directory.Exists(output));
    }

    [Fact]
    public async Task Export_map_is_deterministic_case_insensitive_read_only_and_accepts_empty_output()
    {
        File.Move(Path.Combine(input, "Map.mkf"), Path.Combine(input, "MAP.MKF"));
        var before = Directory.GetFiles(input).ToDictionary(path => path,
            path => (Bytes: File.ReadAllBytes(path), Modified: File.GetLastWriteTimeUtc(path)));
        foreach (string path in before.Keys) File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
        try
        {
            Directory.CreateDirectory(output);
            Assert.Equal(0, (await Export()).ExitCode);
            string secondOutput = Path.Combine(directory, "second");
            Assert.Equal(0, (await Export(secondOutput)).ExitCode);
            var first = Directory.GetFiles(output, "*", SearchOption.AllDirectories)
                .ToDictionary(path => Path.GetRelativePath(output, path), File.ReadAllBytes);
            var second = Directory.GetFiles(secondOutput, "*", SearchOption.AllDirectories)
                .ToDictionary(path => Path.GetRelativePath(secondOutput, path), File.ReadAllBytes);
            Assert.Equal(6, first.Count);
            Assert.Equal(first.Keys.Order(), second.Keys.Order());
            foreach (var (path, bytes) in first) Assert.Equal(bytes, second[path]);
            Assert.Equal(before.Keys.Order(), Directory.GetFiles(input).Order());
            foreach (var (path, snapshot) in before)
            {
                Assert.Equal(snapshot.Bytes, File.ReadAllBytes(path));
                Assert.Equal(snapshot.Modified, File.GetLastWriteTimeUtc(path));
                Assert.True(File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly));
            }
            Assert.Equal(1, (await Export()).ExitCode);
        }
        finally
        {
            foreach (string path in before.Keys) File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Export_map_records_requested_night_and_actual_palette_selection(bool hasNight)
    {
        var palette = new byte[hasNight ? 1536 : 768];
        palette[0] = 3;
        if (hasNight) palette[768] = 7;
        WriteArchive("Pat.mkf", palette);
        Assert.Equal(0, (await Export(lighting: "night")).ExitCode);
        using var json = ReadJson("palette.json");
        var root = json.RootElement;
        Assert.Equal("night", root.GetProperty("lighting").GetString());
        Assert.Equal(hasNight ? "night" : "day", root.GetProperty("selectedLighting").GetString());
        Assert.Equal(hasNight ? 28 : 12, root.GetProperty("colors")[0].GetProperty("r").GetInt32());
        Assert.Equal(hasNight ? new byte[] { 28, 0, 0, 255 } : new byte[] { 12, 0, 0, 255 }, Pixel(ReadPng("tiles/0.png"), 0, 0));
    }

    [Theory]
    [MemberData(nameof(InspectCliTests.InvalidArchives), MemberType = typeof(InspectCliTests))]
    public async Task Export_map_uses_the_same_complete_mkf_validation_as_inspect(byte[] bytes, string error)
    {
        File.WriteAllBytes(Path.Combine(input, "Map.mkf"), bytes);
        var result = await Export();
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Contains("map.mkf", result.Stderr);
        Assert.Contains(error, result.Stderr);
        Assert.DoesNotContain(directory, result.Stderr);
        Assert.False(Directory.Exists(output));
    }

    [Theory]
    [InlineData("Map.mkf")]
    [InlineData("Gop.mkf")]
    [InlineData("Pat.mkf")]
    public async Task Export_map_requires_only_its_three_archives(string name)
    {
        File.Delete(Path.Combine(input, name));
        var result = await Export();
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Contains(name.ToLowerInvariant(), result.Stderr);
        Assert.Contains("missing", result.Stderr);
    }

    [CaseSensitiveFileSystemFact]
    public async Task Export_map_rejects_case_ambiguous_archives()
    {
        File.Copy(Path.Combine(input, "Map.mkf"), Path.Combine(input, "map.mkf"));
        var result = await Export();
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Contains("map.mkf", result.Stderr);
        Assert.Contains("ambiguous", result.Stderr);
    }

    [Fact]
    public async Task Export_map_bounds_aggregate_tile_pixels_including_aliased_frames()
    {
        byte[] frame = TransparentFrame(256, 256);
        var sprite = new byte[516 + frame.Length];
        for (int i = 0; i < 257; i++) BinaryPrimitives.WriteUInt16LittleEndian(sprite.AsSpan(i * 2), 258);
        BinaryPrimitives.WriteUInt16LittleEndian(sprite.AsSpan(514), (ushort)(sprite.Length / 2));
        frame.CopyTo(sprite, 516);
        WriteArchive("Gop.mkf", [], sprite);
        var result = await Export();
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Contains("gop.mkf", result.Stderr);
        Assert.Contains("limit", result.Stderr);
        Assert.False(Directory.Exists(output));
    }

    private static byte[] SolidFrame(ushort width, ushort height, byte color)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(width); writer.Write(height);
        for (int y = 0; y < height; y++) { writer.Write((byte)width); writer.Write(Enumerable.Repeat(color, (int)width).ToArray()); }
        if (stream.Length % 2 != 0) writer.Write((byte)0);
        return stream.ToArray();
    }
    private static byte[] TransparentFrame(ushort width, ushort height)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(width); writer.Write(height);
        int remaining = width * height;
        while (remaining > 0) { int run = Math.Min(remaining, Math.Min(127, (int)width)); writer.Write((byte)(128 + run)); remaining -= run; }
        if (stream.Length % 2 != 0) writer.Write((byte)0);
        return stream.ToArray();
    }
    private void WriteSprite(params byte[][] frames)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        int offset = (frames.Length + 1) * 2;
        writer.Write((ushort)(offset / 2));
        foreach (var frame in frames) { offset += frame.Length; writer.Write((ushort)(offset / 2)); }
        foreach (var frame in frames) writer.Write(frame);
        WriteArchive("Gop.mkf", [], stream.ToArray());
    }

    [Theory]
    [InlineData("nonempty")]
    [InlineData("input")]
    [InlineData("nested-input")]
    [InlineData("file")]
    public async Task Export_map_rejects_unsafe_output_without_changing_existing_files(string kind)
    {
        Directory.CreateDirectory(output);
        string sentinel = Path.Combine(output, "keep.txt");
        File.WriteAllText(sentinel, "keep me");
        string destination = kind switch
        {
            "input" => input,
            "nested-input" => Path.Combine(input, "new-output"),
            "file" => sentinel,
            _ => output
        };
        var before = Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes);
        var result = await Export(destination);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Contains("output", result.Stderr, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(directory, result.Stderr);
        Assert.Equal(before.Keys.Order(), Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Order());
        foreach (var (path, bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData("output-link")]
    [InlineData("output-ancestor")]
    [InlineData("input-link")]
    public async Task Export_map_rejects_directory_links_in_paths(string kind)
    {
        string target = kind == "output-link" ? Path.Combine(directory, "empty-target") : input;
        Directory.CreateDirectory(target);
        string link = Path.Combine(directory, "link");
        CreateDirectoryLink(link, target);
        try
        {
            var result = kind == "input-link"
                ? await CliProcess.Run(directory, "export-map", "--input", link, "--output", output,
                    "--map", "1", "--palette", "0", "--lighting", "day")
                : await Export(kind == "output-link" ? link : Path.Combine(link, "new-output"));
            Assert.Equal(1, result.ExitCode);
            Assert.Empty(result.Stdout);
            Assert.Contains("link", result.Stderr, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(directory, result.Stderr);
            Assert.False(File.Exists(Path.Combine(target, "map.json")));
            Assert.False(Directory.Exists(Path.Combine(input, "new-output")));
        }
        finally { Directory.Delete(link); }
    }

    private static void CreateDirectoryLink(string link, string target)
    {
        if (!OperatingSystem.IsWindows()) { Directory.CreateSymbolicLink(link, target); return; }
        var start = new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in new[] { "/c", "mklink", "/J", link, target }) start.ArgumentList.Add(arg);
        using var process = System.Diagnostics.Process.Start(start)!;
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd());
    }

    private (int Width, int Height, byte[] Rgba) ReadPng(string name)
    {
        using var reader = new BinaryReader(File.OpenRead(Path.Combine(output, name)));
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, reader.ReadBytes(8));
        using var compressed = new MemoryStream();
        int width = 0, height = 0;
        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(reader.ReadBytes(4));
            string type = System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4));
            byte[] bytes = reader.ReadBytes(length);
            reader.ReadBytes(4);
            if (type == "IHDR")
            {
                width = BinaryPrimitives.ReadInt32BigEndian(bytes);
                height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(4));
                Assert.Equal(new byte[] { 8, 6, 0, 0, 0 }, bytes[8..]);
            }
            if (type == "IDAT") compressed.Write(bytes);
        }
        compressed.Position = 0;
        using var zlib = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionMode.Decompress);
        var rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            Assert.Equal(0, zlib.ReadByte()); // Exported PNGs use filter 0.
            zlib.ReadExactly(rgba.AsSpan(y * width * 4, width * 4));
        }
        Assert.Equal(-1, zlib.ReadByte());
        return (width, height, rgba);
    }
    private static byte[] Pixel((int Width, int Height, byte[] Rgba) png, int x, int y) =>
        png.Rgba.AsSpan((y * png.Width + x) * 4, 4).ToArray();

    private void SetCell(int index, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(map.AsSpan(index * 4), value);
    private void WriteMap()
    {
        var encoder = new Yj2TestData(map.Length);
        foreach (byte value in map) encoder.Literal(value);
        WriteArchive("Map.mkf", [], encoder.End());
    }
    private void WriteArchive(string name, params byte[][] chunks)
    {
        using var writer = new BinaryWriter(File.Create(Path.Combine(input, name)));
        int offset = (chunks.Length + 1) * 4;
        writer.Write(offset);
        foreach (var chunk in chunks) { offset += chunk.Length; writer.Write(offset); }
        foreach (var chunk in chunks) writer.Write(chunk);
    }
    private JsonDocument ReadJson(string name) => JsonDocument.Parse(File.ReadAllText(Path.Combine(output, name)));
    private Task<(int ExitCode, string Stdout, string Stderr)> Export(string? destination = null, string lighting = "day") =>
        CliProcess.Run(directory, "export-map", "--input", input, "--output", destination ?? output,
            "--map", "1", "--palette", "0", "--lighting", lighting);
    public void Dispose() => Directory.Delete(directory, recursive: true);
}
