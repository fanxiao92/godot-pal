using System.Text.Json.Nodes;
using Pal.TestFixtures;
using Xunit;

namespace Pal.Core.Tests;

/// <summary>Designated functional RED for #53 unit 1: the capability precheck must accept a real
/// resource closure and open @09C3, where base 7fb6af2 rejects the same search unconditionally
/// because the closure is only declared.</summary>
public sealed class FirstMandatoryBattleClosureTests
{
    [Fact]
    public void Real_closure_verifies_and_opens_the_owner28_search_without_side_effects()
    {
        (FirstMandatoryBattleProfile profile, RealClosureFixture fixture) = PreparedProfile();
        FirstMandatoryBattleClosureReceipt receipt = FirstMandatoryBattleClosure.Verify(profile,
            fixture.Events(), fixture.Chinese(), fixture.English(), fixture.Content(), Images());

        Assert.Equal(0x09c3, receipt.TriggerEntryAddress);
        Assert.Equal(profile.ContextSha256, receipt.ContextSha256);
        Assert.Equal(new[] { 18 }, receipt.VerifiedBattleTeamIds);

        var session = new FirstMandatoryBattleSession(profile, 2, Rules(), Leader(), receipt);
        session.Update(0);
        session.PressSearch();
        Assert.True(session.Update(100));
        Assert.Equal(new FirstMandatoryBattleTriggerRequest(0x09c3, 28), session.PendingTrigger);
        Assert.Equal(1264, session.State.World.Leader.X); // no side effect before the host consumes the trigger
        Assert.Equal(0x09c3, session.State.World.EventObjects.Single(item => item.OwnerId == 28).TriggerEntry);
    }

    [Fact]
    public void Explicit_cash_capability_preflights_both_languages_and_all_branches_without_execution()
    {
        var (profile, fixture) = PreparedProfile();
        var commands = fixture.EventFile["commands"]!.AsArray();
        // Independent modern literals: a debit then halve would change every nonzero seed if run.
        // The shortage goes straight to battle; preflight must still walk the normal path too.
        void CashCommand(int address, string op, int opcode, int? amount = null, int target = 0x09fe)
        {
            var command = new JsonObject
            {
                ["label"] = $"L{address:X4}", ["op"] = op, ["next"] = $"L{address + 1:X4}",
                ["source"] = new JsonObject { ["index"] = address, ["opcode"] = opcode,
                    ["operands"] = new JsonArray(amount is null ? 0 : 65535, target, 0) }
            };
            if (amount is not null) { command["amount"] = amount.Value; command["to"] = $"L{target:X4}"; }
            commands[address] = command;
        }
        CashCommand(0x09c5, "changeCash", 0x001e, -1);
        CashCommand(0x09c6, "halveCash", 0x008f);
        var events = fixture.Events();
        var content = fixture.Content();
        string eventBytes = fixture.EventFile.ToJsonString();
        string contentBytes = System.Text.Json.JsonSerializer.Serialize(content);
        string authorBytes = System.Text.Json.JsonSerializer.Serialize(profile.World);
        var chinese = fixture.Chinese();
        var english = fixture.English();
        Assert.Contains("cash owner", Assert.Throws<InvalidDataException>(() =>
            FirstMandatoryBattleClosure.Verify(profile, events, chinese, english, content, Images())).Message);
        foreach (uint initial in new uint[] { 0, 10, 2147483648, 4294967295 })
        {
            var cash = new CashLiveState(initial);
            for (int repeat = 0; repeat < 2; repeat++)
            {
                // Designated RED: the API shell ignores the explicit owner at @09C5.
                var receipt = FirstMandatoryBattleClosure.Verify(profile, events, chinese, english, content,
                    Images(), cashLiveState: cash);
                Assert.Equal(new[] { 18 }, receipt.VerifiedBattleTeamIds);
                Assert.Equal(profile.ContextSha256, receipt.ContextSha256);
                Assert.Equal(0x09c3, receipt.TriggerEntryAddress);
                Assert.Equal(initial, cash.Cash);
            }
        }
        // The receipt never supplies a default owner for a later unbound caller.
        Assert.Contains("cash owner", Assert.Throws<InvalidDataException>(() =>
            FirstMandatoryBattleClosure.Verify(profile, events, chinese, english, content, Images(), cashLiveState: null)).Message);

        // Both language checks must remain in force after cash capability was added. Message11
        // is on the Won continuation, after the cash prefix; no execution reaches that boundary.
        var missingWon = new DialogueLanguagePackage("en",
            Enumerable.Range(10, 15).Where(id => id != 11).Select(id => english.Resolve(id, 0))
                .Concat(Enumerable.Range(0, 4).Select(band => english.Resolve(13 + band * 2, 1))));
        var unchanged = new CashLiveState(10);
        Assert.Contains("11", Assert.Throws<InvalidDataException>(() =>
            FirstMandatoryBattleClosure.Verify(profile, events, chinese, missingWon, content, Images(), unchanged)).Message);
        Assert.Equal(10u, unchanged.Cash);

        // Positive credit can never take shortage at runtime, but its target is still raw and
        // must reject. Do not prune the branch using balance or the sign of the cash amount.
        CashCommand(0x09c5, "changeCash", 0x001e, 7, 0x0a33);
        var unsupported = fixture.Events();
        foreach (uint initial in new uint[] { 0, 4294967295 })
        {
            var cash = new CashLiveState(initial);
            var error = Assert.Throws<InvalidDataException>(() => FirstMandatoryBattleClosure.Verify(profile,
                unsupported, chinese, english, content, Images(), cash));
            Assert.Contains("@0A33", error.Message);
            Assert.Equal(initial, cash.Cash);
        }
        // Restore only our authored test command; the production checker must not change inputs.
        CashCommand(0x09c5, "changeCash", 0x001e, -1);
        Assert.Equal(eventBytes, fixture.EventFile.ToJsonString());
        Assert.Equal(contentBytes, System.Text.Json.JsonSerializer.Serialize(content));
        Assert.Equal(authorBytes, System.Text.Json.JsonSerializer.Serialize(profile.World));
        // No random interface is supplied to Verify: battle execution/RNG is not this seam.
    }

    [Fact]
    public void Search_opens_the_trigger_only_once_and_parks_exploration()
    {
        (FirstMandatoryBattleProfile profile, RealClosureFixture fixture) = PreparedProfile();
        FirstMandatoryBattleClosureReceipt receipt = FirstMandatoryBattleClosure.Verify(profile,
            fixture.Events(), fixture.Chinese(), fixture.English(), fixture.Content(), Images());
        var session = new FirstMandatoryBattleSession(profile, 2, Rules(), Leader(), receipt);
        session.Update(0);
        session.PressSearch();
        Assert.True(session.Update(100));
        session.PressSearch();
        session.Press(ExplorationKey.SouthPrimary);
        Assert.False(session.Update(200));
        Assert.Equal(new FirstMandatoryBattleTriggerRequest(0x09c3, 28), session.PendingTrigger);
    }

    [Fact]
    public void Receipt_from_another_context_does_not_open_this_entry()
    {
        (FirstMandatoryBattleProfile profile, RealClosureFixture fixture) = PreparedProfile();
        FirstMandatoryBattleClosureReceipt receipt = FirstMandatoryBattleClosure.Verify(profile,
            fixture.Events(), fixture.Chinese(), fixture.English(), fixture.Content(), Images());
        var otherContext = BattleContextTestFixture.Valid();
        otherContext.Document["snapshot"]!["world"]!["musicId"] = 32;
        FirstMandatoryBattleProfile other = Load(otherContext, prepared: true);
        Assert.NotEqual(profile.ContextSha256, other.ContextSha256);
        Assert.Throws<InvalidDataException>(() => new FirstMandatoryBattleSession(other, 2, Rules(), Leader(), receipt));
    }

