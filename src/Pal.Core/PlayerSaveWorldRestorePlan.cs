namespace Pal.Core;

/// <summary>The prepared-content closure one world-domain restore is validated against
/// (M6-C leaf #226; parent #148). The frozen #221 contract and the #223 codec validate shape
/// domains only — whether the saved scene, its event objects and the leader's static identity
/// can actually be rebuilt from the prepared content is decided here, at candidate
/// construction, fail-closed per the matrix's compatibility rule: a scene outside the closure
/// can never correspond to a real capture and rejects with a field-locatable diagnostic
/// instead of a silent downgrade to a default scene (#148 Decision 24). The host derives the
/// closure from the prepared content the save's <see cref="PlayerSaveV1.ContentIdentity"/>
/// names: the scene id it rebuilds, that scene's static rules, and the leader's static
/// identity words (sprite and walk-frame word are content, never file bytes — #148
/// Decision 15).</summary>
public sealed record PlayerSaveWorldRestoreContent(
    int SceneId,
    StaticSceneRules SceneRules,
    StaticLeader LeaderIdentity);

/// <summary>The world-domain restore candidate: a freshly constructed shared-owner
/// <see cref="DynamicEventWorld"/> seeded from one decoded v1 snapshot's world domain, plus the
/// rebuilt <see cref="ExplorationSession"/> view over it. <see cref="Rules"/> is the exact
/// world-projected rule set that view was built from — the host's rebuilt exploration owner
/// (its interaction session) consumes it instead of re-deriving the projection (#227). The
/// candidate commit/switch (old session retirement) is a later leaf's boundary (c7): this
/// record is a plan's output, not a live session swap, and constructing it never advanced a
/// script, a world tick or gameplay randomness (#148 Decision 24).</summary>
public sealed record PlayerSaveWorldRestoreCandidate(
    StaticSceneRules Rules,
    DynamicEventWorld World,
    ExplorationSession Exploration,
    TriggerMusicState? Music);

