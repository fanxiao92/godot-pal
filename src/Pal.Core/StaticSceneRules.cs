namespace Pal.Core;

// Pure static subset of SDLPal scene.c/map.c. Metadata is snapshotted; neither query advances state.
public sealed class StaticSceneRules
{
    private const int AnchorX = 160, AnchorY = 112;
    private readonly SceneMap map;
    public int MapId => map.MapId;
    public IReadOnlyList<SceneObject> Objects { get; }

    public StaticSceneRules(SceneMap map, IEnumerable<SceneObject>? objects = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        this.map = map;
        Objects = Array.AsReadOnly(objects?.ToArray() ?? []);
        if (Objects.Any(o => o.Id == 0) || Objects.Select(o => o.Id).Distinct().Count() != Objects.Count)
            throw new ArgumentException("Objects require unique global, one-based WORD IDs.", nameof(objects));
        if (Objects.Any(o => o.Sprite is not null && o.Sprite.SpriteId != o.SpriteNumber))
            throw new ArgumentException("An object's sprite identity must match its raw sprite number.", nameof(objects));
    }

    // PAL_Search: sample priority, then original object order; not distance or visibility.
    public SceneSearchMatch? FindSearchTarget(StaticLeader leader)
    {
        if (leader.Direction is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(leader));
        int x = leader.X, y = leader.Y;
        int ox = leader.Direction is 2 or 3 ? 16 : -16;
        int oy = leader.Direction is 0 or 3 ? 8 : -8;
        var samples = new (int X, int Y)[13];
        samples[0] = (x, y);
        for (int i = 0; i < 4; i++)
        {
            samples[i * 3 + 1] = (Pack(x + ox), Pack(y + oy));
            samples[i * 3 + 2] = (Pack(x), Pack(y + oy * 2));
            samples[i * 3 + 3] = (Pack(x + ox * 2), Pack(y));
            x += ox; y += oy;
        }
        for (int i = 0; i < samples.Length; i++)
        {
            var point = samples[i];
            foreach (var obj in Objects)
                if (obj.State > 0 && obj.TriggerMode < 4 && obj.TriggerMode * 6 - 4 > i &&
                    point.X / 32 == obj.X / 32 && point.Y / 16 == obj.Y / 16 &&
                    (point.X % 32 != 0) == (obj.X % 32 != 0))
                    return new(obj.Id, i);
        }
        return null;
    }

    internal StaticSceneRules WithObjects(IEnumerable<SceneObject> objects) => new(map, objects);

    // Returns the complete draw order, including background and repeated cover tiles.
    // Missing required frames or more than 2048 sprite/cover entries fail, never substitute/truncate.
    public StaticScenePlan BuildPlan(StaticLeader leader) => BuildPlan(leader,
        unchecked((ushort)(leader.Direction * (leader.WalkFramesWord == 0 ? 3 : leader.WalkFramesWord))));

    public StaticScenePlan BuildPlan(StaticLeader leader, int leaderFrameIndex)
    {
        if (leader.Direction is < 0 or > 3 || leader.StepPhase is < 0 or > 3)
            throw new ArgumentOutOfRangeException(nameof(leader), "Direction and step phase must be 0..3.");
        int cameraX = unchecked((short)(leader.X - AnchorX));
        int cameraY = unchecked((short)(leader.Y - AnchorY));
        int frameIndex = leaderFrameIndex;
        var frame = leader.Sprite.Frames.TryGetValue(frameIndex, out var found) ? found
            : throw new InvalidOperationException($"Leader sprite {leader.Sprite.SpriteId} is missing frame {frameIndex}.");
        var sprites = new List<Pending>();
        AddSprite(sprites, new(SceneDrawSource.Leader, leader.Sprite.SpriteId, frame,
            Pack(AnchorX - frame.Width / 2), Pack(AnchorY + leader.Layer + 10), leader.Layer + 6));
        AddCover(sprites, sprites[^1], cameraX, cameraY);
        foreach (var obj in Objects)
        {
            if (obj.State <= 0 || obj.VanishTime > 0 || obj.Sprite is null) continue;
            int current = obj.CurrentFrame;
            if (obj.SpriteFrames == 3)
            {
                if (current == 2) current = 0;
                if (current == 3) current = 2;
            }
            int index = obj.Direction * obj.SpriteFrames + current;
            if (!obj.Sprite.Frames.TryGetValue(index, out var objectFrame))
                throw new InvalidOperationException($"Object {obj.Id} sprite {obj.Sprite.SpriteId} is missing frame {index}.");
            int x = Pack(obj.X) - cameraX - objectFrame.Width / 2;
            int y = Pack(obj.Y) - cameraY + obj.Layer * 8 + 9;
            int vy = y - objectFrame.Height - obj.Layer * 8 + 2;
            if (x >= 320 || x < -objectFrame.Width || vy >= 200 || vy < -objectFrame.Height) continue;
            AddSprite(sprites, new(SceneDrawSource.Object, obj.Sprite.SpriteId, objectFrame,
                Pack(x), Pack(y), obj.Layer * 8 + 2, obj.Id));
            AddCover(sprites, sprites[^1], cameraX, cameraY);
        }
        var commands = new List<SceneDrawCommand>();
        for (int layer = 0; layer < 2; layer++)
        for (int y = cameraY / 16 - 1; y < (cameraY + 200) / 16 + 2; y++)
        for (int h = 0; h < 2; h++)
        for (int x = cameraX / 32 - 1; x < (cameraX + 320) / 32 + 2; x++)
        {
            var tile = Tile(x, y, h, layer);
            if (tile is null && layer == 0) tile = Tile(0, 0, 0, 0);
            if (tile is null) continue;
            int left = Pack(x * 32 + h * 16 - 16 - cameraX);
            int top = Pack(y * 16 + h * 8 - 8 - cameraY);
            commands.Add(new(SceneDrawSource.Background, map.MapId, tile.Index, left, top, top, layer));
        }
        commands.AddRange(sprites.OrderBy(s => s.SortY).Select(s => new SceneDrawCommand(s.Source,
            s.ResourceId, s.Frame.Index, s.Left, Pack(s.SortY - s.Frame.Height - s.Layer), s.SortY, s.Layer, s.ObjectId)));
        return new(cameraX, cameraY, frameIndex, leader.StepPhase, commands.AsReadOnly());
    }

