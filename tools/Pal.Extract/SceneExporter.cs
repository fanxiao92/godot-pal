using System.Buffers.Binary;
using static Pal.Extract.ExportSupport;

namespace Pal.Extract;

internal static class SceneExporter
{
    internal static void Export(string input, string output, int sceneId, int paletteId, string lighting)
    {
        string[] requiredNames = ["sss.mkf", "data.mkf", "mgo.mkf", "pat.mkf"];
        output = ExportPaths.Validate(input, output, requiredNames);
        var archives = MkfArchive.OpenRequired(input, requiredNames, 65536);
        try
        {
            var objects = archives[0].ReadChunk(0, 2 * 1024 * 1024);
            var scenes = archives[0].ReadChunk(1, 65536 * 8);
            if (objects.Length % 32 != 0)
                throw new InvalidDataException("sss.mkf: chunk 0: truncated 32-byte EVENTOBJECT record.");
            if (scenes.Length % 8 != 0)
                throw new InvalidDataException("sss.mkf: chunk 1: truncated 8-byte SCENE record.");
            if (sceneId >= scenes.Length / 8)
                throw new InvalidDataException($"sss.mkf: chunk 1: scene {sceneId} or its next object boundary is outside the table.");
            int offset = (sceneId - 1) * 8;
            int start = Word(scenes, offset + 6), end = Word(scenes, offset + 14);
            if (start > end || end > objects.Length / 32)
                throw new InvalidDataException($"sss.mkf: chunk 1: scene {sceneId} object range is outside chunk 0 or reversed.");
            var records = new List<SceneObject>();
            for (int index = start; index < end; index++)
            {
                int p = index * 32;
                records.Add(new SceneObject(index + 1, Signed(objects, p), Word(objects, p + 2), Word(objects, p + 4),
                    Signed(objects, p + 6), Word(objects, p + 8), Word(objects, p + 10), Signed(objects, p + 12),
                    Word(objects, p + 14), Word(objects, p + 16), Word(objects, p + 18), Word(objects, p + 20),
                    Word(objects, p + 22), Word(objects, p + 24), Word(objects, p + 26), Word(objects, p + 28), Word(objects, p + 30)));
            }
            // global.h PLAYERROLES: 75 arrays of six WORDs; third array is rgwSpriteNum.
            var roles = archives[1].ReadChunk(3, 900);
            if (roles.Length != 900)
                throw new InvalidDataException("data.mkf: chunk 3: PLAYERROLES requires exactly 900 bytes.");
            int roleSprite = Word(roles, 24);
            var palette = DecodeResource("pat.mkf", paletteId,
                () => PaletteDecoder.Decode(archives[3].ReadChunk(paletteId, 1536), lighting == "night"));
            var spriteIds = records.Select(record => (int)record.wSpriteNum).Append(roleSprite)
                .Where(id => id != 0).Distinct().Order().ToArray();
            var sprites = new List<SceneSprite>();
            int remainingPixels = IndexedImage.MaxPixels, remainingBytes = 64 * 1024 * 1024;
            foreach (int id in spriteIds)
            {
                sprites.Add(DecodeResource("mgo.mkf", id, () =>
                {
                    var raw = Yj2Decoder.Decode(archives[2].ReadChunk(id, 8 * 1024 * 1024), Math.Min(remainingBytes, 8 * 1024 * 1024));
                    remainingBytes -= raw.Length;
                    int count = SpriteDecoder.GetFrameCount(raw);
                    if (count is < 1 or > 4096)
                        throw new InvalidDataException("Sprite frame count is empty or exceeds the resource limit.");
                    var selected = records.Where(record => record.wSpriteNum == id).Select(record => record.InitialFrameIndex!.Value);
                    var indices = Enumerable.Range(0, count).Concat(selected).Distinct().Order();
                    var frames = new List<IndexedImage>();
                    foreach (int index in indices)
                    {
                        var frame = DecodeResource("frame", index, () => SpriteDecoder.DecodeFrame(raw, index, remainingPixels));
                        remainingPixels -= frame.Pixels.Length;
                        frames.Add(frame);
                    }
                    return new SceneSprite(id, raw.Length, count, frames);
                }));
            }
            Directory.CreateDirectory(output);
            foreach (var sprite in sprites)
            {
                Directory.CreateDirectory(Path.Combine(output, "sprites", $"{sprite.Id}"));
                foreach (var frame in sprite.Frames)
                    File.WriteAllBytes(Path.Combine(output, "sprites", $"{sprite.Id}", $"{frame.FrameIndex}.png"),
                        PngWriter.Encode(frame, palette.Colors));
                WriteJson(output, $"sprites/{sprite.Id}.json", new
                {
                    formatVersion = 1,
                    spriteId = sprite.Id,
                    source = new { archive = "mgo.mkf", chunk = sprite.Id, encoding = "Windows YJ2" },
                    decodedByteLength = sprite.DecodedByteLength,
                    frameCount = sprite.FrameCount,
                    frames = sprite.Frames.Select(frame => new
                    {
                        index = frame.FrameIndex, frame.SourceOffset, frame.Width, frame.Height,
                        png = $"sprites/{sprite.Id}/{frame.FrameIndex}.png", indices = frame.Pixels, frame.Alpha
                    })
                });
            }
            WriteJson(output, "scene.json", new
            {
                formatVersion = 1,
                sceneId,
                mapId = Word(scenes, offset),
                wScriptOnEnter = Word(scenes, offset + 2),
                wScriptOnTeleport = Word(scenes, offset + 4),
                wEventObjectIndex = start,
                nextEventObjectIndex = end,
                source = new { archive = "sss.mkf", chunk = 1, recordIndex = sceneId - 1, recordByteLength = 8 },
                objectsSource = new { archive = "sss.mkf", chunk = 0, recordByteLength = 32, encoding = "raw little-endian EVENTOBJECT" },
                objects = records,
                defaultParty = new[] { new { roleId = 0, wSpriteNum = roleSprite, sprite = SpriteReference(roleSprite),
                    source = new { archive = "data.mkf", chunk = 3, spriteByteOffset = 24 } } },
                spriteIds,
                positioning = new
                {
                    reference = "scene.c PAL_SceneDrawSprites; integer width/2; world coordinates before viewport subtraction",
                    objects = new
                    {
                        coordinates = "raw x/y are WORD; drawing interprets each as signed SHORT",
                        bitmapLeft = "short(x)-width/2", bitmapTop = "short(y)+7-height",
                        sortY = "short(y)+sLayer*8+9", drawLayer = "sLayer*8+2",
                        frame = "direction*nSpriteFrames+current; when nSpriteFrames==3, current 2 maps to 0 and 3 maps to 2",
                        visibility = "sState>0 && sVanishTime<=0 && wSpriteNum!=0; no script changes applied",
                        blocker = "sState>=2; original state only, no collision evaluation"
                    },
                    party = new
                    {
                        bitmapLeft = "x-width/2", bitmapTop = "y+4-height",
                        sortY = "y+layer+10", drawLayer = "layer+6",
                        placement = "not placed in the selected scene; only the default role sprite dependency"
                    }
                }
            });
            WritePalette(output, paletteId, lighting, palette);
        }
        finally { foreach (var archive in archives) archive.Dispose(); }
    }

