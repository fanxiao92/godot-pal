using System.Text.Json.Nodes;
using Xunit;
using Pal.Core.State;
using Pal.Core.Exploration;
using Pal.Core.Dialogue;
using Pal.Core.Battle;
using Pal.Core.Events;

namespace Pal.Core.Tests;

/// <summary>#53 unit 3a: the eleven retail verbs (script.c 0x0013/0x0014/0x001D/0x0022/0x0024/
/// 0x0025/0x004A/0x0075/0x0078/0x009A) execute against the dynamic world.</summary>
public sealed class RetailVerbsTests
{
    [Fact]
    public void CreateIsolatedCopy_owns_current_roles_live_vitals_and_independent_repeated_copies()
    {
        var spectator = Role(5) with
        {
            Level = 9, Hp = 73, Mp = 11, CoveredByRoleId = 4,
            Attack = 71, MagicPower = 72, Defense = 73, Dexterity = 74, FleeRate = 75,
            Equipment = new[] { 81, 0, 0, 0, 0, 0 },
            Magics = new[] { 82, 0 }, Statuses = new[] { 83, 0 }
        };
        var owner = new RoleLiveState([Role(0), Role(4), spectator]);
        var source = DynamicEventWorld.ForHost(Rules(), Leader(), 2, 7, 8, owner, new[] { 4, 0 });
        VitalScript(source, 1, 10).Start("story", "owner28", 0);
        var copy = source.CreateIsolatedCopy();
        var copiedOwner = Assert.IsType<RoleLiveState>(copy.RoleLiveState);
        Assert.NotSame(owner, copiedOwner);
        Assert.Equal(new[] { 4, 0 }, copy.PartyRoles);
        Assert.NotSame(source.PartyRoles, copy.PartyRoles);
        Assert.Equal(3, copiedOwner.Roles.Count);
        foreach (int id in new[] { 0, 4, 5 })
        {
            Assert.NotSame(owner.Roles[id], copiedOwner.Roles[id]);
            Assert.NotSame(owner.Roles[id].Equipment, copiedOwner.Roles[id].Equipment);
            Assert.NotSame(owner.Roles[id].Magics, copiedOwner.Roles[id].Magics);
            Assert.NotSame(owner.Roles[id].Statuses, copiedOwner.Roles[id].Statuses);
            Assert.NotSame(owner.Roles[id].HiddenExperience, copiedOwner.Roles[id].HiddenExperience);
        }
        var heldVitals = copy.PartyVitals[4];
        var records = copiedOwner.Roles;
        Assert.Equal((110, 30, 150, 50), (heldVitals.Hp, heldVitals.Mp, heldVitals.MaxHp, heldVitals.MaxMp));
        var copiedSpectator = records[5];
        Assert.Equal((5, 9, 73, 150, 11, 50, 4), (copiedSpectator.RoleId, copiedSpectator.Level,
            copiedSpectator.Hp, copiedSpectator.MaxHp, copiedSpectator.Mp, copiedSpectator.MaxMp, copiedSpectator.CoveredByRoleId));
        Assert.Equal((71, 72, 73, 74, 75), (copiedSpectator.Attack, copiedSpectator.MagicPower,
            copiedSpectator.Defense, copiedSpectator.Dexterity, copiedSpectator.FleeRate));
        Assert.Equal(new BattleExperienceSnapshot(5, 2, 17), copiedSpectator.PrimaryExperience);
        Assert.Equal(new BattleExperienceSnapshot(17, 3, 4), copiedSpectator.HiddenExperience["attack"]);
        Assert.Equal(new[] { 81, 0, 0, 0, 0, 0 }, copiedSpectator.Equipment);
        Assert.Equal(new[] { 82, 0 }, copiedSpectator.Magics);
        Assert.Equal(new[] { 83, 0 }, copiedSpectator.Statuses);
        Assert.Throws<NotSupportedException>(() => ((IList<int>)copiedSpectator.Equipment)[0] = 9);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, BattleExperienceSnapshot>)copiedSpectator.HiddenExperience).Clear());

        VitalScript(copy, 0, -7).Start("story", "owner28", 4);
        Assert.Equal((103, 23), (heldVitals.Hp, heldVitals.Mp));
        Assert.Equal((103, 23), (records[4].Hp, records[4].Mp));
        Assert.Equal((110, 30), (owner.Roles[4].Hp, owner.Roles[4].Mp));
        VitalScript(source, 0, 5).Start("story", "owner28", 4);
        var second = source.CreateIsolatedCopy();
        Assert.Equal((115, 35), (second.PartyVitals[4].Hp, second.PartyVitals[4].Mp));
        Assert.Equal((103, 23), (heldVitals.Hp, heldVitals.Mp));
        VitalScript(second, 0, -10).Start("story", "owner28", 4);
        Assert.Equal((105, 25), (second.RoleLiveState!.Roles[4].Hp, second.RoleLiveState.Roles[4].Mp));
        Assert.Equal((115, 35), (owner.Roles[4].Hp, owner.Roles[4].Mp));
        Assert.Equal((103, 23), (heldVitals.Hp, heldVitals.Mp));
        Assert.Equal((73, 11), (records[5].Hp, records[5].Mp));
    }

    [Fact]
    public void CreateIsolatedCopy_preserves_current_object_words_leader_party_and_all_script_slots()
    {
        var source = DynamicEventWorld.ForHost(Rules(), Leader(), 2, 5, 6);
        source.InitializePartyVitals(0, 1, 150, 100, 100);
        source.InitializePartyVitals(1, 28, 240, 10, 240);
        var fixture = Fixture(false);
        fixture.EventFile["commands"]![6]!["roleId0"] = 2;
        fixture.EventFile["commands"]![6]!["roleId1"] = 1;
        CopyCommand(fixture, 10, 0x46, """{"op":"setPartyPos","x":11,"y":10,"h":1}""");
        CopyCommand(fixture, 11, 0x15, """{"op":"setPartyFacing","direction":3,"gesture":2,"memberIndex":0}""");
        CopyCommand(fixture, 12, 0x6d, """{"op":"setSceneScripts","sceneId":2,"onEnter":7,"onTeleport":8}""");
        CopyCommand(fixture, 13, 0x6d, """{"op":"setSceneScripts","sceneId":9,"onEnter":17,"onTeleport":18}""");
        CopyCommand(fixture, 14, 0x90, """{"op":"setObjectScript","objectId":777,"value":13,"slotIndex":4}""");
        CopyCommand(fixture, 15, 0x90, """{"op":"setObjectScript","objectId":888,"value":23,"slotIndex":2}""");
        CopyCommand(fixture, 16, 0x40, """{"op":"setTriggerMode","eventObjectId":28,"mode":2}""");
        CopyCommand(fixture, 17, 0x52, """{"op":"hide","frames":9}""");
        CopyCommand(fixture, 18, 0x0f, """{"op":"face","direction":3,"frame":2}""");
        CopyCommand(fixture, 19, 0, """{"op":"end","mode":"keep"}""");
        new TriggerScriptSession(fixture.Load(), Language(), source).Start("story", "owner28", 28);
        var copy = source.CreateIsolatedCopy();
        Assert.Equal(2, copy.SceneId);
        Assert.Equal((368, 168, 3, 0), (copy.Leader.X, copy.Leader.Y, copy.Leader.Direction, copy.Leader.StepPhase));
        Assert.Same(source.Leader.Sprite, copy.Leader.Sprite);
        Assert.Equal(new[] { 1, 0 }, copy.PartyRoles);
        Assert.Equal(24, copy.Battlefield);
        Assert.Equal(2, copy.SceneScripts.Count);
        Assert.Equal(Tuple.Create(7, 8), copy.SceneScripts[2]);
        Assert.Equal(Tuple.Create(17, 18), copy.SceneScripts[9]);
        Assert.Equal(2, copy.ObjectScripts.Count);
        Assert.Equal(13, copy.ObjectScripts[(777, 4)]);
        Assert.Equal(23, copy.ObjectScripts[(888, 2)]);
        var obj = copy.Objects.Single(o => o.Id == 28);
        Assert.Equal((1280, 256, 3, 2, -2, 9), (obj.X, obj.Y, obj.Direction, obj.CurrentFrame, obj.State, obj.VanishTime));
        Assert.Equal((2, 1, 11, 0), (obj.TriggerMode, obj.TriggerEntry, obj.AutoEntry, obj.AutoIdleCount));
        Assert.Equal(2500, copy.Objects.Single(o => o.Id == 29).TriggerEntry);
        Assert.Equal(2, copy.Objects.Single(o => o.Id == 30).State);
        foreach (var original in source.Objects) Assert.NotSame(original, copy.Objects.Single(o => o.Id == original.Id));

        fixture.EventFile["commands"]![3]!["x"] = 1400;
        fixture.EventFile["commands"]![12]!["onEnter"] = 27;
        fixture.EventFile["commands"]![14]!["value"] = 33;
        new TriggerScriptSession(fixture.Load(), Language(), copy).Start("story", "owner28", 28);
        Assert.Equal(1280, source.Objects.Single(o => o.Id == 28).X);
        Assert.Equal(Tuple.Create(7, 8), source.SceneScripts[2]);
        Assert.Equal(13, source.ObjectScripts[(777, 4)]);
        Assert.Equal(1400, obj.X);
        Assert.Equal(Tuple.Create(27, 8), copy.SceneScripts[2]);
        Assert.Equal(33, copy.ObjectScripts[(777, 4)]);

        fixture.EventFile["commands"]![1]!["entry"] = 2600;
        fixture.EventFile["commands"]![2]!["entry"] = 12;
        fixture.EventFile["commands"]![3]!["x"] = 1500;
        fixture.EventFile["commands"]![3]!["y"] = 400;
        fixture.EventFile["commands"]![5]!["state"] = -3;
        fixture.EventFile["commands"]![6]!["roleId0"] = 1;
        fixture.EventFile["commands"]![6]!["roleId1"] = 2;
        fixture.EventFile["commands"]![7]!["battlefieldId"] = 31;
        fixture.EventFile["commands"]![10]!["x"] = 12;
        fixture.EventFile["commands"]![11]!["direction"] = 1;
        fixture.EventFile["commands"]![12]!["onEnter"] = 37;
        fixture.EventFile["commands"]![13]!["onTeleport"] = 38;
        fixture.EventFile["commands"]![14]!["value"] = 43;
        fixture.EventFile["commands"]![15]!["value"] = 53;
        fixture.EventFile["commands"]![16]!["mode"] = 3;
        fixture.EventFile["commands"]![17]!["frames"] = 19;
        fixture.EventFile["commands"]![18]!["direction"] = 1;
        fixture.EventFile["commands"]![18]!["frame"] = 1;
        new TriggerScriptSession(fixture.Load(), Language(), source).Start("story", "owner28", 28);
        var second = source.CreateIsolatedCopy();
        Assert.Equal((1500, 400, 1, 1, 2, 19), (second.Objects[0].X, second.Objects[0].Y,
            second.Objects[0].Direction, second.Objects[0].CurrentFrame, second.Objects[0].State, second.Objects[0].VanishTime));
        Assert.Equal((3, 12, 2600, -3), (second.Objects[0].TriggerMode, second.Objects[0].AutoEntry,
            second.Objects[1].TriggerEntry, second.Objects[2].State));
        Assert.Equal((400, 168, 1, 0), (second.Leader.X, second.Leader.Y, second.Leader.Direction, second.Leader.StepPhase));
        Assert.Equal(new[] { 0, 1 }, second.PartyRoles);
        Assert.Equal(31, second.Battlefield);
        Assert.Equal(Tuple.Create(37, 8), second.SceneScripts[2]);
        Assert.Equal(Tuple.Create(17, 38), second.SceneScripts[9]);
        Assert.Equal(43, second.ObjectScripts[(777, 4)]);
        Assert.Equal(53, second.ObjectScripts[(888, 2)]);
        Assert.Equal((1400, 256, 3, 2, 2, 9), (obj.X, obj.Y, obj.Direction, obj.CurrentFrame, obj.State, obj.VanishTime));
        Assert.Equal(new[] { 1, 0 }, copy.PartyRoles);
        Assert.Equal((368, 168, 3, 24), (copy.Leader.X, copy.Leader.Y, copy.Leader.Direction, copy.Battlefield));
        Assert.Equal(Tuple.Create(17, 18), copy.SceneScripts[9]);
        Assert.Equal(23, copy.ObjectScripts[(888, 2)]);

        fixture.EventFile["commands"]![3]!["x"] = 1600;
        fixture.EventFile["commands"]![7]!["battlefieldId"] = 41;
        fixture.EventFile["commands"]![10]!["x"] = 13;
        fixture.EventFile["commands"]![6]!["roleId0"] = 2;
        fixture.EventFile["commands"]![6]!["roleId1"] = 1;
        fixture.EventFile["commands"]![12]!["onEnter"] = 47;
        fixture.EventFile["commands"]![14]!["value"] = 63;
        new TriggerScriptSession(fixture.Load(), Language(), second).Start("story", "owner28", 28);
        Assert.Equal((1600, 432, 41), (second.Objects[0].X, second.Leader.X, second.Battlefield));
        Assert.Equal(new[] { 1, 0 }, second.PartyRoles);
        Assert.Equal(Tuple.Create(47, 8), second.SceneScripts[2]);
        Assert.Equal(63, second.ObjectScripts[(777, 4)]);
        Assert.Equal((1500, 400, 31), (source.Objects[0].X, source.Leader.X, source.Battlefield));
        Assert.Equal(new[] { 0, 1 }, source.PartyRoles);
        Assert.Equal(Tuple.Create(37, 8), source.SceneScripts[2]);
        Assert.Equal(43, source.ObjectScripts[(777, 4)]);
        Assert.Equal((1400, 368, 24), (obj.X, copy.Leader.X, copy.Battlefield));
        Assert.Equal(Tuple.Create(27, 8), copy.SceneScripts[2]);
        Assert.Equal(33, copy.ObjectScripts[(777, 4)]);
    }

    [Fact]
    public void CreateIsolatedCopy_preserves_initial_frame_cycle_during_public_movement_ticks()
    {
        var rules = Rules();
        rules = rules.WithObjects(rules.Objects.Select(o => o.Id == 28
            ? o with { SpriteFrames = 3, CurrentFrame = 3 } : o));
        var source = DynamicEventWorld.ForHost(rules, Leader(), 2, 0, 0);
        var copy = source.CreateIsolatedCopy();
        var fixture = Fixture(false);
        CopyCommand(fixture, 1, 0x10, """{"op":"walkTo","x":12,"y":16,"h":0}""");
        CopyCommand(fixture, 2, 0, """{"op":"end","mode":"keep"}""");
        var trigger = new TriggerScriptSession(fixture.Load(), Language(), copy);
        trigger.Start("story", "owner28", 28);
        Assert.Equal(TriggerScriptPhase.WaitingForMovement, trigger.Phase);
        Assert.True(copy.AdvanceTriggerTick(trigger));
        // Three-frame sprites use the four-step walk cycle: frame 3 wraps to 0.
        Assert.Equal((310, 325, 2, 0), (copy.Objects[0].X, copy.Objects[0].Y,
            copy.Objects[0].Direction, copy.Objects[0].CurrentFrame));
        Assert.True(copy.AdvanceTriggerTick(trigger));
        Assert.Equal((316, 322, 2, 1), (copy.Objects[0].X, copy.Objects[0].Y,
            copy.Objects[0].Direction, copy.Objects[0].CurrentFrame));
        Assert.Equal((304, 328, 0, 3), (source.Objects[0].X, source.Objects[0].Y,
            source.Objects[0].Direction, source.Objects[0].CurrentFrame));
    }

    [Fact]
    public void CreateIsolatedCopy_preserves_legacy_initialized_and_uninitialized_vitals_without_an_owner()
    {
        var empty = DynamicEventWorld.ForHost(Rules(), Leader(), 2, 0, 0).CreateIsolatedCopy();
        Assert.Null(empty.RoleLiveState);
        Assert.Empty(empty.PartyVitals);
        var source = DynamicEventWorld.ForHost(Rules(), Leader(), 2, 0, 0);
        source.InitializePartyVitals(0, 71, 150, 13, 50);
        source.InitializePartyVitals(5, 0, 240, 7, 80);
        var copy = source.CreateIsolatedCopy();
        Assert.Null(copy.RoleLiveState);
        Assert.Equal(2, copy.PartyVitals.Count);
        Assert.Equal((71, 150, 13, 50), (copy.PartyVitals[0].Hp, copy.PartyVitals[0].MaxHp, copy.PartyVitals[0].Mp, copy.PartyVitals[0].MaxMp));
        Assert.Equal((0, 240, 7, 80), (copy.PartyVitals[5].Hp, copy.PartyVitals[5].MaxHp, copy.PartyVitals[5].Mp, copy.PartyVitals[5].MaxMp));
        Assert.NotSame(source.PartyVitals[0], copy.PartyVitals[0]);
        VitalScript(copy, 0, -10).Start("story", "owner28", 0);
        VitalScript(copy, 0, 2, revive: true).Start("story", "owner28", 5);
        Assert.Equal((61, 3, 48, 7), (copy.PartyVitals[0].Hp, copy.PartyVitals[0].Mp, copy.PartyVitals[5].Hp, copy.PartyVitals[5].Mp));
        Assert.Equal((71, 13, 0, 7), (source.PartyVitals[0].Hp, source.PartyVitals[0].Mp, source.PartyVitals[5].Hp, source.PartyVitals[5].Mp));
        source.InitializePartyVitals(0, 80, 150, 20, 50);
        Assert.Equal((61, 3), (copy.PartyVitals[0].Hp, copy.PartyVitals[0].Mp));
        Assert.Equal(80, source.CreateIsolatedCopy().PartyVitals[0].Hp);
    }

    [Fact]
    public void CreateIsolatedCopy_preserves_nonzero_auto_counter_without_source_session_subscribers()
    {
        var fixture = Fixture(false);
        CopyCommand(fixture, 12, 9, """{"op":"waitUpdate","frames":3,"runTriggers":0,"updateGestures":0}""");
        CopyCommand(fixture, 13, 0, """{"op":"end","mode":"keep"}""");
        var rules = Rules();
        rules = rules.WithObjects(rules.Objects.Select(o => o.Id == 28 ? o with { AutoEntry = 12 } : o));
        var events = fixture.Load();
        var session = new DynamicEventSession(rules, Leader(), events, Language(), Entry(events), 2, 0, 0);
        session.Update(0);
        Assert.Equal(1, session.World.Objects[0].AutoIdleCount);
        var before = session.Current;
        var copy = session.World.CreateIsolatedCopy();
        Assert.Equal((12, 1), (copy.Objects[0].AutoEntry, copy.Objects[0].AutoIdleCount));
        var move = Fixture(false);
        move.EventFile["entries"]![0]!["target"] = "L0003";
        move.EventFile["entries"]![0]!["sourceAddress"] = 3;
        CopyCommand(move, 4, 0, """{"op":"end","mode":"keep"}""");
        new TriggerScriptSession(move.Load(), Language(), copy).Start("story", "owner28", 28);
        Assert.Same(before, session.Current);
        Assert.Same(before.Plan, session.Current.Plan);
        Assert.Equal((304, 328), (session.World.Objects[0].X, session.World.Objects[0].Y));
        Assert.Equal((1280, 256), (copy.Objects[0].X, copy.Objects[0].Y));
        session.Update(100);
        Assert.Equal(2, session.World.Objects[0].AutoIdleCount);
        Assert.Equal(1, copy.Objects[0].AutoIdleCount);
        session.Update(200);
        Assert.Equal((13, 0), (session.World.Objects[0].AutoEntry, session.World.Objects[0].AutoIdleCount));
        Assert.Equal((12, 1), (copy.Objects[0].AutoEntry, copy.Objects[0].AutoIdleCount));
    }

    [Fact]
    public void CreateIsolatedCopy_owns_complete_current_roles_after_owned_final_battle_ack()
    {
        var panel = Role(1) with
        {
            Attack = 40, MagicPower = 12, Defense = 20, Dexterity = 100, FleeRate = 6,
            HiddenExperience = new Dictionary<string, BattleExperienceSnapshot>
            {
                ["health"] = new(7, 3, 55), ["magic"] = new(7, 3, 55), ["attack"] = new(7, 3, 55),
                ["magicPower"] = new(7, 3, 55), ["defense"] = new(7, 3, 55),
                ["dexterity"] = new(7, 3, 55), ["flee"] = new(7, 3, 55)
            },
            Magics = new[] { 9 }.Concat(new int[31]).ToArray()
        };
        var owner = new RoleLiveState([panel with { Hp = 80, Mp = 8 },
            panel with { RoleId = 4, Hp = 60, Mp = 4, Dexterity = 120 }, Role(5)]);
        var source = DynamicEventWorld.ForHost(Rules(), Leader(), 2, 0, 0, owner, new[] { 4, 1 });
        var beforeBattle = source.CreateIsolatedCopy();
        var growth = new ClassicBattleGrowthData(Enumerable.Repeat(100, 100).ToArray(), [new(2, 9), new(3, 10)]);
        var content = new TriggerBattleContent([panel, panel with { RoleId = 4, Dexterity = 120 }],
            [new(4, 100, 150, 40, 20, 120), new(1, 100, 150, 40, 20, 100)],
            new Dictionary<int, ClassicBattleGrowthData> { [1] = growth, [4] = growth },
            new Dictionary<int, IReadOnlyList<ClassicBattleEnemy>>
            { [18] = new[] { new ClassicBattleEnemy(7, 0, 43, 0, 0, 0, 1) { Experience = 120 } } },
            panelDeclaredRoles: [1, 4]);
        var fixture = Fixture(false);
        CopyCommand(fixture, 1, 7, """{"op":"startBattle","enemyTeamId":18,"lossTarget":null,"isBoss":true,"fleeTarget":null}""");
        CopyCommand(fixture, 2, 0, """{"op":"end","mode":"keep"}""");
        var script = TriggerScriptSession.ForSharedRoles(fixture.Load(), Language(), content,
            new CopyBattleRandom(), owner, source);
        script.Start("story", "owner28", 28);
        var battle = Assert.IsType<TriggerBattleRequest>(script.PendingBattle);
        battle.Lifecycle.SelectAttack(0, 0);
        battle.Lifecycle.SelectAttack(1, 0);
        Assert.True(battle.Lifecycle.ResolveNextAction());
        Assert.Equal(ClassicBattlePhase.Won, battle.Lifecycle.Completion!.Outcome);
        Assert.Equal((2, 60, 4), (owner.Roles[4].Level, owner.Roles[4].Hp, owner.Roles[4].Mp));
        Assert.True(script.AcknowledgeBattle(battle.Id, battle.Lifecycle.Completion));
        var copy = source.CreateIsolatedCopy();
        var copiedOwner = Assert.IsType<RoleLiveState>(copy.RoleLiveState);
        var grown = copiedOwner.Roles[4];
        // Independent PAL growth example: 5+120-100=25 primary XP, HP +10 then
        // hidden-health +1, MP +9; attack XP 7+80=87, health XP 7+160-100=67.
        Assert.Equal((4, 3, 161, 161, 59, 59, 0), (grown.RoleId, grown.Level,
            grown.Hp, grown.MaxHp, grown.Mp, grown.MaxMp, grown.CoveredByRoleId));
        Assert.Equal((44, 16, 22, 122, 8), (grown.Attack, grown.MagicPower, grown.Defense, grown.Dexterity, grown.FleeRate));
        Assert.Equal(new BattleExperienceSnapshot(25, 3, 17), grown.PrimaryExperience);
        Assert.Equal(new BattleExperienceSnapshot(87, 3, 1), grown.HiddenExperience["attack"]);
        Assert.Equal(new BattleExperienceSnapshot(67, 4, 2), grown.HiddenExperience["health"]);
        Assert.Equal(new int[6], grown.Equipment);
        Assert.Equal(new[] { 9, 10 }.Concat(new int[30]), grown.Magics);
        Assert.Equal(new int[9], grown.Statuses);
        Assert.Equal((161, 59, 161, 59), (copy.PartyVitals[4].Hp, copy.PartyVitals[4].Mp,
            copy.PartyVitals[4].MaxHp, copy.PartyVitals[4].MaxMp));
        Assert.Equal((2, 60, 4, 40), (beforeBattle.RoleLiveState!.Roles[4].Level,
            beforeBattle.PartyVitals[4].Hp, beforeBattle.PartyVitals[4].Mp, beforeBattle.RoleLiveState.Roles[4].Attack));
        Assert.Equal((2, 100, 20), (copiedOwner.Roles[5].Level, copy.PartyVitals[5].Hp, copy.PartyVitals[5].Mp));
        foreach (int id in new[] { 1, 4, 5 })
        {
            Assert.NotSame(owner.Roles[id], copiedOwner.Roles[id]);
            Assert.NotSame(owner.Roles[id].Equipment, copiedOwner.Roles[id].Equipment);
            Assert.NotSame(owner.Roles[id].Magics, copiedOwner.Roles[id].Magics);
            Assert.NotSame(owner.Roles[id].Statuses, copiedOwner.Roles[id].Statuses);
            Assert.NotSame(owner.Roles[id].HiddenExperience, copiedOwner.Roles[id].HiddenExperience);
        }
        Assert.Throws<NotSupportedException>(() => ((IList<int>)grown.Magics)[0] = 99);
        Assert.Throws<NotSupportedException>(() => ((IList<int>)grown.Statuses)[0] = 99);
        VitalScript(source, 0, -10).Start("story", "owner28", 4);
        VitalScript(copy, 0, -7).Start("story", "owner28", 4);
        Assert.Equal((151, 49), (source.PartyVitals[4].Hp, source.PartyVitals[4].Mp));
        Assert.Equal((154, 52), (copy.PartyVitals[4].Hp, copy.PartyVitals[4].Mp));
        Assert.Equal((154, 52), (copiedOwner.Roles[4].Hp, copiedOwner.Roles[4].Mp));
        Assert.Equal(44, copiedOwner.Roles[4].Attack);
    }

    // External random seam: unit multipliers, noncritical +1 damage, health weight 2.
    private sealed class CopyBattleRandom : IBattleRandom
    {
        public int NextInclusive(int from, int to) => from == 0 && to == 5 ? 1 : from;
        public float NextInclusive(float from, float to) => 1;
    }

    private static void CopyCommand(VerbsFixture fixture, int address, int opcode, string body)
    {
        var command = JsonNode.Parse(body)!.AsObject();
        command["label"] = $"L{address:X4}";
        command["next"] = address < 23 ? $"L{address + 1:X4}" : null;
        command["source"] = new JsonObject { ["index"] = address, ["opcode"] = opcode, ["operands"] = new JsonArray(0, 0, 0) };
        fixture.EventFile["commands"]![address] = command;
    }

    [Fact]
    public void RoleLiveState_freezes_six_role_records_and_rejects_duplicate_or_invalid_seed()
    {
        var equipment = new int[6];
        var magics = new int[32];
        var statuses = new int[9];
        var hidden = new Dictionary<string, BattleExperienceSnapshot> { ["attack"] = new(17, 3, 4) };
        var role = Role(5) with { Equipment = equipment, Magics = magics, Statuses = statuses, HiddenExperience = hidden };
        var seeds = Enumerable.Range(0, 5).Select(id => Role(id)).Append(role).ToArray();
        var owner = new RoleLiveState(seeds);
        equipment[0] = 81; magics[0] = 82; statuses[0] = 83; hidden["attack"] = new(99, 9, 9);
        seeds[5] = Role(5) with { Hp = 0 };

        Assert.Equal(6, owner.Roles.Count);
        Assert.Equal(100, owner.Roles[5].Hp);
        Assert.Equal(0, owner.Roles[5].Equipment[0]);
        Assert.Equal(0, owner.Roles[5].Magics[0]);
        Assert.Equal(0, owner.Roles[5].Statuses[0]);
        Assert.Equal(new BattleExperienceSnapshot(17, 3, 4), owner.Roles[5].HiddenExperience["attack"]);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<int, BattleRoleSnapshot>)owner.Roles).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<int>)owner.Roles[5].Equipment)[0] = 3);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, BattleExperienceSnapshot>)owner.Roles[5].HiddenExperience).Clear());
        Assert.Throws<InvalidDataException>(() => new RoleLiveState([Role(1), Role(1)]));
        Assert.Throws<InvalidDataException>(() => new RoleLiveState([Role(-1)]));
        Assert.Throws<InvalidDataException>(() => new RoleLiveState([Role(6)]));
        Assert.Throws<InvalidDataException>(() => new RoleLiveState([Role(1) with { Hp = 65536 }]));
        Assert.Throws<InvalidDataException>(() => new RoleLiveState([Role(1) with { Mp = -1 }]));
        Assert.Throws<InvalidDataException>(() => new RoleLiveState([null!]));
        Assert.Throws<ArgumentNullException>(() => new RoleLiveState(null!));
    }

    [Fact]
    public void Shared_world_scripts_write_the_owner_and_retained_views_without_reseeding()
    {
        var owner = new RoleLiveState([Role(0), Role(1) with { Hp = 28, Mp = 10, MaxHp = 240, MaxMp = 240 }, Role(5)]);
        var world = DynamicEventWorld.ForHost(Rules(), Leader(), 2, 0, 0, owner);
        var view = world.PartyVitals;
        var vitals = view[0];
        var records = owner.Roles;
        Assert.Same(owner, world.RoleLiveState);
        Assert.Throws<InvalidOperationException>(() => world.InitializePartyVitals(0, 1, 150, 1, 50));
        Assert.Throws<NotSupportedException>(() => ((IDictionary<int, DynamicRoleVitals>)view).Clear());
        var fixture = Fixture(false);
        fixture.EventFile["entries"]![0]!["target"] = "L0006";
        fixture.EventFile["entries"]![0]!["sourceAddress"] = 6;
        var script = new TriggerScriptSession(fixture.Load(), Language(), world);
        script.Start("story", "owner28", 0);
        Assert.Equal((150, 50), (records[0].Hp, records[0].Mp));
        Assert.Equal((150, 50), (vitals.Hp, vitals.Mp));
        Assert.Equal((240, 240), (view[1].Hp, view[1].Mp));
        Assert.Equal((100, 20), (records[5].Hp, records[5].Mp));
        Assert.Equal(new BattleExperienceSnapshot(5, 2, 17), records[0].PrimaryExperience);
        Assert.Throws<InvalidOperationException>(() => world.InitializePartyVitals(0, 1, 150, 1, 50));

        var drain = VitalScript(world, scope: 0, amount: -9999);
        drain.Start("story", "owner28", 0);
        Assert.Equal((0, 0), (vitals.Hp, vitals.Mp));
        var revive = VitalScript(world, scope: 0, amount: 2, revive: true);
        revive.Start("story", "owner28", 0);
        Assert.Equal((30, 0), (vitals.Hp, vitals.Mp));
        Assert.Equal(30, records[0].Hp);
    }

    [Fact]
    public void Shared_world_initial_party_order_targets_only_selected_owner_roles_and_is_read_only()
    {
        var owner = new RoleLiveState(Enumerable.Range(0, 6).Select(id => Role(id)).ToArray());
        var before = owner.Roles.ToDictionary(pair => pair.Key, pair => pair.Value);
        var initialParty = new List<int> { 4, 1 };
        var world = DynamicEventWorld.ForHost(Rules(), Leader(), 2, 0, 0, owner, initialParty);
        Assert.Equal(new[] { 4, 1 }, world.PartyRoles);
        foreach (int roleId in Enumerable.Range(0, 6)) Assert.Same(before[roleId], owner.Roles[roleId]);

        var script = VitalScript(world, scope: 1, amount: 10);
        script.Start("story", "owner28", 0); // caller is not in the initial party
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
        Assert.True(script.ScriptSuccess);
        Assert.Equal(before[4] with { Hp = 110, Mp = 30 }, owner.Roles[4]);
        Assert.Equal(before[1] with { Hp = 110, Mp = 30 }, owner.Roles[1]);
        foreach (int roleId in new[] { 0, 2, 3, 5 }) Assert.Same(before[roleId], owner.Roles[roleId]);
        Assert.Equal((110, 30), (world.PartyVitals[4].Hp, world.PartyVitals[4].Mp));
        Assert.Equal((110, 30), (world.PartyVitals[1].Hp, world.PartyVitals[1].Mp));

        initialParty[0] = 5;
        initialParty.Add(0);
        Assert.Equal(new[] { 4, 1 }, world.PartyRoles);
        Assert.Throws<NotSupportedException>(() => ((IList<int>)world.PartyRoles)[0] = 0);
        Assert.Throws<NotSupportedException>(() => ((IList<int>)world.PartyRoles).Clear());
        Assert.Equal(new[] { 4, 1 }, world.PartyRoles);
    }

    [Fact]
    public void Shared_world_initial_party_rejects_null_empty_duplicate_illegal_missing_and_over_capacity_without_owner_changes()
    {
        var owner = new RoleLiveState(new[] { 0, 1, 2, 4, 5 }.Select(id => Role(id)).ToArray());
        var before = owner.Roles.ToDictionary(pair => pair.Key, pair => pair.Value);
        Assert.Throws<ArgumentNullException>(() => DynamicEventWorld.ForHost(Rules(), Leader(), 2, 0, 0, owner, null!));
        Assert.Throws<ArgumentNullException>(() => DynamicEventWorld.ForHost(Rules(), Leader(), 2, 0, 0, null!, new[] { 4, 1 }));
        IReadOnlyList<int>[] invalidParties = [[], [4, 4], [-1], [6], [3], [0, 1, 2, 4]];
        foreach (var initialParty in invalidParties)
        {
            Assert.Throws<InvalidDataException>(() => DynamicEventWorld.ForHost(Rules(), Leader(), 2, 0, 0, owner, initialParty));
            Assert.Equal(5, owner.Roles.Count);
            foreach (int roleId in new[] { 0, 1, 2, 4, 5 }) Assert.Same(before[roleId], owner.Roles[roleId]);
        }
    }

    [Fact]
    public void Shared_world_initial_party_accepts_three_world_slots_without_changing_owner_records()
    {
        var owner = new RoleLiveState(Enumerable.Range(0, 6).Select(id => Role(id)).ToArray());
        var before = owner.Roles.ToDictionary(pair => pair.Key, pair => pair.Value);
        var world = DynamicEventWorld.ForHost(Rules(), Leader(), 2, 0, 0, owner, new[] { 5, 2, 0 });
        Assert.Equal(new[] { 5, 2, 0 }, world.PartyRoles); // world capacity, not three-player battle support
        foreach (int roleId in Enumerable.Range(0, 6)) Assert.Same(before[roleId], owner.Roles[roleId]);
    }

    [Fact]
    public void ForHost_existing_overloads_keep_the_default_role_zero_party()
    {
        Assert.Equal(new[] { 0 }, DynamicEventWorld.ForHost(Rules(), Leader(), 2, 0, 0).PartyRoles);
        var owner = new RoleLiveState([Role(0), Role(1)]);
        var spectator = owner.Roles[1];
        var world = DynamicEventWorld.ForHost(Rules(), Leader(), 2, 0, 0, owner);
        Assert.Equal(new[] { 0 }, world.PartyRoles);
        var script = VitalScript(world, scope: 1, amount: 10);
        script.Start("story", "owner28", 1);
        Assert.Equal((110, 30), (owner.Roles[0].Hp, owner.Roles[0].Mp));
        Assert.Same(spectator, owner.Roles[1]);
    }

    // Independent literals from global.c PAL_IncreaseHPMP: WORD write precedes signed SHORT
    // testing (not mathematical clamp); success compares the resulting two words.
    [Theory]
    [InlineData(100, 150, 20, 50, 10, 110, 30, true)]
    [InlineData(100, 150, 20, 50, 0, 100, 20, false)]
    [InlineData(100, 150, 20, 50, -20, 80, 0, true)]
    [InlineData(150, 150, 50, 50, 10, 150, 50, false)]
    [InlineData(0, 150, 20, 50, 10, 0, 20, false)]
    [InlineData(1, 150, 20, 50, -1, 0, 19, true)]
    [InlineData(32767, 65535, 32767, 65535, 1, 0, 0, true)]
    [InlineData(65535, 65535, 65535, 65535, 1, 0, 0, true)]
    [InlineData(65530, 65535, 65530, 65535, 10, 4, 4, true)]
    [InlineData(1, 65535, 0, 65535, -32768, 0, 0, true)]
    [InlineData(32768, 65535, 65535, 65535, -32768, 0, 32767, true)]
    [InlineData(40000, 65535, 50000, 65535, -32768, 7232, 17232, true)]
    [InlineData(1, 65535, 0, 65535, 32767, 0, 32767, true)]
    [InlineData(1, 150, 0, 50, -2, 0, 0, true)]
    [InlineData(0, 0, 0, 0, -1, 0, 0, false)]
    public void ChangeHpMp_matches_WORD_SHORT_alive_and_actual_change_rules_in_shared_and_legacy_worlds(
        int hp, int maxHp, int mp, int maxMp, int amount, int expectedHp, int expectedMp, bool success)
    {
        foreach (bool shared in new[] { false, true })
        {
            var owner = new RoleLiveState([Role(5) with { Hp = hp, MaxHp = maxHp, Mp = mp, MaxMp = maxMp }, Role(0)]);
            var world = shared ? DynamicEventWorld.ForHost(Rules(), Leader(), 2, 0, 0, owner)
                : DynamicEventWorld.ForHost(Rules(), Leader(), 2, 0, 0);
            if (!shared) world.InitializePartyVitals(5, hp, maxHp, mp, maxMp);
            var script = VitalScript(world, 0, amount);
            script.Start("story", "owner28", 5); // caller is RoleId, not party index 0
            Assert.Equal((expectedHp, expectedMp), (world.PartyVitals[5].Hp, world.PartyVitals[5].Mp));
            Assert.Equal(success, script.ScriptSuccess);
            if (shared)
            {
                Assert.Equal((expectedHp, expectedMp), (owner.Roles[5].Hp, owner.Roles[5].Mp));
                Assert.Equal((100, 20), (owner.Roles[0].Hp, owner.Roles[0].Mp));
            }
        }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(150, false)]
    [InlineData(100, false)]
    [InlineData(0, true)]
    [InlineData(150, true)]
    [InlineData(100, true)]
    public void ChangeHpMp_party_scope_preserves_success_flag_even_without_actual_changes(int hp, bool failedBefore)
    {
        var owner = new RoleLiveState([Role(0) with { Hp = hp, Mp = 50 }, Role(1) with { Hp = hp, Mp = 50 }, Role(5)]);
        var world = DynamicEventWorld.ForHost(Rules(), Leader(), 2, 0, 0, owner);
        var fixture = Fixture(false);
        fixture.EventFile["entries"]![0]!["target"] = "L0006";
        fixture.EventFile["entries"]![0]!["sourceAddress"] = 6;
        if (failedBefore)
        {
            fixture.EventFile["commands"]![8]!["op"] = "markFailed";
            fixture.EventFile["commands"]![8]!["source"]!["opcode"] = 0x41;
        }
        fixture.EventFile["commands"]![9]!["scope"] = 65535;
        var script = new TriggerScriptSession(fixture.Load(), Language(), world);
        script.Start("story", "owner28", 5);
        Assert.Equal(!failedBefore, script.ScriptSuccess); // party branch ignores helper result
        Assert.Equal(hp == 0 ? 0 : 150, owner.Roles[0].Hp);
        Assert.Equal(hp == 0 ? 0 : 150, owner.Roles[1].Hp);
        Assert.Equal((100, 20), (owner.Roles[5].Hp, owner.Roles[5].Mp));
    }

    [Fact]
    public void ChangeHpMp_success_does_not_reset_an_earlier_failure_or_roll_back_applied_words()
    {
        var owner = new RoleLiveState([Role(0)]);
        var world = DynamicEventWorld.ForHost(Rules(), Leader(), 2, 0, 0, owner);
        var fixture = Fixture(false);
        fixture.EventFile["entries"]![0]!["target"] = "L0008";
        fixture.EventFile["entries"]![0]!["sourceAddress"] = 8;
        fixture.EventFile["commands"]![8]!["op"] = "markFailed";
        fixture.EventFile["commands"]![8]!["source"]!["opcode"] = 0x41;
        fixture.EventFile["commands"]![9]!["scope"] = 0;
        fixture.EventFile["commands"]![9]!["amount"] = 10;
        var script = new TriggerScriptSession(fixture.Load(), Language(), world);
        script.Start("story", "owner28", 0);
        Assert.False(script.ScriptSuccess);
        Assert.Equal((110, 30), (owner.Roles[0].Hp, owner.Roles[0].Mp));
    }

    private static TriggerScriptSession VitalScript(DynamicEventWorld world, int scope, int amount, bool revive = false)
    {
        var fixture = Fixture(false);
        fixture.EventFile["entries"]![0]!["target"] = "L0009";
        fixture.EventFile["entries"]![0]!["sourceAddress"] = 9;
        var command = fixture.EventFile["commands"]![9]!;
        command["scope"] = scope;
        if (revive)
        {
            command["op"] = "revivePlayer";
            command.AsObject().Remove("amount");
            command["healthTenths"] = amount;
            command["source"]!["opcode"] = 0x22;
        }
        else command["amount"] = amount;
        return new(fixture.Load(), Language(), world);
    }

    private static BattleRoleSnapshot Role(int id) => new(id, 2, 100, 150, 20, 50, new(5, 2, 17),
        new Dictionary<string, BattleExperienceSnapshot> { ["attack"] = new(17, 3, 4) },
        new int[6], new int[32], new int[9], 0);

    [Fact]
    public void World_verbs_apply_entries_positions_states_party_battlefield_and_vitals()
    {
        DynamicEventSession session = Session();
        RunTrigger(session);

        var owner = session.World.Objects.Single(o => o.Id == 28);
        Assert.Equal(11, owner.AutoEntry);                                        // 0x0024
        Assert.Equal(2500, session.World.Objects.Single(o => o.Id == 29).TriggerEntry); // 0x0025
        Assert.Equal((1280, 256), (owner.X, owner.Y));                           // 0x0013
        Assert.Equal(0, owner.Direction);                                        // 0x0014 south + frame 3
        Assert.Equal(3, owner.CurrentFrame);
        Assert.Equal((2, 2), (session.World.Objects.Single(o => o.Id == 29).State,
            session.World.Objects.Single(o => o.Id == 30).State));               // 0x009A range 29..30
        Assert.Equal(new[] { 0, 1 }, session.World.PartyRoles);                   // 0x0075 words 1,2
        Assert.Equal(24, session.World.Battlefield);                              // 0x004A
        Assert.Equal((150, 100), (session.World.PartyVitals[0].Hp, session.World.PartyVitals[0].Mp)); // clamped at max
        Assert.Equal((240, 240), (session.World.PartyVitals[1].Hp, session.World.PartyVitals[1].Mp));
    }

    [Fact]
    public void Revive_returns_dead_roles_at_maxhp_tenths_without_touching_mp()
    {
        DynamicEventSession session = Session(revive: true);
        RunTrigger(session);
        Assert.Equal(DynamicEventPhase.Exploring, session.Phase);
        Assert.Equal(15, session.World.PartyVitals[0].Hp);  // 150 * 1 / 10
        Assert.Equal(24, session.World.PartyVitals[1].Hp);  // 240 * 1 / 10
        Assert.Equal((0, 0), (session.World.PartyVitals[0].Mp, session.World.PartyVitals[1].Mp)); // still drained
    }

    [Fact]
    public void Party_vital_verbs_reject_when_the_host_did_not_supply_the_profile()
    {
        var events = Fixture(revive: false).Load();
        var session = new DynamicEventSession(Rules(), Leader(), events, Language(), Entry(events), 2, 0, 0);
        session.PressSearch();
        Assert.True(session.Update(0));
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => session.StartTrigger(session.PendingTrigger!.Id));
        Assert.Contains("vitals for role", error.Message, StringComparison.Ordinal);
    }

    private static void RunTrigger(DynamicEventSession session)
    {
        session.PressSearch();
        Assert.True(session.Update(0));
        Assert.NotNull(session.PendingTrigger);
        session.StartTrigger(session.PendingTrigger.Id);
        // The verbs execute synchronously; the return phase then settles back to exploring.
        for (uint tick = 100; session.Phase != DynamicEventPhase.Exploring && tick <= 1000; tick += 100)
            Assert.True(session.Update(tick));
        Assert.Equal(DynamicEventPhase.Exploring, session.Phase);
    }

    private static DynamicEventSession Session(bool revive = false)
    {
        var events = Fixture(revive).Load();
        var session = new DynamicEventSession(Rules(), Leader(), events, Language(), Entry(events), 2, 0, 0);
        session.InitializePartyVitals(0, 1, 150, 100, 100);
        session.InitializePartyVitals(1, 28, 240, 10, 240);
        return session;
    }

    private static StaticSceneRules Rules() => new(
        new SceneMap(2, new uint[16384], [new SceneFrame(0, 32, 15)]),
    [
        new SceneObject(28, 304, 328, 2, 0, 0, 210, 0, 0, 0) { TriggerMode = 1, TriggerEntry = 1 },
        new SceneObject(29, 300, 300, 1, 0, 0, 211, 0, 0, 0),
        new SceneObject(30, 320, 300, 1, 0, 0, 212, 0, 0, 0),
    ]);

    private static StaticLeader Leader() => new(320, 320, 0, 0, 0, 3,
        new SceneSprite(17, Enumerable.Range(0, 12).Select(i => new SceneFrame(i, 20, 30))));

    private static DialogueLanguagePackage Language() => new("zh-Hans", []);

    private static TriggerEventEntry Entry(TriggerEventCollection events) =>
        events.Entries.Single(entry => entry.OwnerId == 28 && entry.Slot == "wTriggerScript");

    internal sealed class VerbsFixture
    {
        public JsonObject Index { get; } = new()
        {
            ["formatVersion"] = 1, ["profile"] = "faithful",
            ["source"] = new JsonObject { ["archive"] = "SSS.MKF", ["chunk"] = 4, ["byteOrder"] = "little",
                ["instructionCount"] = 24, ["byteLength"] = 192,
                ["sha256"] = "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd" },
            ["files"] = new JsonArray("story")
        };
        public JsonObject EventFile { get; } = Build();
        private static JsonObject Build()
        {
            var commands = new JsonArray();
            int address = 0;
            void Put(string op, int opcode, int a, int b, int c, params (string Key, JsonNode? Value)[] fields)
            {
                var command = new JsonObject { ["label"] = $"L{address:X4}", ["op"] = op,
                    ["next"] = address + 1 < 24 ? $"L{address + 1:X4}" : null };
                foreach (var (key, value) in fields) command[key] = value;
                command["source"] = new JsonObject { ["index"] = address, ["opcode"] = opcode, ["operands"] = new JsonArray(a, b, c) };
                commands.Add(command);
                address++;
            }
            Put("raw", 134, 0, 65535, 8); // unreached retained row so the entry address is nonzero
            Put("setTriggerEntry", 0x25, 29, 2500, 0, ("eventObjectId", 29), ("entry", 2500)); // not the caller: its entry word is restored on completion
            Put("setAutoEntry", 0x24, 28, 11, 0, ("eventObjectId", 28), ("entry", 11)); // a valid end address for the auto runner
            Put("setObjectPosition", 0x13, 28, 1280, 256, ("eventObjectId", 28), ("x", 1280), ("y", 256));
            Put("setObjectGesture", 0x14, 3, 0, 0, ("frame", 3));
            Put("setStateRange", 0x9a, 29, 30, 2, ("fromEventObjectId", 29), ("toEventObjectId", 30), ("state", 2));
            Put("setParty", 0x75, 1, 2, 0, ("roleId0", 1), ("roleId1", 2), ("roleId2", 0));
            Put("setBattlefield", 0x4a, 24, 0, 0, ("battlefieldId", 24));
            Put("noop", 0x78, 0, 0, 0);
            Put("changeHpMp", 0x1d, 1, 9999, 0, ("scope", 1), ("amount", 9999));
            Put("end", 0, 0, 0, 0, ("mode", "keep")); // explicit stop; the filler stays unreached
            Put("end", 0, 0, 0, 0, ("mode", "keep")); // second stop so the revive variant can chain
            while (address < 24) Put("raw", 134, 0, 65535, 8); // retained filler: dense address coverage
            return new JsonObject
            {
                ["formatVersion"] = 1, ["profile"] = "faithful", ["fileId"] = "story",
                ["entries"] = new JsonArray(new JsonObject { ["id"] = "owner28",
                    ["owner"] = new JsonObject { ["type"] = "eventObject", ["id"] = 28 },
                    ["slot"] = "wTriggerScript", ["kind"] = "trigger", ["target"] = "L0001", ["sourceAddress"] = 1 }),
                ["commands"] = commands
            };
        }
        public TriggerEventCollection Load() => TriggerEventCollection.Parse(Index.ToJsonString(),
            new Dictionary<string, string> { ["story"] = EventFile.ToJsonString() });
    }

    internal static VerbsFixture Fixture(bool revive)
    {
        var fixture = new VerbsFixture();
        if (revive)
        {
            var commands = fixture.EventFile["commands"]!.AsArray();
            var restore = (JsonObject)commands[9]!; // the changeHpMp row
            restore["amount"] = -9999; // drain first so revivePlayer has dead roles
            var stop = (JsonObject)commands[10]!; // the explicit end row becomes the revive step
            stop["op"] = "revivePlayer";
            stop["scope"] = 1;
            stop["healthTenths"] = 1;
            stop.Remove("mode");
            stop["next"] = "L000B"; // flows into the retained filler; the call stops at its next
            var source = (JsonObject)stop["source"]!;
            source["opcode"] = 34;
            source["operands"] = new JsonArray(1, 1, 0);
        }
        return fixture;
    }
}
