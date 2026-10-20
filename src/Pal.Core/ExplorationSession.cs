namespace Pal.Core;

public sealed record ExplorationState(StaticLeader Leader, bool IsWalking, StaticScenePlan Plan);

public sealed class ExplorationSession
{
    private readonly StaticSceneRules rules;
    private bool hasUpdated;
    private uint nextDeadline;
    public ExplorationState Current { get; private set; }

    public ExplorationSession(StaticSceneRules rules, StaticLeader initialLeader)
    {
        this.rules = rules;
        Current = new(initialLeader, false, rules.BuildPlan(initialLeader));
    }

    public bool Update(uint ticksMilliseconds, int? direction)
    {
        if (direction is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(direction));
        // SDL_TICKS_PASSED: comparisons require tick differences smaller than 2^31 ms.
        if (hasUpdated && unchecked((int)(nextDeadline - ticksMilliseconds)) > 0) return false;
        var leader = Current.Leader;
        int facing = direction ?? leader.Direction;
        short targetX = unchecked((short)(leader.X + (facing is 0 or 1 ? -16 : 16)));
        short targetY = unchecked((short)(leader.Y + (facing is 1 or 2 ? -8 : 8)));
        leader = leader with { Direction = facing };
        bool walking = direction.HasValue && !rules.IsBlocked(targetX, targetY,
            checkRange: true, checkObjects: true, selfObjectId: 0);
        StaticScenePlan plan;
        if (walking)
        {
            leader = leader with
            {
                X = targetX,
                Y = targetY,
                StepPhase = (leader.StepPhase + 1) % 4
            };
            int frameIndex = leader.WalkFramesWord == 4 ? facing * 4 + leader.StepPhase
                : facing * 3 + (leader.StepPhase % 2 == 0 ? 0 : (leader.StepPhase + 1) / 2);
            plan = rules.BuildPlan(leader, frameIndex);
        }
        else
        {
            leader = leader with { StepPhase = (leader.StepPhase & 2) ^ 2 };
            plan = rules.BuildPlan(leader);
        }
        Current = new(leader, walking, plan);
        nextDeadline = unchecked(ticksMilliseconds + 100);
        hasUpdated = true;
        return true;
    }
}
