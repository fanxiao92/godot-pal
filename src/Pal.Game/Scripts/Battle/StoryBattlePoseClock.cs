namespace Pal.Game.Battle;

/// <summary>25FPS minimum pose duration. Late delivery advances one pose and starts a fresh duration.
/// Like Core tick comparisons, elapsed intervals must remain below 2^31 milliseconds.</summary>
public readonly record struct StoryBattlePoseClock(uint PoseStartedAt, int Pose)
{
    public static StoryBattlePoseClock Start(uint now) => new(now, 0);
    public StoryBattlePoseClock Advance(uint now) => unchecked((int)(now - PoseStartedAt)) < 40
        ? this : new(now, Pose + 1);
}

/// <summary>Facts from the actual Main presentation callback, on its production monotonic clock.</summary>
public readonly record struct StoryBattlePoseSnapshot(uint Now, uint PoseStartedAt, int Pose);
