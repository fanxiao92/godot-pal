namespace Pal.Core;

public sealed record SceneFrame(int Index, int Width, int Height)
{
    internal static IReadOnlyDictionary<int, SceneFrame> IndexFrames(IEnumerable<SceneFrame> frames)
    {
        var indexed = frames.ToDictionary(f => f.Index);
        if (indexed.Values.Any(f => f.Index < 0 || f.Width is <= 0 or > 65535 || f.Height is <= 0 or > 65535))
            throw new ArgumentException("Frame indices must be nonnegative and dimensions must fit nonzero WORDs.", nameof(frames));
        return new System.Collections.ObjectModel.ReadOnlyDictionary<int, SceneFrame>(indexed);
    }
}

public sealed class SceneMap
{
    public int MapId { get; }
    public IReadOnlyList<uint> RawCells { get; }

    public IReadOnlyDictionary<int, SceneFrame> TileFrames { get; }

    public SceneMap(int mapId, IEnumerable<uint> rawCells, IEnumerable<SceneFrame>? tileFrames = null)
    {
        var cells = rawCells.ToArray();
        if (cells.Length != 64 * 128 * 2)
            throw new ArgumentException("A scene map requires 64 * 128 * 2 raw cells.", nameof(rawCells));
        MapId = mapId;
        RawCells = Array.AsReadOnly(cells);
        TileFrames = SceneFrame.IndexFrames(tileFrames ?? []);
    }
}

public sealed class SceneSprite
{
    public int SpriteId { get; }
    public IReadOnlyDictionary<int, SceneFrame> Frames { get; }

    public SceneSprite(int spriteId, IEnumerable<SceneFrame> frames)
    {
        SpriteId = spriteId;
        Frames = SceneFrame.IndexFrames(frames);
    }
}

// Coordinates and frame parameters retain the event object's raw WORDs; signed fields stay signed.
public sealed record SceneObject(ushort Id, ushort X, ushort Y, short State, short VanishTime,
    short Layer, ushort SpriteNumber, ushort SpriteFrames, ushort Direction, ushort CurrentFrame,
    SceneSprite? Sprite = null)
{
    // Raw event-object WORDs. Non-positional additions preserve existing construction/deconstruction.
    public ushort TriggerMode { get; init; }
    public ushort TriggerEntry { get; init; }
}

public sealed record SceneSearchMatch(ushort ObjectId, int SampleIndex);

public sealed record StaticLeader(short X, short Y, int Direction, ushort Layer,
    int StepPhase, ushort WalkFramesWord, SceneSprite Sprite);

public enum SceneDrawSource { Background, Leader, Object, Cover }

// Background/Cover ResourceId identifies the map tileset; Leader/Object identifies the sprite.
public sealed record SceneDrawCommand(SceneDrawSource Source, int ResourceId, int FrameIndex,
    int Left, int Top, int SortY, int Layer, int ObjectId = 0);

public sealed record StaticScenePlan(int CameraX, int CameraY, int LeaderFrameIndex,
    int StepPhase, IReadOnlyList<SceneDrawCommand> Commands);