    // Keep the reference's five-candidate scan, C division/remainder, BYTE queries and duplicates.
    private void AddCover(List<Pending> sprites, Pending actor, int cameraX, int cameraY)
    {
        int sx = cameraX + actor.Left - actor.Layer / 2;
        int sy = cameraY + actor.SortY - actor.Layer;
        int sh = sx % 32 != 0 ? 1 : 0;
        int firstX = (sx - actor.Frame.Width / 2) / 32;
        int dx = 0, dy = 0, dh = 0;
        for (int y = (sy - actor.Frame.Height - 15) / 16; y <= sy / 16; y++)
        for (int x = firstX; x <= (sx + actor.Frame.Width / 2) / 32; x++)
        for (int i = x == firstX ? 0 : 3; i < 5; i++)
        {
            switch (i)
            {
                case 0: dx = x; dy = y; dh = sh; break;
                case 1: dx = x - 1; break;
                case 2: dx = sh != 0 ? x : x - 1; dy = sh != 0 ? y + 1 : y; dh = 1 - sh; break;
                case 3: dx = x + 1; dy = y; dh = sh; break;
                case 4: dx = sh != 0 ? x + 1 : x; dy = sh != 0 ? y + 1 : y; dh = 1 - sh; break;
            }
            for (int layer = 0; layer < 2; layer++)
            {
                var tile = Tile(dx, dy, dh, layer);
                int height = TileHeight(dx, dy, dh, layer);
                if (tile is null || height <= 0 || (dy + height) * 16 + dh * 8 < sy) continue;
                AddSprite(sprites, new(SceneDrawSource.Cover, map.MapId, tile,
                    Pack(dx * 32 + dh * 16 - 16 - cameraX),
                    Pack(dy * 16 + dh * 8 + 7 + layer + height * 8 - cameraY), height * 8 + layer));
            }
        }
    }

    private int TileHeight(int x, int y, int h, int layer)
    {
        x = unchecked((byte)x); y = unchecked((byte)y); h = unchecked((byte)h);
        return x >= 64 || y >= 128 || h > 1 ? 0
            : (int)((map.RawCells[(y * 64 + x) * 2 + h] >> (layer * 16 + 8)) & 15);
    }

    private SceneFrame? Tile(int x, int y, int h, int layer)
    {
        x = unchecked((byte)x); y = unchecked((byte)y); h = unchecked((byte)h);
        if (x >= 64 || y >= 128 || h > 1) return null;
        uint cell = map.RawCells[(y * 64 + x) * 2 + h] >> (layer * 16);
        int index = (int)((cell & 0xff) | ((cell >> 4) & 0x100)) - layer;
        if (index < 0) return null;
        return map.TileFrames.TryGetValue(index, out var frame) ? frame
            : throw new InvalidOperationException($"Map {map.MapId} is missing tile frame {index}.");
    }

    private static void AddSprite(List<Pending> sprites, Pending sprite)
    {
        if (sprites.Count >= 2048)
            throw new InvalidOperationException("The scene sprite/cover list exceeds 2048 entries.");
        sprites.Add(sprite);
    }

    private static int Pack(int value) => unchecked((short)value);
    private sealed record Pending(SceneDrawSource Source, int ResourceId, SceneFrame Frame,
        int Left, int SortY, int Layer, int ObjectId = 0);

    // Targets pack through PAL_XY/signed SHORT; map queries convert to BYTE.
    // selfObjectId is the global one-based object ID, not its index in this scene's list.
    public bool IsBlocked(int worldX, int worldY, bool checkRange = true, bool checkObjects = true, ushort selfObjectId = 0)
    {
        int px = unchecked((short)worldX), py = unchecked((short)worldY);
        int x = px / 32, y = py / 16, h = 0;
        int blockX = AnchorX / 32, blockY = AnchorY / 16;
        // scene.c intentionally compares y with 2048 - blockY - 1 (not the map row count).
        if (checkRange && (x < blockX || x >= 2048 / 32 - blockX - 1 || y < blockY || y >= 2048 - blockY - 1))
            return true;

        int xr = px % 32, yr = py % 16;
        if (xr + yr * 2 >= 16)
        {
            if (xr + yr * 2 >= 48) { x++; y++; }
            else if (32 - xr + yr * 2 < 16) x++;
            else if (32 - xr + yr * 2 < 48) h = 1;
            else y++;
        }
        x = unchecked((byte)x);
        y = unchecked((byte)y);
        if (x >= 64 || y >= 128 || (map.RawCells[(y * 64 + x) * 2 + h] & 0x2000) != 0)
            return true;
        return checkObjects && Objects.Any(o => o.Id != selfObjectId && o.State >= 2 &&
            Math.Abs(o.X - px) + Math.Abs(o.Y - py) * 2 < 16);
    }
}