    private static ushort Word(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset));
    private static short Signed(byte[] bytes, int offset) => BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(offset));
    internal static string? SpriteReference(int id) => id == 0 ? null : $"sprites/{id}.json";
}

internal sealed record SceneSprite(int Id, int DecodedByteLength, int FrameCount, List<IndexedImage> Frames);

internal sealed record SceneObject(int Id, short sVanishTime, ushort x, ushort y, short sLayer,
    ushort wTriggerScript, ushort wAutoScript, short sState, ushort wTriggerMode, ushort wSpriteNum,
    ushort nSpriteFrames, ushort wDirection, ushort wCurrentFrameNum, ushort nScriptIdleFrame,
    ushort wSpritePtrOffset, ushort nSpriteFramesAuto, ushort wScriptIdleFrameCountAuto)
{
    public string? Sprite => SceneExporter.SpriteReference(wSpriteNum);
    public int? InitialFrameIndex => wSpriteNum == 0 ? null : wDirection * nSpriteFrames +
        (nSpriteFrames == 3 ? wCurrentFrameNum switch { 2 => 0, 3 => 2, _ => wCurrentFrameNum } : wCurrentFrameNum);
    public bool InitiallyVisible => wSpriteNum != 0 && sState > 0 && sVanishTime <= 0;
}
