using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pal.TestFixtures;

// Artificial modern M1 data only: no extractor, MKF or copyrighted input.
public static class M1Bundle
{
    public static void Write(string root)
    {
        Directory.CreateDirectory(root);
        object Source(string archive, int chunk) => new { archive, chunk, encoding = "artificial fixture" };
        object Frame(string prefix, int index, int width = 2, int height = 2)
        {
            byte[] indices = width == 2 ? [0, 1, 0, 2] : [1];
            byte[] alpha = width == 2 ? [255, 255, 0, 255] : [255];
            string png = $"{prefix}/{index}.png";
            byte[] rgba = new byte[width * height * 4];
            for (int i = 0; i < indices.Length; i++)
            {
                rgba[i * 4] = (byte)(indices[i] * 40); rgba[i * 4 + 1] = 20;
                rgba[i * 4 + 2] = 80; rgba[i * 4 + 3] = alpha[i];
            }
            WritePng(Path.Combine(root, png), width, height, rgba);
            return new { index, sourceOffset = 4 + index * 12, width, height, png, indices, alpha };
        }
        WriteJson(root, "tiles.json", new { formatVersion = 1, mapId = 12, source = Source("gop.mkf", 12), frames = new[] { Frame("tiles", 0), Frame("tiles", 1, 1, 1) } });
        foreach (int id in new[] { 7, 9 })
            WriteJson(root, $"sprites/{id}.json", new { formatVersion = 1, spriteId = id, source = Source("mgo.mkf", id), decodedByteLength = 64, frameCount = 1, frames = new[] { Frame($"sprites/{id}", 0), Frame($"sprites/{id}", 2) } });
        WritePng(Path.Combine(root, "preview.png"), 2048, 2048, new byte[2048 * 2048 * 4]);
        WriteJson(root, "map.json", new
        {
            formatVersion = 1, mapId = 12, width = 64, height = 128, halfRows = 2,
            cellOrder = "[y,x,h]", decodedByteLength = 65536, source = Source("map.mkf", 12),
            coordinates = new { bitmapX = "x*32+h*16-16", bitmapY = "y*16+h*8-8", logicalHeight = "occlusion only", bitmapAnchor = "top-left" },
            preview = new { png = "preview.png", originX = 0, originY = 0, width = 2048, height = 2048, drawOrder = "layer,y,h,x", boundary = "bottom fallback", scope = "static tiles only" },
            cells = Enumerable.Range(0, 16384).Select(i =>
            {
                int y = i / 128, x = i / 2 % 64, h = i % 2;
                // Distinct non-origin coordinates expose x/y transposition and h swaps.
                // Raw DWORDs and their decoded fields are independently specified literals.
                (uint Raw, int Bottom, int? Top, int BottomHeight, int TopHeight, bool Blocked) cell = (y, x, h) switch
                {
                    (0, 0, 0) => (0x03022500u, 0, 1, 5, 3, true),
                    (1, 3, 0) => (0x07012401u, 1, 0, 4, 7, true),
                    (3, 1, 1) => (0x02020900u, 0, 1, 9, 2, false),
                    (5, 7, 1) => (0x00000c01u, 1, null, 12, 0, false),
                    _ => (0u, 0, null, 0, 0, false)
                };
                return new { y, x, h, raw = cell.Raw, bottomFrame = cell.Bottom, topFrame = cell.Top,
                    bottomHeight = cell.BottomHeight, topHeight = cell.TopHeight, blocked = cell.Blocked };
            })
        });
        WriteJson(root, "palette.json", new { formatVersion = 1, paletteId = 0, lighting = "night", selectedLighting = "day", colors = Enumerable.Range(0, 256).Select(i => new { r = (byte)(i * 40), g = 20, b = 80 }), source = new { archive = "pat.mkf", chunk = 0, componentConversion = "six-bit << 2" } });
        object Obj(int id, int sprite) => new
        {
            id, sVanishTime = -3, x = 65520, y = 48, sLayer = -2, wTriggerScript = 55, wAutoScript = 66,
            sState = sprite == 0 ? 2 : 0, wTriggerMode = 4, wSpriteNum = sprite, nSpriteFrames = 1,
            wDirection = 2, wCurrentFrameNum = 0, nScriptIdleFrame = 5, wSpritePtrOffset = 6,
            nSpriteFramesAuto = 7, wScriptIdleFrameCountAuto = 8,
            sprite = sprite == 0 ? null : $"sprites/{sprite}.json", initialFrameIndex = sprite == 0 ? (int?)null : 2, initiallyVisible = false
        };
        WriteJson(root, "scene.json", new
        {
            formatVersion = 1, sceneId = 2, mapId = 12, wScriptOnEnter = 33, wScriptOnTeleport = 44,
            wEventObjectIndex = 0, nextEventObjectIndex = 2,
            source = new { archive = "sss.mkf", chunk = 1, recordIndex = 1, recordByteLength = 8 },
            objectsSource = new { archive = "sss.mkf", chunk = 0, recordByteLength = 32, encoding = "raw little-endian EVENTOBJECT" },
            objects = new[] { Obj(1, 9), Obj(2, 0) },
            defaultParty = new[] { new { roleId = 0, wSpriteNum = 7, sprite = "sprites/7.json", source = new { archive = "data.mkf", chunk = 3, spriteByteOffset = 24 } } },
            spriteIds = new[] { 7, 9 }, positioning = new { reference = "artificial static data", objects = new { visibility = "sState>0" }, party = new { placement = "not placed" } }
        });
        Rehash(root);
    }

