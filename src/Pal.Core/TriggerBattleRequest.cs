namespace Pal.Core;

public sealed record TriggerBattleRequest(Guid Id, int Caller, TriggerPosition Position, int EnemyTeamId, bool IsBoss,
    int NextAddress, int? LossAddress, int? FleeAddress, ClassicBattleLifecycleSession Lifecycle);