/// <summary>M6-C leaf #226 (parent #148): the scene/variable-entry restore plan (frozen matrix
/// D10–D13 and restore steps 7–8 in docs/milestones/m6/m6c-save-matrix.md).
/// <see cref="BuildWorld"/> rebuilds the world domain from one decoded
/// <see cref="PlayerSaveV1"/> following the frozen order: the saved leader words over the
/// prepared static identity and the current scene's retained script row seeding a fresh
/// shared-owner world (D10; the walking mid-step stays cleared and a new session time base
/// starts — Decision 16), every event object's dynamic words including the trigger/auto entry
/// addresses and the shared idle counter (D11; the reference loads the whole EVENTOBJECT row
/// back, global.c PAL_LoadGame_Common), the retained scene/object script slots — cross-scene
/// rows included, retained data that loading never executes (D12; script.c 0x006D/0x0090
/// writes), the battlefield word and the saved setMusic words returned for the host's D13
/// owner. Fail-closed and never partial: the closure is validated before any owner is
/// constructed, so any rejection leaves no half-built state anywhere (#148 Stories 50/54; no
/// clamping — the frozen contract already rejected every out-of-domain word before this plan
/// ran). The restored party order reseeds the world party (D5's second view over the
/// #225 role-domain candidate this plan consumes).</summary>
public static class PlayerSaveWorldRestorePlan
{
    /// <summary>Builds the complete world-domain candidate from one decoded v1 snapshot and the
    /// role-domain candidate the restore order requires to run first (matrix step 7 follows
    /// steps 3–6). Throws <see cref="InvalidDataException"/> with a <c>$.</c>-prefixed locatable
    /// diagnostic when the snapshot references content the supplied closure does not declare;
    /// nothing is applied anywhere before the full candidate exists.</summary>
    public static PlayerSaveWorldRestoreCandidate BuildWorld(PlayerSaveV1 save,
        PlayerSaveWorldRestoreContent content, PlayerSaveRestoreCandidate roles)
    {
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(roles);
        ValidateWorldClosure(save, content);
        var saved = save.World;

        // Frozen restore order, world domain (docs/milestones/m6/m6c-save-matrix.md steps 7–8).
        // D10 first: scene identity and leader — the five saved words ride on the prepared
        // static leader identity; the sprite and the walk-frame word stay content words.
        var leader = content.LeaderIdentity with
        {
            X = saved.Leader.X,
            Y = saved.Leader.Y,
            Direction = saved.Leader.Direction,
            Layer = (ushort)saved.Leader.Layer,
            StepPhase = saved.Leader.StepPhase,
        };
        // A real capture always carries the current scene's retained row (the world seeds it on
        // entry), so it — never a guessed static default — is the constructor's scene seed.
        var currentRow = saved.SceneScripts.Single(row => row.SceneId == saved.SceneId);

        // The fresh shared-owner world through the public ForHost chain: the restored party
        // order reseeds the world party (D5's second view) and the party-vital views bind the
        // role-domain candidate's owner — the same construction a host entering the scene uses.
        var world = DynamicEventWorld.ForHost(content.SceneRules, leader, saved.SceneId,
            currentRow.OnEnterEntry, currentRow.OnTeleportEntry, roles.Roles, roles.PartyRoleIds);

        // D11: every dynamic word of every object — dispatch words included, so a consumed
        // trigger/auto entry resumes from its saved address and both per-object idle counters
        // keep their progress (the reference stores the whole EVENTOBJECT, global.c:869; Story 22);
        // sprite identity and frame counts stay with the prepared records and never come from the
        // file (Story 24). Pure word writes on a world that is not yet published: no script runs,
        // no world tick advances and no event fires.
        foreach (var row in saved.Objects)
        {
            var obj = world.Find(row.ObjectId)
                ?? throw new InvalidDataException($"$.world.objects.objectId: event object {row.ObjectId} is not in the prepared scene closure.");
            obj.X = row.X;
            obj.Y = row.Y;
            obj.Direction = row.Direction;
            obj.CurrentFrame = row.CurrentFrame;
            obj.State = row.State;
            obj.VanishTime = row.VanishTime;
            obj.TriggerMode = row.TriggerMode;
            obj.TriggerEntry = row.TriggerEntry;
            obj.AutoEntry = row.AutoEntry;
            obj.AutoIdleCount = row.AutoIdleCount;
            obj.TriggerIdleCount = row.TriggerIdleCount;
        }

        // D12: the retained scene/object script slots write back through the world's own word
        // owners — retained data loading never executes; the current scene's row is rewritten
        // to its exact saved words and cross-scene rows survive for the later scene entry
        // (Story 23: an unloaded scene's recorded change is never dropped).
        foreach (var row in saved.SceneScripts)
            world.SetSceneScripts(row.SceneId, row.OnEnterEntry, row.OnTeleportEntry);
        foreach (var row in saved.ObjectScripts)
            world.SetObjectScript(row.ObjectId, row.Entry, row.SlotIndex);

        // D13: the battlefield control word; the setMusic words return for the host's D13 owner
        // (a null before the first setMusic restores as null, never a default track).
        world.Battlefield = saved.Battlefield;

        // Step 8: the rebuilt exploration view — the same object-word projection the live
        // DynamicEventSession applies, the saved leader, a fresh BuildPlan and a brand-new
        // scheduling basis: no old absolute deadline is restored and shutdown downtime never
        // replays (Decision 16; the first due update commits exactly once at any new tick).
        var restoredRules = content.SceneRules.WithObjects(world.Objects.Select(DynamicEventSession.ObjectView));
        var exploration = new ExplorationSession(restoredRules, leader);

        return new(restoredRules, world, exploration,
            saved.Music is { } music ? new TriggerMusicState(music.MusicId, music.Mode) : null);
    }

    /// <summary>Reference-closure preflight (matrix compatibility rule 2): the prepared closure
    /// must rebuild exactly the saved scene, the saved object rows must cover the prepared
    /// scene's objects one-to-one (an uncovered object would silently reset to its initial
    /// words — a content mismatch, not a repair), and the current scene's retained script row
    /// must exist because every real capture carries it. All checks run before any owner is
    /// constructed.</summary>
    private static void ValidateWorldClosure(PlayerSaveV1 save, PlayerSaveWorldRestoreContent content)
    {
        if (content.SceneId != save.World.SceneId)
            throw new InvalidDataException($"$.world.sceneId: the prepared content closure rebuilds scene {content.SceneId}; saved scene {save.World.SceneId} is not rebuildable from this closure.");
        if (save.World.SceneScripts.All(row => row.SceneId != save.World.SceneId))
            throw new InvalidDataException($"$.world.sceneScripts: the current scene {save.World.SceneId}'s retained script row is required by a real capture.");
        var prepared = content.SceneRules.Objects.Select(o => (int)o.Id).ToHashSet();
        foreach (var row in save.World.Objects)
            if (!prepared.Contains(row.ObjectId))
                throw new InvalidDataException($"$.world.objects.objectId: event object {row.ObjectId} is not in the prepared scene closure.");
        foreach (int objectId in prepared)
            if (save.World.Objects.All(row => row.ObjectId != objectId))
                throw new InvalidDataException($"$.world.objects: the prepared scene declares event object {objectId} the saved world does not carry; the content identity does not match the snapshot.");
    }
}