    public static void Rehash(string root)
    {
        var artifacts = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Where(p => Path.GetFileName(p) != "manifest.json")
            .Select(p => new { path = Path.GetRelativePath(root, p).Replace('\\', '/'), byteLength = new FileInfo(p).Length, sha256 = Hash(File.ReadAllBytes(p)) })
            .OrderBy(a => a.path, StringComparer.Ordinal).ToArray();
        string identity = string.Concat(artifacts.Select(a => FormattableString.Invariant($"{a.path}\t{a.byteLength}\t{a.sha256}\n")));
        WriteJson(root, "manifest.json", new
        {
            formatVersion = 1, command = "extract-scene", parameters = new { sceneId = 2, mapId = 12, paletteId = 0, lighting = "night" },
            sources = new[] { "data.mkf", "gop.mkf", "map.mkf", "mgo.mkf", "pat.mkf", "sss.mkf" }.Select(name => new { name, byteLength = 128, sha256 = new string('a', 64) }),
            resources = new { scene = "scene.json", map = "map.json", tiles = "tiles.json", palette = "palette.json", preview = "preview.png", sprites = new[] { new { id = 7, path = "sprites/7.json" }, new { id = 9, path = "sprites/9.json" } } },
            artifacts, bundleSha256 = Hash(Encoding.UTF8.GetBytes(identity))
        });
    }
    public static void Mutate(string root, string path, Action<JsonNode> mutation, bool rehash = true)
    {
        var node = JsonNode.Parse(File.ReadAllText(Path.Combine(root, path)))!;
        mutation(node);
        File.WriteAllText(Path.Combine(root, path), node.ToJsonString());
        if (rehash) Rehash(root);
    }
    public static void WriteJson(string root, string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, path))!);
        File.WriteAllText(Path.Combine(root, path), JsonSerializer.Serialize(value));
    }
    public static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    public static void WritePng(string path, int width, int height, byte[] rgba)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        stream.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; header[9] = 6;
        Chunk("IHDR", header);
        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Fastest, true))
            for (int y = 0; y < height; y++) { z.WriteByte(0); z.Write(rgba, y * width * 4, width * 4); }
        Chunk("IDAT", compressed.ToArray()); Chunk("IEND", []);
        void Chunk(string type, byte[] data)
        {
            byte[] n = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(n, data.Length); stream.Write(n);
            byte[] bytes = Encoding.ASCII.GetBytes(type).Concat(data).ToArray(); stream.Write(bytes);
            uint crc = uint.MaxValue;
            foreach (byte b in bytes) { crc ^= b; for (int k = 0; k < 8; k++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0); }
            BinaryPrimitives.WriteUInt32BigEndian(n, ~crc); stream.Write(n);
        }
    }
}
