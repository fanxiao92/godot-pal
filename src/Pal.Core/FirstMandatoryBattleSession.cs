namespace Pal.Core;

/// <summary>Pre-battle exploration boundary for the authored scene2/map12/owner28 context.</summary>
public sealed class FirstMandatoryBattleSession
{
    public const string UnsupportedTriggerDiagnostic =
        "First mandatory battle trigger @09C3 is unavailable: closure tickets #46-#53 are not implemented; no side effects were applied.";

    private readonly ExplorationSession exploration;
    private readonly ExplorationInput input;
    private bool searchPressed;

    public FirstMandatoryBattleSessionState State { get; }
    public ExplorationState Current => exploration.Current;
    public IReadOnlyList<SceneObject> Objects => exploration.Objects;

    public FirstMandatoryBattleSession(FirstMandatoryBattleProfile profile, StaticSceneRules staticRules, StaticLeader staticLeader)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(staticRules);
        ArgumentNullException.ThrowIfNull(staticLeader);
        State = profile.CreateSessionState();
        if (State.World is not { SceneId: 2, MapId: 12 } || staticRules.Objects.All(item => item.Id != 28))
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
        exploration = new ExplorationSession(rules, leader);
        input = new ExplorationInput(exploration);
    }

    public void Press(ExplorationKey key, bool repeat = false) => input.Press(key, repeat);
    public void Release(ExplorationKey key) => input.Release(key);
    public void PressSearch() => searchPressed = true;

    public bool Update(uint ticksMilliseconds)
    {
        if (searchPressed)
        {
            searchPressed = false;
            if (exploration.FindSearchTarget() is { ObjectId: 28 })
                throw new InvalidOperationException(UnsupportedTriggerDiagnostic);
        }
        return input.Update(ticksMilliseconds);
    }

    private static int Direction(string direction) => direction switch
    {
        "south" => 0, "west" => 1, "north" => 2, "east" => 3,
        _ => throw new InvalidDataException("First mandatory battle leader direction is invalid.")
    };
}
