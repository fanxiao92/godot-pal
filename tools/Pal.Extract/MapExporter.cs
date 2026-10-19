using System.Buffers.Binary;
using System.Text.Json;

namespace Pal.Extract;

internal static class MapExporter
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    internal static void Export(string input, string output, int mapId, int paletteId, string lighting)
    {
        string[] requiredNames = ["map.mkf", "gop.mkf", "pat.mkf"];
        output = ExportPaths.Validate(input, output, requiredNames);
        var archives = MkfArchive.OpenRequired(input, requiredNames, 65536);
        try
        {
            var raw = DecodeResource("map.mkf", mapId,
                () => Yj2Decoder.Decode(archives[0].ReadChunk(mapId, 8 * 1024 * 1024), 65536));
            if (raw.Length != 65536)
                throw new InvalidDataException($"map.mkf: chunk {mapId}: map dimensions require exactly 65536 decoded bytes.");
            var cells = new MapCell[16384];
            for (int i = 0; i < cells.Length; i++)
            {
                uint d = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(i * 4));
                int top = (int)(((d >> 16) & 255) | ((d >> 20) & 256)) - 1;
                cells[i] = new MapCell(i / 128, i / 2 % 64, i % 2, d,
                    (int)((d & 255) | ((d >> 4) & 256)), top < 0 ? null : top,
                    (int)((d >> 8) & 15), (int)((d >> 24) & 15), (d & 0x2000) != 0);
            }
            var palette = DecodeResource("pat.mkf", paletteId,
                () => PaletteDecoder.Decode(archives[2].ReadChunk(paletteId, 1536), lighting == "night"));
            var sprite = archives[1].ReadChunk(mapId, 8 * 1024 * 1024);
            var frames = DecodeResource("gop.mkf", mapId, () => DecodeTiles(sprite, cells));
            var preview = RenderPreview(cells, frames);
            Directory.CreateDirectory(output);
            Directory.CreateDirectory(Path.Combine(output, "tiles"));
            foreach (var frame in frames.Values)
                File.WriteAllBytes(Path.Combine(output, "tiles", $"{frame.FrameIndex}.png"), PngWriter.Encode(frame, palette.Colors));
            File.WriteAllBytes(Path.Combine(output, "preview.png"), PngWriter.Encode(preview, palette.Colors));
            WriteJson(output, "tiles.json", new
            {
                formatVersion = 1,
                mapId,
                source = new { archive = "gop.mkf", chunk = mapId, encoding = "raw sprite/RLE" },
                frames = frames.Values.Select(frame => new
                {
                    index = frame.FrameIndex,
                    frame.SourceOffset,
                    frame.Width,
                    frame.Height,
                    png = $"tiles/{frame.FrameIndex}.png",
                    indices = frame.Pixels,
                    frame.Alpha
                })
            });
            WriteJson(output, "map.json", new
            {
                formatVersion = 1,
                mapId,
                width = 64,
                height = 128,
                halfRows = 2,
                cellOrder = "[y,x,h]",
                decodedByteLength = raw.Length,
                source = new { archive = "map.mkf", chunk = mapId, encoding = "Windows YJ2" },
                coordinates = new
                {
                    bitmapX = "x*32+h*16-16",
                    bitmapY = "y*16+h*8-8",
                    logicalHeight = "occlusion only; does not shift the map bitmap",
                    bitmapAnchor = "top-left"
                },
                preview = new
                {
                    png = "preview.png",
                    originX = 0,
                    originY = 0,
                    width = 2048,
                    height = 2048,
                    drawOrder = "layer (bottom then top), y, h, x; map.c PAL_MapBlitToSurface",
                    boundary = "bottom uses cell [0,0,0] outside grid; top absent; clipped to viewport",
                    scope = "static tiles only; no sprites, scripts, waves or palette animation"
                },
                cells
            });
            WriteJson(output, "palette.json", new
            {
                formatVersion = 1,
                paletteId,
                lighting,
                selectedLighting = palette.UsesNightColors ? "night" : "day",
                colors = palette.Colors,
                source = new { archive = "pat.mkf", chunk = paletteId, componentConversion = "six-bit << 2" }
            });
        }
        finally { foreach (var archive in archives) archive.Dispose(); }
    }

    private static SortedDictionary<int, IndexedImage> DecodeTiles(byte[] sprite, MapCell[] cells)
    {
        int count = SpriteDecoder.GetFrameCount(sprite);
        if (count > 4096) throw new InvalidDataException("Tile frame count exceeds the resource limit.");
        var indices = Enumerable.Range(0, count).Concat(cells.Select(cell => cell.BottomFrame))
            .Concat(cells.Where(cell => cell.TopFrame.HasValue).Select(cell => cell.TopFrame!.Value)).Distinct().Order();
        var frames = new SortedDictionary<int, IndexedImage>();
        int remainingPixels = IndexedImage.MaxPixels;
        foreach (int index in indices)
        {
            try
            {
                var frame = SpriteDecoder.DecodeFrame(sprite, index, remainingPixels);
                remainingPixels -= frame.Pixels.Length;
                frames.Add(index, frame);
            }
            catch (InvalidDataException exception)
            { throw new InvalidDataException($"tile frame {index}: {exception.Message}", exception); }
        }
        return frames;
    }

    private static IndexedImage RenderPreview(MapCell[] cells, SortedDictionary<int, IndexedImage> frames)
    {
        const int width = 2048, height = 2048;
        long work = cells.Sum(cell => (long)frames[cell.BottomFrame].Pixels.Length
            + (cell.TopFrame is int top ? frames[top].Pixels.Length : 0));
        int boundaryCells = 131 * 2 * 67 - cells.Length;
        work += (long)boundaryCells * frames[cells[0].BottomFrame].Pixels.Length;
        if (work > 128L * 1024 * 1024)
            throw new InvalidDataException("map preview: tile drawing work exceeds the resource limit.");
        var pixels = new byte[width * height];
        var alpha = new byte[pixels.Length];
        // map.c viewport (0,0,2048,2048): includes its out-of-grid bottom fallback.
        for (int layer = 0; layer < 2; layer++)
            for (int y = -1; y < 130; y++)
                for (int h = 0; h < 2; h++)
                    for (int x = -1; x < 66; x++)
                    {
                        bool inside = x >= 0 && x < 64 && y >= 0 && y < 128;
                        MapCell? cell = inside ? cells[(y * 64 + x) * 2 + h] : layer == 0 ? cells[0] : null;
                        int? index = layer == 0 ? cell?.BottomFrame : cell?.TopFrame;
                        if (index is null) continue;
                        var frame = frames[index.Value];
                        int left = x * 32 + h * 16 - 16, top = y * 16 + h * 8 - 8;
                        for (int row = Math.Max(0, -top); row < Math.Min(frame.Height, height - top); row++)
                            for (int column = Math.Max(0, -left); column < Math.Min(frame.Width, width - left); column++)
                            {
                                int source = row * frame.Width + column;
                                if (frame.Alpha[source] == 0) continue;
                                int target = (top + row) * width + left + column;
                                pixels[target] = frame.Pixels[source];
                                alpha[target] = frame.Alpha[source];
                            }
                    }
        return new IndexedImage(width, height, pixels, alpha);
    }

    private static T DecodeResource<T>(string name, int chunk, Func<T> decode)
    {
        try { return decode(); }
        catch (InvalidDataException exception) { throw new InvalidDataException($"{name}: chunk {chunk}: {exception.Message}", exception); }
    }

    private static void WriteJson(string output, string name, object value) =>
        File.WriteAllText(Path.Combine(output, name), JsonSerializer.Serialize(value, JsonOptions) + "\n");
}

internal sealed record MapCell(int Y, int X, int H, uint Raw, int BottomFrame, int? TopFrame,
    int BottomHeight, int TopHeight, bool Blocked);