    [Fact]
    public void Unverified_closure_still_rejects_the_search_before_side_effects()
    {
        (FirstMandatoryBattleProfile profile, _) = PreparedProfile();
        var session = new FirstMandatoryBattleSession(profile, 2, Rules(), Leader());
        session.Update(0);
        session.PressSearch();
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => session.Update(100));
        Assert.Equal(FirstMandatoryBattleSession.UnsupportedTriggerDiagnostic, error.Message);
        Assert.Null(session.PendingTrigger);
    }

    [Theory]
    [InlineData("wrong-operands")]
    [InlineData("single-enemy")]
    [InlineData("missing-dying-hook")]
    [InlineData("undeclared-equipment-grid")]
    [InlineData("missing-dual-attack-object")]
    [InlineData("wrong-images")]
    [InlineData("missing-english-message")]
    public void Every_closure_gap_rejects_before_the_entry_opens(string fault)
    {
        (FirstMandatoryBattleProfile profile, RealClosureFixture fixture) = PreparedProfile(fault);
        Assert.Throws<InvalidDataException>(() => FirstMandatoryBattleClosure.Verify(profile,
            fixture.Events(), fixture.Chinese(), fixture.English(), fixture.Content(), Images(fault == "wrong-images")));
    }

    [Fact]
    public void Contract_only_closure_reports_the_first_declared_entry()
    {
        (FirstMandatoryBattleProfile profile, RealClosureFixture fixture) = ContractOnlyProfile();
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => FirstMandatoryBattleClosure.Verify(profile,
            fixture.Events(), fixture.Chinese(), fixture.English(), fixture.Content(), Images()));
        Assert.Equal(string.Format(FirstMandatoryBattleClosure.ContractOnlyDiagnostic, profile.Closure.Entries[0].Id), error.Message);
    }

    [Fact]
    public void Owned_call_completes_returns_and_requires_a_fresh_origin_for_the_next_search()
    {
        var session = VerifiedSession();
        var trigger = ReturnTrigger();
        Assert.Equal(Guid.Empty, session.PendingRequestId);
        session.PressSearch();
        Assert.True(session.Update(0));
        Guid first = session.PendingRequestId;
        Assert.NotEqual(Guid.Empty, first);
        trigger.Start("return", "owner28", 28, first);
        Assert.True(session.BindTrigger(first, trigger));
        Assert.False(session.CompleteTrigger(first, trigger, 100));
        FinishReturnDisplay(trigger);
        Assert.True(session.CompleteTrigger(first, trigger, 100));
        Assert.Null(session.PendingTrigger);
        Assert.Equal(Guid.Empty, session.PendingRequestId);
        Assert.Equal(0x09c3, session.Rules.Objects.Single().TriggerEntry);
        Assert.Equal(0x09c3, session.Objects.Single().TriggerEntry);
        Assert.Equal(0x09c3, session.State.World.EventObjects.Single(o => o.OwnerId == 28).TriggerEntry);
        Assert.False(session.Update(149));
        Assert.True(session.Update(150));
        session.PressSearch();
        Assert.True(session.Update(250));
        Guid second = session.PendingRequestId;
        Assert.NotEqual(first, second);
        Assert.Equal(new FirstMandatoryBattleTriggerRequest(0x09c3, 28), session.PendingTrigger);
        Assert.False(session.BindTrigger(second, trigger));
        Assert.False(session.CompleteTrigger(second, trigger, 250));
        trigger.Start("return", "owner28", 28, second);
        Assert.True(session.BindTrigger(second, trigger));
        FinishReturnDisplay(trigger);
        Assert.True(session.CompleteTrigger(second, trigger, 350));
        Assert.True(session.Update(400));
    }

    [Fact]
    public void Binding_rejects_unstarted_presearch_foreign_and_wrong_entry_calls_without_changing_exploration()
    {
        var session = VerifiedSession();
        var trigger = ReturnTrigger(false);
        Assert.False(session.BindTrigger(Guid.NewGuid(), trigger));
        Assert.False(session.CompleteTrigger(Guid.NewGuid(), trigger, 0));
        trigger.Start("return", "owner28", 28); // Real completed call, but before this search.
        session.PressSearch();
        Assert.True(session.Update(0));
        Guid id = session.PendingRequestId;
        var pending = session.PendingTrigger;
        var current = session.Current;
        var rules = session.Rules;
        string world = System.Text.Json.JsonSerializer.Serialize(session.State.World);
        Assert.False(session.BindTrigger(id, trigger));
        Assert.False(session.BindTrigger(id, ReturnTrigger(false)));
        Assert.Throws<ArgumentNullException>(() => session.BindTrigger(id, null!));
        Assert.Throws<ArgumentNullException>(() => session.CompleteTrigger(id, null!, 100));
        trigger.Start("return", "owner28", 28, Guid.NewGuid());
        Assert.False(session.BindTrigger(id, trigger));
        foreach (var wrong in new[]
        {
            ReturnTrigger(false, owner: 29),
            ReturnTrigger(false, ownerType: "scene", slot: "wScriptOnEnter"),
            ReturnTrigger(false, address: 0x09c4)
        })
        {
            wrong.Start("return", "owner28", 28, id);
            Assert.False(session.BindTrigger(id, wrong));
            Assert.False(session.CompleteTrigger(id, wrong, 100));
        }
        trigger.Start("return", "owner28", 29, id);
        Assert.False(session.BindTrigger(id, trigger));
        trigger.Start("return", "owner28", 28, id);
        Assert.False(session.BindTrigger(Guid.Empty, trigger));
        Assert.False(session.BindTrigger(Guid.NewGuid(), trigger));
        Assert.False(session.CompleteTrigger(id, trigger, 100)); // Completed but unbound.
        Assert.Same(pending, session.PendingTrigger);
        Assert.Equal(id, session.PendingRequestId);
        Assert.Same(current, session.Current);
        Assert.Same(rules, session.Rules);
        Assert.Equal(world, System.Text.Json.JsonSerializer.Serialize(session.State.World));
        Assert.True(session.BindTrigger(id, trigger));
        Assert.True(session.CompleteTrigger(id, trigger, 100));
        Assert.False(session.Update(149));
        Assert.True(session.Update(150));
    }

    [Fact]
    public void Bound_call_rejects_unfinished_foreign_duplicate_and_restarted_call_identity()
    {
        var session = VerifiedSession();
        session.PressSearch(); session.Update(0);
        Guid id = session.PendingRequestId;
        var trigger = ReturnTrigger();
        var foreign = ReturnTrigger(false);
        trigger.Start("return", "owner28", 28, id);
        foreign.Start("return", "owner28", 28, id);
        Assert.True(session.BindTrigger(id, trigger));
        Assert.False(session.BindTrigger(id, trigger));
        Assert.False(session.BindTrigger(id, foreign));
        var current = session.Current;
        var rules = session.Rules;
        Assert.False(session.CompleteTrigger(id, trigger, 100)); // WaitingForDisplay.
        Assert.False(session.CompleteTrigger(id, foreign, 100));
        Assert.False(session.Update(100));
        FinishReturnDisplay(trigger);
        Guid call = trigger.CallId;
        Assert.Throws<InvalidDataException>(() => trigger.Start("return", "missing", 28, id));
        Assert.Equal(call, trigger.CallId);
        Assert.Equal(id, trigger.SourceRequestId);
        trigger.Start("return", "owner28", 28, id);
        FinishReturnDisplay(trigger);
        Assert.NotEqual(call, trigger.CallId);
        Assert.False(session.BindTrigger(id, trigger));
        Assert.False(session.CompleteTrigger(id, trigger, 100));
        Assert.Equal(id, session.PendingRequestId);
        Assert.Same(current, session.Current);
        Assert.Same(rules, session.Rules);
        Assert.False(session.Update(1000));

        var lost = VerifiedSession();
        lost.PressSearch(); lost.Update(0);
        var reload = ReturnTrigger(false, reload: true);
        reload.Start("return", "owner28", 28, lost.PendingRequestId);
        Assert.True(lost.BindTrigger(lost.PendingRequestId, reload));
        Assert.Equal(TriggerScriptPhase.WaitingForLostReload, reload.Phase);
        Assert.False(lost.CompleteTrigger(lost.PendingRequestId, reload, 100));
        Assert.False(lost.Update(1000));
    }

    [Fact]
    public void Bound_real_WaitingForBattle_call_cannot_release_or_publish_exploration()
    {
        var (profile, fixture) = PreparedProfile();
        var events = fixture.Events();
        var language = fixture.English();
        var content = fixture.Content();
        var receipt = FirstMandatoryBattleClosure.Verify(profile,
            events, fixture.Chinese(), language, content, Images());
        var session = new FirstMandatoryBattleSession(profile, 2, Rules(), ReturnLeader(), receipt);
        var owner = new RoleLiveState(content.InitialRoles.Values.ToArray());
        var world = DynamicEventWorld.ForHost(session.Rules, session.Current.Leader, 2, 0, 0, owner);
        var random = new ClosureRandom();
        var trigger = new TriggerScriptSession(events, language, content, random, world);
        Assert.True(session.InitializeTrigger(trigger));
        session.PressSearch(); Assert.True(session.Update(0));
        Guid id = session.PendingRequestId;
        trigger.Start("story", "owner28", 28, id);
        Assert.True(session.BindTrigger(id, trigger));
        var display = Assert.IsType<TriggerDialogueRequest>(trigger.PendingDisplay);
        display.Display.Update(24, false, true);
        display.Display.Update(100, true, false);
        Assert.Equal(DialogueDisplayPhase.Completed, display.Display.Current.Phase);
        Assert.True(trigger.AcknowledgeDisplay(display.Id));
        Assert.Equal(TriggerScriptPhase.WaitingForBattle, trigger.Phase);
        var battle = Assert.IsType<TriggerBattleRequest>(trigger.PendingBattle);
        var combat = battle.Lifecycle.Current;
        var pending = session.PendingTrigger;
        var current = session.Current;
        var rules = session.Rules;
        Guid call = trigger.CallId;
        string legacy = System.Text.Json.JsonSerializer.Serialize(session.State.World);
        string actual = System.Text.Json.JsonSerializer.Serialize(world);
        var roles = owner.Roles.ToDictionary(pair => pair.Key, pair => pair.Value);
        string roleWords = System.Text.Json.JsonSerializer.Serialize(owner.Roles);
        int draws = random.Draws;
        session.PressSearch(); session.Press(ExplorationKey.SouthPrimary);
        Assert.False(session.CompleteTrigger(id, trigger, 100));
        Assert.False(session.BindTrigger(id, trigger));
        Assert.False(session.Update(1000));
        Assert.Same(pending, session.PendingTrigger);
        Assert.Equal(id, session.PendingRequestId);
        Assert.Same(current, session.Current);
        Assert.Same(rules, session.Rules);
        Assert.Equal(legacy, System.Text.Json.JsonSerializer.Serialize(session.State.World));
        Assert.Equal(actual, System.Text.Json.JsonSerializer.Serialize(world));
        Assert.Same(owner, world.RoleLiveState);
        foreach (var pair in roles) Assert.Same(pair.Value, owner.Roles[pair.Key]);
        Assert.Equal(roleWords, System.Text.Json.JsonSerializer.Serialize(owner.Roles));
        Assert.Equal(draws, random.Draws);
        Assert.Equal((call, id), (trigger.CallId, trigger.SourceRequestId));
        Assert.Equal(TriggerScriptPhase.WaitingForBattle, trigger.Phase);
        Assert.Same(battle, trigger.PendingBattle);
        Assert.Same(combat, battle.Lifecycle.Current);
    }

    [Fact]
    public void Every_earlier_completed_object_stays_stale_across_four_real_searches()
    {
        var session = VerifiedSession();
        var earlier = new List<(Guid Origin, Guid Call, TriggerScriptSession Trigger)>();
        for (uint search = 0; search < 4; search++)
        {
            uint ticks = search * 250;
            session.PressSearch(); Assert.True(session.Update(ticks));
            Guid id = session.PendingRequestId;
            Assert.NotEqual(Guid.Empty, id);
            var pending = session.PendingTrigger;
            var current = session.Current;
            var rules = session.Rules;
            string legacy = System.Text.Json.JsonSerializer.Serialize(session.State.World);
            foreach (var old in earlier)
            {
                Assert.NotEqual(old.Origin, id);
                Assert.Equal((old.Call, old.Origin), (old.Trigger.CallId, old.Trigger.SourceRequestId));
                Assert.Equal(TriggerScriptPhase.Completed, old.Trigger.Phase);
                Assert.False(session.BindTrigger(id, old.Trigger));
                Assert.False(session.BindTrigger(old.Origin, old.Trigger));
                Assert.False(session.CompleteTrigger(id, old.Trigger, ticks + 1000));
                Assert.False(session.CompleteTrigger(old.Origin, old.Trigger, ticks + 1000));
                Assert.Same(pending, session.PendingTrigger);
                Assert.Equal(id, session.PendingRequestId);
                Assert.Same(current, session.Current);
                Assert.Same(rules, session.Rules);
                Assert.Equal(legacy, System.Text.Json.JsonSerializer.Serialize(session.State.World));
                Assert.False(session.Update(ticks + 100));
            }
            var trigger = ReturnTrigger(false);
            trigger.Start("return", "owner28", 28, id);
            Assert.True(session.BindTrigger(id, trigger));
            Assert.True(session.CompleteTrigger(id, trigger, ticks + 100));
            Assert.False(session.CompleteTrigger(id, trigger, ticks + 1000));
            Assert.False(session.Update(ticks + 149));
            Assert.True(session.Update(ticks + 150)); // Neither replay nor duplicate resets the deadline.
            Assert.Null(session.PendingTrigger);
            Assert.Equal(Guid.Empty, session.PendingRequestId);
            earlier.Add((id, trigger.CallId, trigger));
        }
        Assert.Equal(4, earlier.Count);
    }

    private sealed class ClosureRandom : IBattleRandom
    {
        public int Draws { get; private set; }
        public int NextInclusive(int from, int to) { Draws++; return from; }
        public float NextInclusive(float from, float to) { Draws++; return from; }
    }

    [Fact]
    public void Synchronous_endkeep_and_failed_Start_preserve_owned_completion_and_session_isolation()
    {
        var (profile, fixture) = PreparedProfile();
        var receipt = FirstMandatoryBattleClosure.Verify(profile,
            fixture.Events(), fixture.Chinese(), fixture.English(), fixture.Content(), Images());
        var template = Rules();
        var session = new FirstMandatoryBattleSession(profile, 2, template, ReturnLeader(), receipt);
        var second = new FirstMandatoryBattleSession(profile, 2, template, ReturnLeader(), receipt);
        var untouched = second.Current;
        string original = System.Text.Json.JsonSerializer.Serialize(profile.World);
        session.PressSearch(); session.Update(0);
        Guid id = session.PendingRequestId;
        var trigger = ReturnTrigger(false);
        trigger.Start("return", "owner28", 28, id);
        Assert.Equal(TriggerScriptPhase.Completed, trigger.Phase);
        Assert.True(session.BindTrigger(id, trigger));
        Guid call = trigger.CallId;
        var entry = trigger.StartedEntry;
        Assert.Throws<InvalidDataException>(() => trigger.Start("return", "missing", 28, id));
        Assert.Equal((call, id, entry), (trigger.CallId, trigger.SourceRequestId, trigger.StartedEntry));
        Assert.False(session.CompleteTrigger(Guid.NewGuid(), trigger, 50));
        Assert.True(session.CompleteTrigger(id, trigger, 100));
        Assert.False(session.CompleteTrigger(id, trigger, 500)); // Must not move the return deadline.
        session.Press(ExplorationKey.SouthPrimary);
        Assert.False(session.Update(149));
        Assert.True(session.Update(150));
        Assert.Equal((1248, 256), ((int)session.Current.Leader.X, (int)session.Current.Leader.Y));
        Assert.Same(untouched, second.Current);
        Assert.Equal(Guid.Empty, second.PendingRequestId);
        Assert.Null(second.PendingTrigger);
        Assert.Equal(1264, second.State.World.Leader.X);
        Assert.Equal(template.Objects, second.Objects);
        Assert.Equal(original, System.Text.Json.JsonSerializer.Serialize(profile.World));
    }

    [Theory]
    [InlineData(100u, 149u, 150u, 249u, 250u)]
    [InlineData(100u, 149u, 900u, 999u, 1000u)]
    [InlineData(4294967275u, 28u, 29u, 128u, 129u)]
    public void Return_uses_reference_50ms_then_one_100ms_step_clearing_search_but_retaining_held_alias_priority(
        uint completed, uint early, uint due, uint beforeNext, uint next)
    {
        var session = VerifiedSession();
        session.PressSearch(); Assert.True(session.Update(unchecked(completed - 100)));
        Guid id = session.PendingRequestId;
        var trigger = ReturnTrigger(false);
        trigger.Start("return", "owner28", 28, id);
        Assert.True(session.BindTrigger(id, trigger));
        session.Press(ExplorationKey.WestPrimary);
        session.Press(ExplorationKey.SouthPrimary);
        session.Press(ExplorationKey.SouthAlternate);
        session.Press(ExplorationKey.NorthPrimary, repeat: true);
        session.PressSearch();
        Assert.True(session.CompleteTrigger(id, trigger, completed));
        session.Release(ExplorationKey.SouthPrimary);
        var standing = session.Current;
        Assert.False(session.Update(early));
        Assert.Same(standing, session.Current);
        session.PressSearch(); // Delivered during the return delay must also be cleared.
        Assert.True(session.Update(due));
        Assert.Null(session.PendingTrigger);
        Assert.Equal((1248, 256, 0, 1), ((int)session.Current.Leader.X, (int)session.Current.Leader.Y,
            session.Current.Leader.Direction, session.Current.Leader.StepPhase));
        Assert.Equal((1248, 256), (session.State.World.Leader.X, session.State.World.Leader.Y));
        Assert.False(session.Update(unchecked(due + 1)));
        Assert.False(session.Update(beforeNext));
        session.Release(ExplorationKey.SouthAlternate); // Fall back to the older held west.
        Assert.True(session.Update(next));
        Assert.Equal((1232, 248, 1, 2), ((int)session.Current.Leader.X, (int)session.Current.Leader.Y,
            session.Current.Leader.Direction, session.Current.Leader.StepPhase));
        session.Release(ExplorationKey.WestPrimary);
        Assert.True(session.Update(unchecked(next + 100)));
        Assert.False(session.Current.IsWalking);
        Assert.Equal((1232, 248), ((int)session.Current.Leader.X, (int)session.Current.Leader.Y));
    }

    [Theory]
    [InlineData(17)]
    [InlineData(65535)]
    public void Owned_world_endkeep_retains_the_opaque_source_auto_idle_word(int sourceCounter)
    {
        var context = BattleContextTestFixture.Valid();
        var source = context.Document["snapshot"]!["world"]!["eventObjects"]![0]!;
        source["autoEntry"] = 0;
        source["autoIdleFrameCount"] = sourceCounter;
        var profile = Load(context, prepared: true);
        var fixture = new RealClosureFixture("none");
        var receipt = FirstMandatoryBattleClosure.Verify(profile,
            fixture.Events(), fixture.Chinese(), fixture.English(), fixture.Content(), Images());
        var template = Rules();
        var session = new FirstMandatoryBattleSession(profile, 2, template, ReturnLeader(), receipt);
        var second = new FirstMandatoryBattleSession(profile, 2, template, ReturnLeader(), receipt);
        string original = System.Text.Json.JsonSerializer.Serialize(profile.World);
        var world = DynamicEventWorld.ForHost(session.Rules, session.Current.Leader, 2, 0, 0);
        var trigger = ReturnTrigger(false, world: world);
        Assert.Equal(0, world.Objects.Single().AutoIdleCount); // Not materialized by this route.
        Assert.True(session.InitializeTrigger(trigger));
        session.PressSearch(); Assert.True(session.Update(0));
        Guid id = session.PendingRequestId;
        trigger.Start("return", "owner28", 28, id);
        Assert.Equal(TriggerScriptPhase.Completed, trigger.Phase);
        Assert.True(session.BindTrigger(id, trigger));
        Assert.True(session.CompleteTrigger(id, trigger, 100));
        Assert.Equal(sourceCounter, session.State.World.EventObjects.Single().AutoIdleFrameCount);
        Assert.Equal(0, session.State.World.EventObjects.Single().AutoEntry);
        Assert.Equal(sourceCounter, second.State.World.EventObjects.Single().AutoIdleFrameCount);
        Assert.Null(second.PendingTrigger);
        Assert.Equal(Guid.Empty, second.PendingRequestId);
        Assert.Equal(original, System.Text.Json.JsonSerializer.Serialize(profile.World));
        Assert.Equal((1280, 256, 0x09c3), ((int)template.Objects.Single().X,
            (int)template.Objects.Single().Y, (int)template.Objects.Single().TriggerEntry));
    }

    [Fact]
    public void Initialized_actual_world_follows_movement_before_the_first_Start()
    {
        var session = VerifiedSession();
        var world = DynamicEventWorld.ForHost(session.Rules, session.Current.Leader, 2, 0, 0);
        var trigger = ReturnTrigger(false, world: world);
        Assert.True(session.InitializeTrigger(trigger));
        Assert.False(session.BindTrigger(Guid.NewGuid(), trigger));
        session.Press(ExplorationKey.SouthPrimary);
        Assert.True(session.Update(0));
        Assert.Equal((1248, 256, 0, 1), ((int)world.Leader.X, (int)world.Leader.Y,
            world.Leader.Direction, world.Leader.StepPhase));
        Assert.Equal(session.Current.Leader, world.Leader);
        Assert.Equal(Guid.Empty, trigger.CallId);
        session.Release(ExplorationKey.SouthPrimary);
        session.Press(ExplorationKey.EastPrimary); Assert.True(session.Update(100));
        session.Release(ExplorationKey.EastPrimary);
        session.Press(ExplorationKey.NorthPrimary); Assert.True(session.Update(200));
        Assert.Equal((1264, 264, 2, 0), ((int)world.Leader.X, (int)world.Leader.Y,
            world.Leader.Direction, world.Leader.StepPhase));
        session.PressSearch(); Assert.True(session.Update(300));
        trigger.Start("return", "owner28", 28, session.PendingRequestId);
        Assert.True(session.BindTrigger(session.PendingRequestId, trigger));
        Assert.Equal((1264, 264, 2, 0), ((int)world.Leader.X, (int)world.Leader.Y,
            world.Leader.Direction, world.Leader.StepPhase));
    }

    [Fact]
    public void Owned_world_return_projects_current_scene_words_and_leader_without_global_collision_or_owner_copies()
    {
        var sceneRules = Rules();
        sceneRules = sceneRules.WithObjects(sceneRules.Objects.Select(o => o with
            { Sprite = new SceneSprite(210, [new SceneFrame(0, 1, 1)]) }));
        var session = VerifiedSession(sceneRules);
        var second = VerifiedSession(sceneRules);
        var template = session.Rules;
        var initial = session.Current;
        var extra = new SceneObject(99, 1232, 248, 2, 0, 0, 211, 0, 0, 0,
            new SceneSprite(211, [new SceneFrame(0, 1, 1)])) { TriggerMode = 1, TriggerEntry = 0x09c3 };
        var owner = new RoleLiveState(new RealClosureFixture("none").Content().InitialRoles.Values.ToArray());
        var roles = owner.Roles.ToDictionary(pair => pair.Key, pair => pair.Value);
        var world = DynamicEventWorld.ForHost(template.WithObjects(template.Objects.Append(extra)), initial.Leader, 2, 0, 0, owner);
        var trigger = ReturnTrigger(world: world, prefix:
        [
            WorldCommand("setObjectPosition", 0x13, [28, 1200, 224], ("eventObjectId", 28), ("x", 1200), ("y", 224)),
            WorldCommand("setState", 0x49, [28, 0, 0], ("eventObjectId", 28), ("state", 0)),
            WorldCommand("setPartyPos", 0x46, [39, 15, 0], ("x", 39), ("y", 15), ("h", 0)),
            WorldCommand("setTriggerEntry", 0x25, [28, 0x09c4, 0], ("eventObjectId", 28), ("entry", 0x09c4))
        ]);
        Assert.True(session.InitializeTrigger(trigger));
        session.PressSearch(); Assert.True(session.Update(0));
        Guid id = session.PendingRequestId;
        trigger.Start("return", "owner28", 28, id);
        Assert.True(session.BindTrigger(id, trigger));
        Assert.Same(initial, session.Current); // Script prefix is not yet a published exploration view.
        Assert.False(session.CompleteTrigger(id, trigger, 100));
        FinishReturnDisplay(trigger);
        Assert.True(session.CompleteTrigger(id, trigger, 100));
        Assert.False(session.CompleteTrigger(id, trigger, 500));
        var obj = Assert.Single(session.Objects);
        Assert.Equal((28, 1200, 224, 0, 0x09c3), ((int)obj.Id, (int)obj.X, (int)obj.Y, (int)obj.State, (int)obj.TriggerEntry));
        Assert.Equal((1248, 240, 3), ((int)session.Current.Leader.X, (int)session.Current.Leader.Y, session.Current.Leader.Direction));
        Assert.Equal((1248, 240), (session.State.World.Leader.X, session.State.World.Leader.Y));
        Assert.Equal((1088, 128, 9), (session.Current.Plan.CameraX, session.Current.Plan.CameraY, session.Current.Plan.LeaderFrameIndex));
        Assert.DoesNotContain(session.Current.Plan.Commands, item => item.Source == SceneDrawSource.Object);
        Assert.False(session.Rules.IsBlocked(1232, 248));
        Assert.Null(session.Rules.FindSearchTarget(session.Current.Leader with { Direction = 0 }));
        Assert.Equal(2, world.Objects.Count);
        Assert.Equal((1232, 248, 2), (world.Objects[1].X, world.Objects[1].Y, world.Objects[1].State));
        var legacy = session.State.World.EventObjects.Single(item => item.OwnerId == 28);
        Assert.Equal((1200, 224, 0, 0x09c3), (legacy.X, legacy.Y, legacy.State, legacy.TriggerEntry));
        Assert.Equal(second.State.World.EventObjects.Length, session.State.World.EventObjects.Length);
        Assert.Same(template.Objects[0].Sprite, obj.Sprite);
        Assert.Same(initial.Leader.Sprite, session.Current.Leader.Sprite);
        Assert.Equal((1280, 256, 2), ((int)template.Objects[0].X, (int)template.Objects[0].Y, (int)template.Objects[0].State));
        Assert.Equal((1280, 256, 2), ((int)second.Objects[0].X, (int)second.Objects[0].Y, (int)second.Objects[0].State));
        Assert.Same(owner, world.RoleLiveState);
        foreach (var pair in roles) Assert.Same(pair.Value, owner.Roles[pair.Key]);
        session.PressSearch(); session.Press(ExplorationKey.SouthPrimary);
        Assert.False(session.Update(149));
        Assert.True(session.Update(150));
        Assert.Null(session.PendingTrigger);
        Assert.Equal((1232, 248), ((int)world.Leader.X, (int)world.Leader.Y));
        Assert.Equal(world.Leader, session.Current.Leader);
    }

    [Fact]
    public void Initialization_rejects_incompatible_started_duplicate_and_late_contexts_atomically()
    {
        var session = VerifiedSession();
        var initial = session.Current;
        string legacy = System.Text.Json.JsonSerializer.Serialize(session.State.World);
        Assert.Throws<ArgumentNullException>(() => session.InitializeTrigger(null!));
        Assert.False(session.InitializeTrigger(ReturnTrigger(false)));
        foreach (var world in new[]
        {
            DynamicEventWorld.ForHost(session.Rules, initial.Leader, 4, 0, 0),
            DynamicEventWorld.ForHost(session.Rules.WithObjects([]), initial.Leader, 2, 0, 0),
            DynamicEventWorld.ForHost(session.Rules, initial.Leader with { X = 1248 }, 2, 0, 0),
            DynamicEventWorld.ForHost(session.Rules.WithObjects(session.Objects.Select(o => o with { SpriteNumber = 211 })), initial.Leader, 2, 0, 0)
        })
        {
            var leader = world.Leader;
            Assert.False(session.InitializeTrigger(ReturnTrigger(false, world: world)));
            Assert.Same(leader, world.Leader);
            Assert.Same(initial, session.Current);
            Assert.Equal(legacy, System.Text.Json.JsonSerializer.Serialize(session.State.World));
        }
        var correctWorld = DynamicEventWorld.ForHost(session.Rules, ReturnLeader(), 2, 0, 0);
        var wrongEntry = ReturnTrigger(false, address: 0x09c4, world: correctWorld);
        Assert.False(session.InitializeTrigger(wrongEntry));
        var started = ReturnTrigger(false, world: correctWorld);
        started.Start("return", "owner28", 28);
        Assert.False(session.InitializeTrigger(started));
        var correct = ReturnTrigger(false, world: correctWorld);
        Assert.True(session.InitializeTrigger(correct)); // Independently cloned valid sprite metadata is compatible.
        Assert.False(session.InitializeTrigger(correct));
        var foreignWorld = DynamicEventWorld.ForHost(session.Rules, initial.Leader, 2, 0, 0);
        Assert.False(session.InitializeTrigger(ReturnTrigger(false, world: foreignWorld)));
        session.Press(ExplorationKey.SouthPrimary); Assert.True(session.Update(0));
        Assert.Equal((1248, 256), ((int)correctWorld.Leader.X, (int)correctWorld.Leader.Y));
        Assert.Same(initial.Leader, foreignWorld.Leader);
        var late = VerifiedSession();
        var lateWorld = DynamicEventWorld.ForHost(late.Rules, late.Current.Leader, 2, 0, 0);
        Assert.True(late.Update(0));
        Assert.False(late.InitializeTrigger(ReturnTrigger(false, world: lateWorld)));
        var pending = VerifiedSession();
        var pendingWorld = DynamicEventWorld.ForHost(pending.Rules, pending.Current.Leader, 2, 0, 0);
        pending.PressSearch(); Assert.True(pending.Update(0));
        var uninitialized = ReturnTrigger(false, world: pendingWorld);
        Assert.False(pending.InitializeTrigger(uninitialized));
        uninitialized.Start("return", "owner28", 28, pending.PendingRequestId);
        Assert.False(pending.BindTrigger(pending.PendingRequestId, uninitialized));
        Assert.False(pending.CompleteTrigger(pending.PendingRequestId, uninitialized, 100));
    }

    [Fact]
    public void Initialized_context_rejects_foreign_world_and_restarted_completion_without_publishing_prefix()
    {
        var session = VerifiedSession();
        var world = DynamicEventWorld.ForHost(session.Rules, session.Current.Leader, 2, 0, 0);
        var trigger = ReturnTrigger(world: world, prefix:
            [WorldCommand("setObjectPosition", 0x13, [28, 1200, 224], ("eventObjectId", 28), ("x", 1200), ("y", 224))]);
        Assert.True(session.InitializeTrigger(trigger));
        session.PressSearch(); Assert.True(session.Update(0));
        Guid id = session.PendingRequestId;
        var current = session.Current;
        var rules = session.Rules;
        var pending = session.PendingTrigger;
        string legacy = System.Text.Json.JsonSerializer.Serialize(session.State.World);
        var foreignWorld = DynamicEventWorld.ForHost(session.Rules, session.Current.Leader, 2, 0, 0);
        var foreign = ReturnTrigger(false, world: foreignWorld);
        foreign.Start("return", "owner28", 28, id);
        Assert.False(session.BindTrigger(id, foreign));
        var sameWorldForeign = ReturnTrigger(false, world: world);
        sameWorldForeign.Start("return", "owner28", 28, id);
        Assert.False(session.BindTrigger(id, sameWorldForeign));
        trigger.Start("return", "owner28", 28, id);
        Assert.False(session.CompleteTrigger(id, trigger, 100)); // unbound
        Assert.False(session.BindTrigger(Guid.NewGuid(), trigger));
        Assert.True(session.BindTrigger(id, trigger));
        Assert.False(session.CompleteTrigger(id, trigger, 100)); // unfinished display
        FinishReturnDisplay(trigger);
        string actual = System.Text.Json.JsonSerializer.Serialize(world);
        Assert.False(session.CompleteTrigger(id, foreign, 100));
        Assert.False(session.CompleteTrigger(Guid.NewGuid(), trigger, 100));
        trigger.Start("return", "owner28", 28, id); FinishReturnDisplay(trigger);
        Assert.False(session.CompleteTrigger(id, trigger, 500)); // same session, a different actual CallId
        Assert.Same(current, session.Current);
        Assert.Same(rules, session.Rules);
        Assert.Same(pending, session.PendingTrigger);
        Assert.Equal(id, session.PendingRequestId);
        Assert.Equal(legacy, System.Text.Json.JsonSerializer.Serialize(session.State.World));
        Assert.Equal(actual, System.Text.Json.JsonSerializer.Serialize(world));
        Assert.False(session.Update(1000));
        Assert.Equal((1200, 224), (world.Objects[0].X, world.Objects[0].Y));
        Assert.Equal((1280, 256), ((int)session.Objects[0].X, (int)session.Objects[0].Y));
    }

    [Fact]
    public void Returned_object_position_controls_collision_and_the_next_owned_search()
    {
        var session = VerifiedSession();
        var world = DynamicEventWorld.ForHost(session.Rules, session.Current.Leader, 2, 0, 0);
        var trigger = ReturnTrigger(false, world: world, prefix:
            [WorldCommand("setObjectPosition", 0x13, [28, 1248, 256], ("eventObjectId", 28), ("x", 1248), ("y", 256))]);
        Assert.True(session.InitializeTrigger(trigger));
        session.PressSearch(); Assert.True(session.Update(0));
        Guid id = session.PendingRequestId;
        trigger.Start("return", "owner28", 28, id);
        Assert.True(session.BindTrigger(id, trigger));
        Assert.True(session.CompleteTrigger(id, trigger, 100));
        Assert.Null(session.Rules.FindSearchTarget(session.Current.Leader)); // old east-facing search is now empty
        Assert.True(session.Rules.IsBlocked(1248, 256));
        session.Press(ExplorationKey.SouthPrimary);
        Assert.True(session.Update(150));
        Assert.False(session.Current.IsWalking);
        Assert.Equal((1264, 248, 0), ((int)world.Leader.X, (int)world.Leader.Y, world.Leader.Direction));
        session.Release(ExplorationKey.SouthPrimary); session.PressSearch();
        Assert.True(session.Update(250));
        Assert.Equal(new FirstMandatoryBattleTriggerRequest(0x09c3, 28), session.PendingTrigger);
        Assert.NotEqual(id, session.PendingRequestId);
        Assert.False(session.CompleteTrigger(id, trigger, 250));
    }

    [Fact]
    public void Invalid_return_plan_is_rejected_before_any_exploration_or_legacy_publication()
    {
        var session = VerifiedSession();
        var world = DynamicEventWorld.ForHost(session.Rules, session.Current.Leader, 2, 0, 0);
        var trigger = ReturnTrigger(false, world: world, prefix:
            [WorldCommand("setPartyFacing", 0x15, [9, 0, 0], ("direction", 9), ("gesture", 0), ("memberIndex", 0))]);
        Assert.True(session.InitializeTrigger(trigger));
        session.PressSearch(); Assert.True(session.Update(0));
        Guid id = session.PendingRequestId;
        trigger.Start("return", "owner28", 28, id);
        Assert.True(session.BindTrigger(id, trigger));
        var current = session.Current;
        var rules = session.Rules;
        var pending = session.PendingTrigger;
        string legacy = System.Text.Json.JsonSerializer.Serialize(session.State.World);
        string actual = System.Text.Json.JsonSerializer.Serialize(world);
        Assert.False(session.CompleteTrigger(id, trigger, 100));
        Assert.Same(current, session.Current);
        Assert.Same(rules, session.Rules);
        Assert.Same(pending, session.PendingTrigger);
        Assert.Equal(id, session.PendingRequestId);
        Assert.Equal(legacy, System.Text.Json.JsonSerializer.Serialize(session.State.World));
        Assert.Equal(actual, System.Text.Json.JsonSerializer.Serialize(world));
        Assert.False(session.Update(1000));
    }

    private static JsonObject WorldCommand(string op, int opcode, int[] operands, params (string Name, int Value)[] fields)
    {
        var command = new JsonObject { ["op"] = op,
            ["source"] = new JsonObject { ["opcode"] = opcode,
                ["operands"] = new JsonArray(operands.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()) } };
        foreach (var field in fields) command[field.Name] = field.Value;
        return command;
    }

    private static FirstMandatoryBattleSession VerifiedSession(StaticSceneRules? rules = null)
    {
        var (profile, fixture) = PreparedProfile();
        var receipt = FirstMandatoryBattleClosure.Verify(profile,
            fixture.Events(), fixture.Chinese(), fixture.English(), fixture.Content(), Images());
        return new(profile, 2, rules ?? Rules(), ReturnLeader(), receipt);
    }

    private static StaticLeader ReturnLeader() => Leader() with
    {
        Sprite = new SceneSprite(17, Enumerable.Range(0, 12).Select(i => new SceneFrame(i, 1, 1)))
    };

    // Original selected modern stream isolates outer-call release, not the battle engine.
    private static TriggerScriptSession ReturnTrigger(bool dialogue = true, int owner = 28,
        string ownerType = "eventObject", string slot = "wTriggerScript", int address = 0x09c3,
        bool reload = false, DynamicEventWorld? world = null, JsonObject[]? prefix = null)
    {
        const int count = 0x09d0;
        var commands = new JsonArray();
        for (int i = 0; i < count; i++)
        {
            bool show = dialogue && i == address + (prefix?.Length ?? 0);
            var command = new JsonObject
            {
                ["label"] = $"L{i:X4}", ["op"] = show ? "showDialog" : reload && i == address ? "requestReload" : "end",
                ["next"] = i + 1 < count ? $"L{i + 1:X4}" : null,
                ["source"] = new JsonObject { ["index"] = i, ["opcode"] = show ? 65535 : reload && i == address ? 78 : 0,
                    ["operands"] = new JsonArray(show ? 10 : 0, 0, 0) }
            };
            if (show) { command["messageId"] = 10; command["descriptionLineWord"] = 0; }
            else if (!(reload && i == address)) command["mode"] = "keep";
            if (prefix is not null && i >= address && i < address + prefix.Length)
            {
                command = (JsonObject)prefix[i - address].DeepClone();
                command["label"] = $"L{i:X4}";
                command["next"] = $"L{i + 1:X4}";
                command["source"]!["index"] = i;
            }
            commands.Add(command);
        }
        var index = new JsonObject
        {
            ["formatVersion"] = 1, ["profile"] = "faithful",
            ["source"] = new JsonObject { ["archive"] = "SSS.MKF", ["chunk"] = 4, ["byteOrder"] = "little",
                ["instructionCount"] = count, ["byteLength"] = count * 8, ["sha256"] = new string('c', 64) },
            ["files"] = new JsonArray("return")
        };
        var file = new JsonObject
        {
            ["formatVersion"] = 1, ["profile"] = "faithful", ["fileId"] = "return",
            ["entries"] = new JsonArray(new JsonObject { ["id"] = "owner28",
                ["owner"] = new JsonObject { ["type"] = ownerType, ["id"] = owner }, ["slot"] = slot,
                ["kind"] = "trigger", ["target"] = $"L{address:X4}", ["sourceAddress"] = address }),
            ["commands"] = commands
        };
        var events = TriggerEventCollection.Parse(index.ToJsonString(),
            new Dictionary<string, string> { ["return"] = file.ToJsonString() });
        var language = new DialogueLanguagePackage("en", [new DialogueMessageBlock(10, 10, [DialogueMessagePart.FromText("AB")])]);
        return world is null ? new(events, language) : new(events, language, world);
    }

    private static void FinishReturnDisplay(TriggerScriptSession trigger)
    {
        var display = Assert.IsType<TriggerDialogueRequest>(trigger.PendingDisplay);
        display.Display.Update(24, false, true);
        display.Display.Update(100, true, false);
        Assert.Equal(DialogueDisplayPhase.Completed, display.Display.Current.Phase);
        Assert.True(trigger.AcknowledgeDisplay(display.Id));
        Assert.Equal(TriggerScriptPhase.Completed, trigger.Phase);
    }

    private static (FirstMandatoryBattleProfile Profile, RealClosureFixture Fixture) PreparedProfile(string fault = "none") =>
        (Load(BattleContextTestFixture.Valid(), prepared: true), new RealClosureFixture(fault));

    private static (FirstMandatoryBattleProfile Profile, RealClosureFixture Fixture) ContractOnlyProfile() =>
        (Load(BattleContextTestFixture.Valid(), prepared: false), new RealClosureFixture("none"));

    private static FirstMandatoryBattleProfile Load(BattleContextTestFixture context, bool prepared)
    {
        context.Document["snapshot"]!["world"]!["leader"]!["x"] = 1264;
        context.Document["snapshot"]!["world"]!["leader"]!["y"] = 248;
        context.Document["snapshot"]!["world"]!["searchProof"]!["sampleIndex"] = 1;
        if (prepared)
            foreach (var node in context.Document["closure"]!["entries"]!.AsArray())
                node!["status"] = "prepared";
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(context.Document.ToJsonString());
        string hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
        byte[] index = System.Text.Encoding.UTF8.GetBytes(
            "{\"formatVersion\":1,\"profile\":\"first-mandatory-battle-pre-trigger-v1\",\"contextFile\":\"battle-context.json\",\"contextSha256\":\"" + hash + "\"}");
        return FirstMandatoryBattleProfile.Load(index, new Dictionary<string, byte[]> { ["battle-context.json"] = bytes });
    }

    internal static StaticSceneRules Rules()
    {
        var owner = new SceneObject(28, 1280, 256, 2, 0, 0, 210, 0, 0, 0) { TriggerMode = 1, TriggerEntry = 0x09c3 };
        return new StaticSceneRules(new SceneMap(12, new uint[64 * 128 * 2], [new SceneFrame(0, 1, 1)]), [owner]);
    }

    internal static StaticLeader Leader() => new(1264, 248, 3, 0, 0, 0,
        new SceneSprite(17, [new SceneFrame(0, 1, 1), new SceneFrame(9, 1, 1)]));

    internal static FirstMandatoryBattleImageClosure Images(bool wrong = false) => wrong
        ? new(96, "ABC96", ["F0", "F1"], 24, "FBP24", true)
        : new(97, "ABC97", ["F0", "F1"], 24, "FBP24", true);
}

