namespace Pal.Core;

/// <summary>Pre-battle exploration boundary for the authored scene2/map12/owner28 context.</summary>
public sealed class FirstMandatoryBattleSession
{
    public const string UnsupportedTriggerDiagnostic =
        "First mandatory battle trigger @09C3 is unavailable: the real resource closure was not verified; no side effects were applied.";

    private readonly ExplorationSession exploration;
    private readonly ExplorationInput input;
    private readonly FirstMandatoryBattleClosureReceipt? closure;
    private bool searchPressed;

    public FirstMandatoryBattleSessionState State { get; }
    /// <summary>The static rules this session was bound to (host world wiring reuses them).</summary>
    public StaticSceneRules Rules { get; }
    /// <summary>The @09C3 entry opened by a verified real closure at the committed search update.
    /// While set, the opened trigger owns further advancement; the host consumes it.</summary>
    public FirstMandatoryBattleTriggerRequest? PendingTrigger { get; private set; }
    public ExplorationState Current => exploration.Current;
    public IReadOnlyList<SceneObject> Objects => exploration.Objects;

    public FirstMandatoryBattleSession(FirstMandatoryBattleProfile profile, int sceneId, StaticSceneRules staticRules, StaticLeader staticLeader,
        FirstMandatoryBattleClosureReceipt? closureReceipt = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(staticRules);
        ArgumentNullException.ThrowIfNull(staticLeader);
        if (closureReceipt is not null && !StringComparer.Ordinal.Equals(closureReceipt.ContextSha256, profile.ContextSha256))
            throw new InvalidDataException("First mandatory battle closure receipt belongs to a different battle context.");
        closure = closureReceipt;
        State = profile.CreateSessionState();
        if (sceneId != 2 || State.World is not { SceneId: 2, MapId: 12 } || staticRules.MapId != 12
            || staticRules.Objects.All(item => item.Id != 28))
            throw new InvalidDataException("First mandatory battle requires scene2/map12 static rules containing owner28.");

        var snapshots = State.World.EventObjects.ToDictionary(item => item.OwnerId);
        var objects = staticRules.Objects.Select(item => snapshots.TryGetValue(item.Id, out BattleEventObjectState? snapshot)
            ? item with
            {
                X = checked((ushort)snapshot.X), Y = checked((ushort)snapshot.Y), Direction = checked((ushort)snapshot.Direction),
                State = checked((short)snapshot.State), VanishTime = checked((short)snapshot.VanishTime),
                TriggerMode = checked((ushort)snapshot.TriggerMode), TriggerEntry = checked((ushort)snapshot.TriggerEntry),
                SpriteFrames = checked((ushort)snapshot.SpriteFrames), CurrentFrame = checked((ushort)snapshot.CurrentFrame),
                Layer = checked((short)snapshot.Layer)
            }
            : item).ToArray();
        StaticSceneRules rules = staticRules.WithObjects(objects);
        BattleLeaderState authored = State.World.Leader;
        var leader = staticLeader with
        {
            X = checked((short)authored.X), Y = checked((short)authored.Y), Direction = Direction(authored.Direction),
            Layer = checked((ushort)authored.Layer), StepPhase = authored.Phase
        };
        SceneSearchMatch? hit = rules.FindSearchTarget(leader);
        if (hit is null || hit.ObjectId != 28 || hit.SampleIndex != State.World.SearchProof.SampleIndex || rules.IsBlocked(leader.X, leader.Y))
            throw new InvalidDataException("First mandatory battle start/search identity does not match owner28 collision proof.");
        Rules = rules;
        exploration = new ExplorationSession(rules, leader);
        input = new ExplorationInput(exploration);
    }

    public void Press(ExplorationKey key, bool repeat = false) => input.Press(key, repeat);
    public void Release(ExplorationKey key) => input.Release(key);
    public void PressSearch() => searchPressed = true;

    public bool Update(uint ticksMilliseconds)
    {
        if (PendingTrigger is not null) return false;
        if (!input.IsUpdateDue(ticksMilliseconds)) return false;
        if (searchPressed)
        {
            searchPressed = false;
            if (exploration.FindSearchTarget() is { ObjectId: 28 })
            {
                if (closure is null) throw new InvalidOperationException(UnsupportedTriggerDiagnostic);
                // The real entry is open: hand @09C3 to the host before any side effect. The
                // unit 2 host executes it; exploration stays parked at this boundary.
                PendingTrigger = new FirstMandatoryBattleTriggerRequest(0x09c3, 28);
                return true;
            }
        }
        bool updated = input.Update(ticksMilliseconds);
        if (updated)
        {
            StaticLeader leader = exploration.Current.Leader;
            State.World.Leader.X = leader.X;
            State.World.Leader.Y = leader.Y;
            State.World.Leader.Direction = DirectionName(leader.Direction);
            State.World.Leader.Layer = leader.Layer;
            State.World.Leader.Phase = leader.StepPhase;
        }
        return updated;
    }

    private static string DirectionName(int direction) => direction switch
    {
        0 => "south", 1 => "west", 2 => "north", 3 => "east",
        _ => throw new InvalidDataException("First mandatory battle leader direction is invalid.")
    };

    private static int Direction(string direction) => direction switch
    {
        "south" => 0, "west" => 1, "north" => 2, "east" => 3,
        _ => throw new InvalidDataException("First mandatory battle leader direction is invalid.")
    };
}