/// <summary>Original synthetic stream carrying the frozen real identities (owner28/@09C3, @09FE
/// team18 operands (0012,A073,0000), Won @09FF, dynamic auto @0A36/@0A3B, lost @A073→0x004E,
/// role1 dying @A96E-@A987 and role0 friend-death @A9B5-@A9D1 with the retail four-band random
/// structure). Original text and values; not retail data.</summary>
internal sealed class RealClosureFixture(string fault)
{
    private const int Entry = 0x09c3, PreDialog = 0x09c4, Battle = 0x09fe, Won = 0x09ff, WonEnd = 0x0a32,
        AutoStart = 0x0a36, AutoEnd = 0x0a3b, Lost = 0xa073, LostDialog = 0xa074,
        Dying = 0xa96e, FriendDeath = 0xa9b5;
    private const int InstructionCount = 0xa9d2;

    public TriggerEventCollection Events() => TriggerEventCollection.Parse(Index.ToJsonString(),
        new Dictionary<string, string> { ["story"] = EventFile.ToJsonString() });

    public DialogueLanguagePackage Chinese() => Language("zh-Hans", includeFirst: true);
    public DialogueLanguagePackage English() => Language("en", includeFirst: fault != "missing-english-message");

    private static DialogueLanguagePackage Language(string id, bool includeFirst)
    {
        // Single-message blocks plus the consecutive-show pair blocks the dying bands read.
        var singles = Enumerable.Range(10, 15).Where(message => includeFirst || message != 10)
            .Select(message => new DialogueMessageBlock(message, message, [DialogueMessagePart.FromText(id + message)]));
        var pairs = Enumerable.Range(0, 4).Select(band => new DialogueMessageBlock(13 + band * 2, 14 + band * 2,
            [DialogueMessagePart.FromText(id + "p" + band)]));
        return new(id, singles.Concat(pairs));
    }

    public TriggerBattleContent Content()
    {
        int[] role0 = [196, 225, 208, 166, 235, 249], role1 = [197, 0, 210, 170, 237, 248];
        BattleRoleSnapshot[] roles = [Role(0, 1, 150, 150, 100, 100, 2, role0), Role(1, 5, 28, 240, 10, 240, 0, role1)];
        ClassicBattlePlayer[] players =
        [
            new(0, 150, 150, 40, 20, 100) { Equipment = role0, Statuses = new int[9], Poisons = [0],
                FriendDeathScript = 0xA9B5, CoveredByRoleId = 2 },
            new(1, 28, 240, 20, 0, 100) { Equipment = role1, Statuses = new int[9], Poisons = [0],
                DyingScript = fault == "missing-dying-hook" ? 0 : 0xA96E, CoveredByRoleId = 0 }
        ];
        var growth = new Dictionary<int, ClassicBattleGrowthData>
        {
            [0] = new(Enumerable.Repeat(100, 100).ToArray(), []),
            [1] = new(Enumerable.Repeat(100, 100).ToArray(), [])
        };
        var enemy = new ClassicBattleEnemy(7, 0, 43, 8, 0, 500, 1) { Statuses = new int[9], Poisons = [0] };
        List<ClassicBattleEnemy> enemies = fault == "single-enemy" ? [enemy] : [enemy, enemy with { Id = 8 }];
        List<IReadOnlyList<int>>? grid = fault == "undeclared-equipment-grid" ? null
            : [role0, role1, new int[6], new int[6], new int[6], new int[6]];
        List<BattleEquipmentObject> objects = grid is null ? [] : role0.Concat(role1)
            .Where(id => id != 0 && (fault != "missing-dual-attack-object" || id != 170)).Distinct()
            .Select(id => new BattleEquipmentObject(id, Effects(id))).ToList();
        return new TriggerBattleContent(roles, players, growth,
            new Dictionary<int, IReadOnlyList<ClassicBattleEnemy>> { [18] = enemies }, grid, objects);
    }

    private static IReadOnlyList<BattleEquipmentEffect> Effects(int objectId) => Enumerable.Range(0, 6).Select(role =>
        objectId == 170 && role == 1
            ? new BattleEquipmentEffect(0, 0, 0, 0, [0, 0, 0, 0, 0, 0, 0, 0, 32760])
            : new BattleEquipmentEffect(0, 0, 0, 0, new int[9])).ToList();

    private static BattleRoleSnapshot Role(int id, int level, int hp, int maxHp, int mp, int maxMp, int coveredBy, int[] equipment) => new(
        id, level, hp, maxHp, mp, maxMp, new(0, level, 0),
        new Dictionary<string, BattleExperienceSnapshot>
        {
            ["health"] = new(0, level, 0), ["magic"] = new(0, level, 0), ["attack"] = new(0, level, 0),
            ["magicPower"] = new(0, level, 0), ["defense"] = new(0, level, 0), ["dexterity"] = new(0, level, 0), ["flee"] = new(0, level, 0)
        }, equipment, new int[32], new int[9], coveredBy);

    public JsonObject Index { get; } = new()
    {
        ["formatVersion"] = 1, ["profile"] = "faithful",
        ["source"] = new JsonObject { ["archive"] = "SSS.MKF", ["chunk"] = 4, ["byteOrder"] = "little",
            ["instructionCount"] = InstructionCount, ["byteLength"] = InstructionCount * 8,
            ["sha256"] = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc" },
        ["files"] = new JsonArray("story")
    };

    private JsonObject? file;
    public JsonObject EventFile => file ??= BuildFile();

    private static void Put(JsonArray commands, int address, string op, JsonNode? next, int opcode, JsonArray operands,
        params (string Key, JsonNode? Value)[] fields)
    {
        var command = new JsonObject { ["label"] = $"L{address:X4}", ["op"] = op, ["next"] = next };
        foreach (var (key, value) in fields) command[key] = value;
        command["source"] = new JsonObject { ["index"] = address, ["opcode"] = opcode, ["operands"] = operands.DeepClone() };
        commands[address] = command;
    }

    private static JsonNode? NextOf(int address) => address + 1 < InstructionCount ? $"L{address + 1:X4}" : null;

    private JsonObject BuildFile()
    {
        // The strict collection covers every source address exactly once and every next is the
        // WORD-order successor; the unused retail-range rows stay retained raw data, like the
        // real export's unverified opcodes.
        var commands = new JsonArray();
        for (int address = 0; address < InstructionCount; address++)
            commands.Add(new JsonObject { ["label"] = $"L{address:X4}", ["op"] = "raw", ["next"] = NextOf(address),
                ["source"] = new JsonObject { ["index"] = address, ["opcode"] = 134, ["operands"] = new JsonArray(0, 65535, 8) } });
        JsonArray Words(int a, int b, int c) => new(a, b, c);
        (int team, int loss, int boss) = fault == "wrong-operands" ? (18, 0, 0) : (0x0012, 0xa073, 0);
        void Idle(int from, int to)
        {
            for (int address = from; address <= to; address++)
                Put(commands, address, "face", NextOf(address), 15, Words(0, 0, 0), ("direction", 0), ("frame", 0));
        }

        Put(commands, Entry, "setDialog", NextOf(Entry), 60, Words(0, 0, 0),
            ("box", "upper"), ("portraitId", 0), ("color", 0), ("rngWord", 0));
        Put(commands, PreDialog, "showDialog", NextOf(PreDialog), 65535, Words(10, 0, 0),
            ("messageId", 10), ("descriptionLineWord", 0));
        Idle(PreDialog + 1, Battle - 1);
        Put(commands, Battle, "startBattle", NextOf(Battle), 7, Words(team, loss, boss),
            ("enemyTeamId", 18), ("lossTarget", $"L{Lost:X4}"), ("isBoss", true), ("fleeTarget", null));
        Put(commands, Won, "showDialog", NextOf(Won), 65535, Words(11, 0, 0),
            ("messageId", 11), ("descriptionLineWord", 0));
        Idle(Won + 1, WonEnd - 1);
        Put(commands, WonEnd, "end", NextOf(WonEnd), 0, Words(0, 0, 0), ("mode", "keep"));
        if (fault != "missing-dynamic-auto")
        {
            Put(commands, AutoStart, "face", NextOf(AutoStart), 15, Words(1, 0, 0), ("direction", 1), ("frame", 0));
            Put(commands, AutoEnd, "walkStep", NextOf(AutoEnd), 11, Words(0, 0, 0), ("direction", 0));
        }
        Put(commands, Lost, "setDialog", NextOf(Lost), 61, Words(0, 0, 0),
            ("box", "lower"), ("portraitId", 0), ("color", 0), ("rngWord", 0));
        Put(commands, LostDialog, "showDialog", NextOf(LostDialog), 65535, Words(12, 0, 0),
            ("messageId", 12), ("descriptionLineWord", 0));
        Put(commands, LostDialog + 1, "requestReload", NextOf(LostDialog + 1), 0x4e, Words(0, 0, 0));

        Hook(commands, Dying, 13, dying: true);
        Hook(commands, FriendDeath, 17, dying: false);
        return new JsonObject
        {
            ["formatVersion"] = 1, ["profile"] = "faithful", ["fileId"] = "story",
            ["entries"] = new JsonArray(new JsonObject { ["id"] = "owner28",
                ["owner"] = new JsonObject { ["type"] = "eventObject", ["id"] = 28 },
                ["slot"] = "wTriggerScript", ["kind"] = "trigger", ["target"] = $"L{Entry:X4}", ["sourceAddress"] = Entry }),
            ["commands"] = commands
        };
    }

    /// <summary>Four random bands inside the frozen WORD range: the dying hook shows dialogue, the
    /// friend-death hook restores or boosts the caller (role-hook-only verbs).</summary>
    private static void Hook(JsonArray commands, int start, int firstMessage, bool dying)
    {
        int[] sizes = dying ? [4, 4, 4, 4] : [4, 4, 5, 5];
        int[] bandStart = new int[4];
        int address = start + 3;
        for (int band = 0; band < 4; band++) { bandStart[band] = address; address += sizes[band]; }
        JsonArray Words(int a, int b, int c) => new(a, b, c);

        Put(commands, start, "randomJump", NextOf(start), 6, Words(75, bandStart[3], 0), ("rate", 75), ("to", $"L{bandStart[3]:X4}"));
        Put(commands, start + 1, "randomJump", NextOf(start + 1), 6, Words(66, bandStart[2], 0), ("rate", 66), ("to", $"L{bandStart[2]:X4}"));
        Put(commands, start + 2, "randomJump", NextOf(start + 2), 6, Words(50, bandStart[1], 0), ("rate", 50), ("to", $"L{bandStart[1]:X4}"));

        for (int band = 0; band < 4; band++)
        {
            int at = bandStart[band], message = firstMessage + band * 2, size = sizes[band];
            Put(commands, at, "setDialog", NextOf(at), band % 2 == 0 ? 60 : 61, Words(0, 0, 0),
                ("box", band % 2 == 0 ? "upper" : "lower"), ("portraitId", 0), ("color", 0), ("rngWord", 0));
            Put(commands, at + 1, "showDialog", NextOf(at + 1), 65535, Words(message, 0, 0),
                ("messageId", message), ("descriptionLineWord", 0));
            if (dying)
                Put(commands, at + 2, "showDialog", NextOf(at + 2), 65535, Words(message + 1, 0, 0),
                    ("messageId", message + 1), ("descriptionLineWord", 0));
            else
                Put(commands, at + 2, band % 2 == 0 ? "changeHp" : "changeMp", NextOf(at + 2), band % 2 == 0 ? 27 : 28,
                    Words(0, 9999, 0), ("scope", 0), ("amount", 9999));
            if (!dying && band >= 2)
                Put(commands, at + 3, "boostStat", NextOf(at + 3), 48, Words(17, 25, 0),
                    ("field", 17), ("percent", 25), ("roleOperand", 0));
            Put(commands, at + size - 1, "end", NextOf(at + size - 1), 0, Words(0, 0, 0), ("mode", "keep"));
        }
    }
}
