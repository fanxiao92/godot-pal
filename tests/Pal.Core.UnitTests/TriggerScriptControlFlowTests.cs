using System.Text.Json.Nodes;
using Pal.Core;
using Xunit;

namespace Pal.Core.Tests;

public class TriggerScriptControlFlowTests
{
    [Fact]
    public void Call_runs_the_callee_then_returns_to_the_outer_continuation_with_reference_caller_semantics()
    {
        var script = new TriggerScriptSession(CallFixture().Load(), Language());

        script.Start("shared", "npc", 60);

        // script.c 0x0004: operand1 = 61 is a literal caller word, so the callee starts as 61,
        // the last event object advances to it and the entry resets g_fScriptSuccess.
        var callee = Assert.IsType<TriggerDialogueRequest>(script.PendingDisplay);
        Assert.Equal(TriggerScriptPhase.WaitingForDisplay, script.Phase);
        Assert.Equal(61, script.Caller);
        Assert.Equal(62, Assert.Single(callee.MessageReferences).StartMessageId);
        Assert.Equal(59, callee.Context.PortraitId);
        Assert.True(script.ScriptSuccess);
        Assert.Equal(1, script.ReturnEntry);

        // Word zero inherits the current caller (61), never the root caller.
        Advance(callee);
        Assert.True(script.AcknowledgeDisplay(callee.Id));
        var nested = Assert.IsType<TriggerDialogueRequest>(script.PendingDisplay);
        Assert.Equal(61, script.Caller);
        Assert.Equal(63, Assert.Single(nested.MessageReferences).StartMessageId);
        Assert.True(script.ScriptSuccess);

        // The nested callee ends, the callee fails and ends with 0x0001; the caller resumes at its
        // own successor with its own caller word. A callee's return entry never replaces it.
        Advance(nested);
        Assert.True(script.AcknowledgeDisplay(nested.Id));
        var outer = Assert.IsType<TriggerDialogueRequest>(script.PendingDisplay);
        Assert.Equal(60, script.Caller);
        Assert.Equal(61, Assert.Single(outer.MessageReferences).StartMessageId);
        Assert.Equal(59, outer.Context.PortraitId);
        Assert.False(script.ScriptSuccess);
        Assert.Equal(1, script.ReturnEntry);
        Assert.Equal(new TriggerPosition("shared", "L0002", 2), Assert.Single(outer.MessageReferences).Position);

        Advance(outer);
        Assert.True(script.AcknowledgeDisplay(outer.Id));
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
        Assert.Equal(new TriggerPosition("shared", "L0003", 3), script.CurrentPosition);
        Assert.Equal(1, script.ReturnEntry);
        Assert.Equal(60, script.Caller);
        Assert.Null(script.PendingDisplay);

        // wLastEventObject is not restored on return: an inherited start uses the deepest callee word.
        script.Start("shared", "npc", 65535);
        Assert.Equal(61, script.Caller);
        Assert.Equal(62, Assert.Single(script.PendingDisplay!.MessageReferences).StartMessageId);
    }

    [Fact]
    public void Ordinary_call_WORD_zero_restores_outer_continuation_and_allows_a_fresh_call()
    {
        // script.c 0x0004: operand1 zero passes the current wEventObjectID to the callee; when the
        // callee ends, the caller continues at its own successor with its own caller word.
        var fixture = Layout(8);
        Set(fixture, 1, 4, """{"op":"call","to":"L0005","eventObjectId":0}""", 7);
        Set(fixture, 2, 0xFFFF, """{"op":"showDialog","messageId":61,"descriptionLineWord":0}""", 7);
        Set(fixture, 3, 0, """{"op":"end","mode":"keep"}""", 7);
        Set(fixture, 5, 0xFFFF, """{"op":"showDialog","messageId":62,"descriptionLineWord":0}""", 7);
        Set(fixture, 6, 1, """{"op":"end","mode":"advance"}""", 7);
        var language = Text((61, "outer"), (62, "callee"));
        var script = new TriggerScriptSession(fixture.Load(), language);

        for (int call = 0; call < 2; call++)
        {
            script.Start("shared", "npc", 60);
            Assert.Equal(TriggerScriptPhase.WaitingForDisplay, script.Phase);
            Assert.Equal(60, script.Caller);
            var callee = Assert.IsType<TriggerDialogueRequest>(script.PendingDisplay);
            Assert.Equal(62, Assert.Single(callee.MessageReferences).StartMessageId);
            Advance(callee);
            Assert.True(script.AcknowledgeDisplay(callee.Id));
            var outer = Assert.IsType<TriggerDialogueRequest>(script.PendingDisplay);
            Assert.Equal(61, Assert.Single(outer.MessageReferences).StartMessageId);
            Assert.Equal(60, script.Caller);
            Advance(outer);
            Assert.True(script.AcknowledgeDisplay(outer.Id));
            Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
            Assert.Equal(1, script.ReturnEntry); // the callee's 0x0001 stays in its own frame
            Assert.Equal(60, script.Caller);
        }
    }

    [Fact]
    public void Zero_target_calls_apply_entry_globals_return_one_frame_and_preserve_last_caller_for_the_next_root_start()
    {
        // PAL_RunTriggerScript(0, literal): the loop never runs but the entry still resets
        // g_fScriptSuccess/wLastEventObject and restores the default dialog delay.
        var fixture = Layout(8);
        Set(fixture, 1, 60, """{"op":"setDialog","box":"upper","portraitId":0,"color":0,"rngWord":0}""", 7);
        Set(fixture, 2, 0xFFFF, """{"op":"showDialog","messageId":61,"descriptionLineWord":0}""", 7);
        Set(fixture, 3, 4, """{"op":"call","to":"L0000","eventObjectId":7}""", 7);
        Set(fixture, 4, 0, """{"op":"end","mode":"keep"}""", 7);
        var script = new TriggerScriptSession(fixture.Load(), Text((61, "A'$12")));

        script.Start("shared", "npc", 60);
        var display = Assert.IsType<TriggerDialogueRequest>(script.PendingDisplay);
        Assert.Equal(136, display.Display.DelayMilliseconds);
        Advance(display);
        Assert.True(script.AcknowledgeDisplay(display.Id));
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
        Assert.Equal(60, script.Caller); // one frame, restored to the outer caller
        Assert.Equal(1, script.ReturnEntry);
        Assert.True(script.ScriptSuccess);
        Assert.Equal(24, script.Context.DelayMilliseconds); // the entry restored the default speed
        Assert.Equal(new TriggerPosition("shared", "L0004", 4), script.CurrentPosition);

        // wLastEventObject keeps the literal zero-target caller for the next root start.
        script.Start("shared", "npc", 65535);
        Assert.Equal(7, script.Caller);
        Assert.IsType<TriggerDialogueRequest>(script.PendingDisplay);
    }

    [Fact]
    public void Callee_advance_return_does_not_replace_outer_continuation_or_root_return_entry()
    {
        var fixture = Layout(8);
        Set(fixture, 1, 4, """{"op":"call","to":"L0005","eventObjectId":0}""", 7);
        Set(fixture, 2, 0xFFFF, """{"op":"showDialog","messageId":61,"descriptionLineWord":0}""", 7);
        Set(fixture, 3, 0, """{"op":"end","mode":"keep"}""", 7);
        Set(fixture, 5, 0xFFFF, """{"op":"showDialog","messageId":62,"descriptionLineWord":0}""", 7);
        Set(fixture, 6, 1, """{"op":"end","mode":"advance"}""", 7);
        var script = new TriggerScriptSession(fixture.Load(), Text((61, "outer"), (62, "callee")));

        script.Start("shared", "npc", 60);
        Assert.Equal(1, script.ReturnEntry);
        var callee = Assert.IsType<TriggerDialogueRequest>(script.PendingDisplay);
        Advance(callee);
        Assert.True(script.AcknowledgeDisplay(callee.Id));
        Assert.Equal(1, script.ReturnEntry); // 0x0001 in the callee never reaches the root word
        var outer = Assert.IsType<TriggerDialogueRequest>(script.PendingDisplay);
        Assert.Equal(new TriggerPosition("shared", "L0002", 2), Assert.Single(outer.MessageReferences).Position);
        Advance(outer);
        Assert.True(script.AcknowledgeDisplay(outer.Id));
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
        Assert.Equal(1, script.ReturnEntry);
        Assert.Equal(60, script.Caller);
    }

    [Fact]
    public void Counter_free_Event_returns_use_active_saved_register()
    {
        // PAL_RunTriggerScript initialises wNextScriptEntry to the selected entry; 0x0008 and
        // 0x0001 overwrite it, 0x0000 keep leaves it untouched.
        var advance = Layout(4);
        Set(advance, 1, 8, """{"op":"advanceEntry"}""", 3);
        Set(advance, 2, 1, """{"op":"end","mode":"advance"}""", 3);
        var typed = new TriggerScriptSession(advance.Load(), Text((61, "A")));
        typed.Start("shared", "npc", 60);
        Assert.Equal(TriggerScriptPhase.Completed, typed.Phase);
        Assert.Equal(3, typed.ReturnEntry);

        var keep = Layout(3);
        Set(keep, 1, 0, """{"op":"end","mode":"keep"}""", 2);
        var untouched = new TriggerScriptSession(keep.Load(), Text((61, "A")));
        untouched.Start("shared", "npc", 60);
        Assert.Equal(TriggerScriptPhase.Completed, untouched.Phase);
        Assert.Equal(1, untouched.ReturnEntry);
    }

    [Fact]
    public void Single_call_inherits_caller_and_resumes_outer_display_without_replacing_root_identity_or_return_entry()
    {
        var fixture = Layout(7);
        Set(fixture, 1, 4, """{"op":"call","to":"L0005","eventObjectId":0}""", 6);
        Set(fixture, 2, 0xFFFF, """{"op":"showDialog","messageId":61,"descriptionLineWord":0}""", 6);
        Set(fixture, 3, 0, """{"op":"end","mode":"keep"}""", 6);
        Set(fixture, 5, 0xFFFF, """{"op":"showDialog","messageId":62,"descriptionLineWord":0}""", 6);
        var script = new TriggerScriptSession(fixture.Load(), Text((61, "outer"), (62, "callee")));

        script.Start("shared", "npc", 60);
        var callId = script.CallId;
        var started = script.StartedEntry;
        var entry = Assert.IsType<TriggerDialogueRequest>(script.PendingDisplay);
        Assert.Equal(60, script.Caller);
        Assert.Equal(62, Assert.Single(entry.MessageReferences).StartMessageId);
        Advance(entry);
        Assert.True(script.AcknowledgeDisplay(entry.Id));
        var outer = Assert.IsType<TriggerDialogueRequest>(script.PendingDisplay);
        Assert.Equal(60, script.Caller);
        Assert.Equal(61, Assert.Single(outer.MessageReferences).StartMessageId);
        Assert.Equal(callId, script.CallId);
        Assert.Same(started, script.StartedEntry);
        Assert.Equal(1, script.ReturnEntry);
        Advance(outer);
        Assert.True(script.AcknowledgeDisplay(outer.Id));
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
        Assert.Equal(60, script.Caller);
        Assert.Equal(1, script.ReturnEntry);
    }

    [Fact]
    public void Call_parked_on_a_shop_menu_keeps_the_event_frame_and_resumes_the_outer_continuation()
    {
        // script.c 0x0026 blocks the trigger inside uigame.c PAL_BuyMenu. A callee that parks there
        // keeps its 0x0004 frame: the caller prefix runs once, only the current request's receipt
        // resumes it, and the caller continues at its own successor without a second menu.
        var fixture = Layout(6);
        Set(fixture, 1, 0x001F, """{"op":"addItem","itemId":7,"count":1}""", 5);
        Set(fixture, 2, 4, """{"op":"call","to":"L0004","eventObjectId":0}""", 5);
        Set(fixture, 3, 0, """{"op":"end","mode":"keep"}""", 5);
        Set(fixture, 4, 0x0026, """{"op":"showBuyMenu","storeId":5}""", 5);
        Set(fixture, 5, 0, """{"op":"end","mode":"keep"}""", 5);
        var cash = new CashLiveState(600);
        var bag = new InventoryLiveState([]);
        var script = new TriggerScriptSession(fixture.Load(), Text((61, "A")), ShopContent(),
            new ZeroRandom(), cashLiveState: cash, inventoryLiveState: bag);

        script.Start("shared", "npc", 60);
        Assert.Equal(TriggerScriptPhase.WaitingForShop, script.Phase);
        Assert.Equal(60, script.Caller);
        Assert.Equal(1, script.ReturnEntry);
        Assert.Equal(1, bag.GetAmount(7)); // the caller prefix ran once before the call
        var buy = Assert.IsType<TriggerShopRequest>(script.PendingShopRequest);
        Assert.True(buy.IsBuy);
        Assert.Equal(5, buy.StoreId);

        // A stale receipt stays inert; the current one only releases the parked callee.
        Assert.False(script.AcknowledgeShop(Guid.NewGuid()));
        Assert.Equal(TriggerScriptPhase.WaitingForShop, script.Phase);
        Assert.True(script.AcknowledgeShop(buy.Id));
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
        Assert.Null(script.PendingShopRequest);
        Assert.False(script.AcknowledgeShop(buy.Id)); // a completed call never resumes twice
        Assert.Equal(60, script.Caller);
        Assert.Equal(1, script.ReturnEntry); // the callee's end keep never rewrites the caller word
        Assert.Equal(1, bag.GetAmount(7)); // the prefix is not replayed after the menu closes
        Assert.Equal(600u, cash.Cash);
        Assert.Equal(new TriggerPosition("shared", "L0003", 3), script.CurrentPosition);
    }

    [Fact]
    public void Nested_cross_file_calls_restore_callers_but_keep_global_success_and_last_caller()
    {
        var fixture = Layout(9);
        // shared owns the selected entry and call; other owns the nested entry and markFailed.
        fixture.Index["files"] = new JsonArray("other", "shared");
        var shared = fixture.Files["shared"];
        shared["commands"] = new JsonArray { Command(1, 4, """{"op":"call","to":"other#L0003","eventObjectId":0}""", 8), Command(2, 0, """{"op":"end","mode":"keep"}""", 8) };
        shared["commands"]![1]!["next"] = "other#L0003";
        var other = new JsonObject
        {
            ["formatVersion"] = 1, ["profile"] = "faithful", ["fileId"] = "other", ["entries"] = new JsonArray(),
            ["commands"] = new JsonArray
            {
                Command(0, 134, """{"op":"raw"}""", 8),
                Command(3, 4, """{"op":"call","to":"other#L0007","eventObjectId":71}""", 8),
                Command(4, 0, """{"op":"end","mode":"keep"}""", 8),
                Command(5, 0, """{"op":"end","mode":"keep"}""", 8),
                Command(6, 0, """{"op":"end","mode":"keep"}""", 8),
                Command(7, 0x41, """{"op":"markFailed"}""", 8),
                Command(8, 0, """{"op":"end","mode":"keep"}""", 8)
            }
        };
        other["commands"]![0]!["next"] = "shared#L0001";
        fixture.Files["other"] = other;
        var script = new TriggerScriptSession(fixture.Load(), Text((61, "A")));

        script.Start("shared", "npc", 60);
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
        Assert.Equal(60, script.Caller); // both callee frames returned their caller words
        Assert.Equal(1, script.ReturnEntry);
        Assert.False(script.ScriptSuccess); // g_fScriptSuccess is global, not restored by a return
        script.Start("shared", "npc", 65535);
        Assert.Equal(71, script.Caller); // wLastEventObject keeps the deepest explicit callee
    }

    [Fact]
    public void Call_operand_word_FFFF_inherits_the_last_event_object_not_literal_65535()
    {
        // script.c 3263-3267: 0x0004 passes operand1 to the recursive PAL_RunTriggerScript, whose
        // entry resolves 0xFFFF to the global wLastEventObject (script.c 3177-3182). The nested
        // literal callee (70) sets that word; returning restores only the current caller (60), so a
        // later 0xFFFF call still runs as 70 and never as literal 65535.
        var fixture = Layout(14);
        Set(fixture, 1, 4, """{"op":"call","to":"L0005","eventObjectId":0}""", 13);
        Set(fixture, 2, 0, """{"op":"end","mode":"keep"}""", 13);
        Set(fixture, 5, 4, """{"op":"call","to":"L0009","eventObjectId":70}""", 13);
        Set(fixture, 6, 4, """{"op":"call","to":"L000B","eventObjectId":65535}""", 13);
        Set(fixture, 7, 0, """{"op":"end","mode":"keep"}""", 13);
        Set(fixture, 9, 0xFFFF, """{"op":"showDialog","messageId":62,"descriptionLineWord":0}""", 13);
        Set(fixture, 10, 0, """{"op":"end","mode":"keep"}""", 13);
        Set(fixture, 11, 0xFFFF, """{"op":"showDialog","messageId":63,"descriptionLineWord":0}""", 13);
        Set(fixture, 12, 0, """{"op":"end","mode":"keep"}""", 13);
        var script = new TriggerScriptSession(fixture.Load(), Text((62, "nested"), (63, "ffff")));

        script.Start("shared", "npc", 60);
        var nested = Assert.IsType<TriggerDialogueRequest>(script.PendingDisplay);
        Assert.Equal(70, script.Caller);
        Advance(nested);
        Assert.True(script.AcknowledgeDisplay(nested.Id));

        var inherited = Assert.IsType<TriggerDialogueRequest>(script.PendingDisplay);
        Assert.Equal(70, script.Caller); // wLastEventObject, not the restored caller 60 or 65535
        Assert.Equal(63, Assert.Single(inherited.MessageReferences).StartMessageId);
        Advance(inherited);
        Assert.True(script.AcknowledgeDisplay(inherited.Id));
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
        Assert.Equal(60, script.Caller);
        Assert.Equal(1, script.ReturnEntry);
        Assert.Null(script.PendingDisplay);
    }

    [Fact]
    public void Counted_callee_behind_a_ffff_after_an_explicit_call_rejects_a_missing_last_caller_owner()
    {
        // script.c 3163-3182/3260-3267: the 0xFFFF operand resolves inside the nested entry from
        // wLastEventObject, which the earlier literal caller 70 advanced and a return never
        // restores, so the counted gate owns object 70 rather than the root caller 60. A missing
        // owner must reject before the prefix display or any identity change.
        var script = new TriggerScriptSession(LastCallerCalleeFixture().Load(),
            Text((61, "prefix"), (62, "nested")), World(60));
        var error = Assert.Throws<InvalidDataException>(() => script.Start("shared", "npc", 60));
        Assert.Contains("counted goto", error.Message);
        Assert.Contains("70", error.Message);
        Assert.Equal(TriggerScriptPhase.Ready, script.Phase);
        Assert.Equal(Guid.Empty, script.CallId);
        Assert.Null(script.StartedEntry);
        Assert.Null(script.CurrentPosition);
        Assert.Null(script.PendingDisplay);
    }

    [Fact]
    public void Counted_callee_behind_a_ffff_after_an_explicit_call_uses_the_last_event_object_owner()
    {
        var world = World(60, 70);
        var script = new TriggerScriptSession(LastCallerCalleeFixture().Load(),
            Text((61, "prefix"), (62, "nested")), world);
        script.Start("shared", "npc", 60);
        var prefix = Assert.IsType<TriggerDialogueRequest>(script.PendingDisplay);
        Assert.Equal(61, Assert.Single(prefix.MessageReferences).StartMessageId);
        Advance(prefix);
        Assert.True(script.AcknowledgeDisplay(prefix.Id));
        var nested = Assert.IsType<TriggerDialogueRequest>(script.PendingDisplay);
        Assert.Equal(70, script.Caller);
        Assert.Equal(62, Assert.Single(nested.MessageReferences).StartMessageId);
        Advance(nested);
        Assert.True(script.AcknowledgeDisplay(nested.Id));
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
        Assert.Equal(60, script.Caller); // only the caller word is restored by a return
        Assert.Equal(1, world.Objects.Single(o => o.Id == 70).TriggerIdleCount);
        Assert.Equal(0, world.Objects.Single(o => o.Id == 60).TriggerIdleCount);
    }

    [Fact]
    public void Counted_goto_and_redirect_publish_the_authored_entry_with_the_callers_idle_counter()
    {
        // script.c 0x0002/0x0003: limit zero takes the branch and never touches a counter. The
        // pre-increment is taken while it stays below the limit; the first failure resets to zero
        // and falls through to the successor word. auto keeps its own counter (wScriptIdleFrameCountAuto).
        var jumpWorld = World(60);
        var jumps = new TriggerScriptSession(CountedFixture().Load(), Language(), jumpWorld);
        jumps.Start("shared", "npc", 60);
        Assert.Equal(TriggerScriptPhase.Completed, jumps.Phase);
        Assert.Equal(new TriggerPosition("shared", "L0006", 6), jumps.CurrentPosition);
        Assert.Equal(1, jumps.ReturnEntry);
        Assert.Equal(1, jumpWorld.Objects.Single(o => o.Id == 60).TriggerIdleCount);

        jumps.Start("shared", "npc", 60);
        Assert.Equal(new TriggerPosition("shared", "L0006", 6), jumps.CurrentPosition);
        Assert.Equal(2, jumpWorld.Objects.Single(o => o.Id == 60).TriggerIdleCount);

        jumps.Start("shared", "npc", 60);
        Assert.Equal(new TriggerPosition("shared", "L0002", 2), jumps.CurrentPosition);
        Assert.Equal(1, jumps.ReturnEntry); // keep never rewrites the root return register
        Assert.Equal(0, jumpWorld.Objects.Single(o => o.Id == 60).TriggerIdleCount); // the failed gate reset it

        jumps.Start("shared", "npc", 60);
        Assert.Equal(new TriggerPosition("shared", "L0006", 6), jumps.CurrentPosition);
        Assert.Equal(1, jumpWorld.Objects.Single(o => o.Id == 60).TriggerIdleCount);

        // 0x0002 publishes the redirect target as the return entry, then falls through and resets.
        var redirectWorld = World(60);
        var redirect = new TriggerScriptSession(CountedFixture().Load(), Language(), redirectWorld);
        redirect.Start("shared", "redirect", 60);
        Assert.Equal(TriggerScriptPhase.Completed, redirect.Phase);
        Assert.Equal(new TriggerPosition("shared", "L0003", 3), redirect.CurrentPosition);
        Assert.Equal(6, redirect.ReturnEntry);
        Assert.Equal(1, redirectWorld.Objects.Single(o => o.Id == 60).TriggerIdleCount);
        redirect.Start("shared", "redirect", 60);
        Assert.Equal(new TriggerPosition("shared", "L0004", 4), redirect.CurrentPosition);
        Assert.Equal(3, redirect.ReturnEntry); // the register still starts at this entry's word
        Assert.Equal(0, redirectWorld.Objects.Single(o => o.Id == 60).TriggerIdleCount);

        // A zero limit is an unconditional, counter-free branch.
        var zeroLimit = CountedFixture();
        zeroLimit.Commands[3] = Command(3, 2, """{"op":"end","mode":"redirect","to":"L0006","limit":0}""", lastAddress: 7);
        var zeroWorld = World(60);
        var unconditional = new TriggerScriptSession(zeroLimit.Load(), Language(), zeroWorld);
        unconditional.Start("shared", "redirect", 60);
        Assert.Equal(new TriggerPosition("shared", "L0003", 3), unconditional.CurrentPosition);
        Assert.Equal(6, unconditional.ReturnEntry);
        Assert.Equal(0, zeroWorld.Objects.Single(o => o.Id == 60).TriggerIdleCount);

        // 0x0008 replaces the returned entry with the next instruction and keeps running.
        var advance = new TriggerScriptSession(CountedFixture().Load(), Language(), World(60));
        advance.Start("shared", "advance", 60);
        Assert.Equal(TriggerScriptPhase.Completed, advance.Phase);
        Assert.Equal(6, advance.ReturnEntry);
        Assert.Equal(new TriggerPosition("shared", "L0006", 6), advance.CurrentPosition);

        // goto to word zero parks the loop with the entry untouched, exactly like the reference.
        var toZero = CountedFixture();
        toZero.Commands[1] = Command(1, 3, """{"op":"goto","to":"L0000","limit":0}""", lastAddress: 7);
        var terminal = new TriggerScriptSession(toZero.Load(), Language());
        terminal.Start("shared", "npc", 0);
        Assert.Equal(TriggerScriptPhase.Completed, terminal.Phase);
        Assert.Equal(1, terminal.ReturnEntry);
        Assert.Equal(0, terminal.Caller);
        Assert.Equal(new TriggerPosition("shared", "L0000", 0), terminal.CurrentPosition);

        // Counted control flow needs the caller's scene event object before any effect.
        var unworldly = new TriggerScriptSession(CountedFixture().Load(), Language());
        var noWorld = Assert.Throws<InvalidDataException>(() => unworldly.Start("shared", "npc", 60));
        Assert.Contains("counted goto", noWorld.Message);
        Assert.Contains("60", noWorld.Message);
        Assert.Equal(TriggerScriptPhase.Ready, unworldly.Phase);
        Assert.Equal(0, unworldly.ReturnEntry);
        Assert.Null(unworldly.CurrentPosition);

        var world = World(60);
        var unknownCaller = new TriggerScriptSession(CountedFixture().Load(), Language(), world);
        Assert.Contains("61", Assert.Throws<InvalidDataException>(() => unknownCaller.Start("shared", "npc", 61)).Message);
        Assert.Contains("0", Assert.Throws<InvalidDataException>(() => unknownCaller.Start("shared", "npc", 0)).Message);
        Assert.Equal(0, world.Objects[0].TriggerIdleCount);

        // 0x00A7 shares the advanceEntry name but belongs to the auto switch only.
        var autoOnly = CountedFixture();
        autoOnly.Commands[5] = Command(5, 0xA7, """{"op":"advanceEntry"}""", lastAddress: 7);
        var wrongSwitch = new TriggerScriptSession(autoOnly.Load(), Language(), World(60));
        Assert.Contains("advanceEntry", Assert.Throws<InvalidDataException>(() => wrongSwitch.Start("shared", "advance", 60)).Message);
    }

    [Fact]
    public void Closed_counted_callee_preserves_actual_caller_last_and_outer_continuation()
    {
        var fixture = Layout(7);
        Set(fixture, 1, 4, """{"op":"call","to":"L0005","eventObjectId":1}""", 6);
        Set(fixture, 2, 0, """{"op":"end","mode":"keep"}""", 6);
        Set(fixture, 5, 3, """{"op":"goto","to":"L0006","limit":2}""", 6);
        var world = World(1);
        var script = new TriggerScriptSession(fixture.Load(), Text((61, "A")), world);

        script.Start("shared", "npc", 60);
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
        Assert.Equal(60, script.Caller);
        Assert.Equal(1, script.ReturnEntry);
        Assert.Equal(1, world.Objects.Single(o => o.Id == 1).TriggerIdleCount);
        script.Start("shared", "npc", 65535);
        Assert.Equal(1, script.Caller); // the literal callee word is the surviving last caller
        Assert.Equal(0, world.Objects.Single(o => o.Id == 1).TriggerIdleCount);
        Assert.Equal(new TriggerPosition("shared", "L0002", 2), script.CurrentPosition);
    }

    [Fact]
    public void Zero_limit_goto_does_not_consume_an_existing_trigger_count()
    {
        var fixture = Layout(7);
        Set(fixture, 1, 3, """{"op":"goto","to":"L0006","limit":3}""", 6);
        Set(fixture, 2, 0, """{"op":"end","mode":"keep"}""", 6);
        Set(fixture, 3, 3, """{"op":"goto","to":"L0006","limit":0}""", 6);
        Set(fixture, 4, 0, """{"op":"end","mode":"keep"}""", 6);
        AddEntry(fixture, "zero", 3);
        var world = World(60);
        var script = new TriggerScriptSession(fixture.Load(), Text((61, "A")), world);
        var owner = world.Objects.Single(o => o.Id == 60);

        script.Start("shared", "npc", 60);
        Assert.Equal(1, owner.TriggerIdleCount);
        script.Start("shared", "zero", 60);
        Assert.Equal(new TriggerPosition("shared", "L0006", 6), script.CurrentPosition);
        Assert.Equal(1, owner.TriggerIdleCount); // limit zero is unconditional and counter-free
        script.Start("shared", "npc", 60);
        Assert.Equal(2, owner.TriggerIdleCount);
    }

    [Fact]
    public void Root_counted_redirect_shares_owner_across_fresh_executors_and_resets_on_limit()
    {
        var events = RedirectFixture(limit: 2).Load();
        var world = World(60);
        var owner = world.Objects.Single(o => o.Id == 60);

        var first = new TriggerScriptSession(events, Text((61, "A")), world);
        first.Start("shared", "npc", 60);
        Assert.Equal(TriggerScriptPhase.Completed, first.Phase);
        Assert.Equal(5, first.ReturnEntry);
        Assert.Equal(1, owner.TriggerIdleCount);

        var second = new TriggerScriptSession(events, Text((61, "A")), world);
        second.Start("shared", "npc", 60);
        Assert.Equal(new TriggerPosition("shared", "L0002", 2), second.CurrentPosition);
        Assert.Equal(1, second.ReturnEntry); // a fall-through keeps the selected entry word
        Assert.Equal(0, owner.TriggerIdleCount); // the failed gate reset the shared owner
    }

    [Fact]
    public void Root_counted_redirect_limit_one_falls_through_without_rewriting_saved_return()
    {
        var world = World(60);
        var script = new TriggerScriptSession(RedirectFixture(limit: 1).Load(), Text((61, "A")), world);
        script.Start("shared", "npc", 60);
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
        Assert.Equal(new TriggerPosition("shared", "L0002", 2), script.CurrentPosition);
        Assert.Equal(1, script.ReturnEntry);
        Assert.Equal(0, world.Objects.Single(o => o.Id == 60).TriggerIdleCount);
    }

    [Fact]
    public void Zero_limit_redirect_does_not_consume_an_existing_trigger_count()
    {
        var fixture = Layout(7);
        Set(fixture, 1, 3, """{"op":"goto","to":"L0006","limit":3}""", 6);
        Set(fixture, 2, 0, """{"op":"end","mode":"keep"}""", 6);
        Set(fixture, 3, 2, """{"op":"end","mode":"redirect","to":"L0006","limit":0}""", 6);
        Set(fixture, 4, 0, """{"op":"end","mode":"keep"}""", 6);
        AddEntry(fixture, "zero", 3);
        var world = World(60);
        var script = new TriggerScriptSession(fixture.Load(), Text((61, "A")), world);
        var owner = world.Objects.Single(o => o.Id == 60);
        script.Start("shared", "npc", 60);
        Assert.Equal(1, owner.TriggerIdleCount);
        script.Start("shared", "zero", 60);
        Assert.Equal(6, script.ReturnEntry);
        Assert.Equal(1, owner.TriggerIdleCount);
    }

    [Fact]
    public void Root_counted_goto_self_loop_reaches_display_then_preserves_saved_return()
    {
        var fixture = Layout(4);
        Set(fixture, 1, 3, """{"op":"goto","to":"L0001","limit":1}""", 3);
        Set(fixture, 2, 0xFFFF, """{"op":"showDialog","messageId":61,"descriptionLineWord":0}""", 3);
        Set(fixture, 3, 0, """{"op":"end","mode":"keep"}""", 3);
        var world = World(60);
        var script = new TriggerScriptSession(fixture.Load(), Text((61, "A")), world);
        script.Start("shared", "npc", 60);
        Assert.Equal(0, world.Objects.Single(o => o.Id == 60).TriggerIdleCount);
        var display = Assert.IsType<TriggerDialogueRequest>(script.PendingDisplay);
        Assert.Equal(1, script.ReturnEntry); // goto never publishes a return entry
        Advance(display);
        Assert.True(script.AcknowledgeDisplay(display.Id));
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
        Assert.Equal(1, script.ReturnEntry);
    }

    [Fact]
    public void Root_counted_goto_zero_preserves_saved_return_across_fresh_calls()
    {
        var fixture = Layout(6);
        Set(fixture, 1, 3, """{"op":"goto","to":"L0005","limit":3}""", 5);
        Set(fixture, 2, 0, """{"op":"end","mode":"keep"}""", 5);
        var events = fixture.Load();
        var world = World(60);
        var owner = world.Objects.Single(o => o.Id == 60);
        for (int call = 1; call <= 3; call++)
        {
            var script = new TriggerScriptSession(events, Text((61, "A")), world);
            script.Start("shared", "npc", 60);
            Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
            Assert.Equal(1, script.ReturnEntry);
            Assert.Equal(call == 3 ? 0 : call, owner.TriggerIdleCount);
        }
    }

    [Fact]
    public void Root_counted_goto_and_redirect_share_the_same_owner_gate()
    {
        var fixture = Layout(7);
        Set(fixture, 1, 3, """{"op":"goto","to":"L0006","limit":2}""", 6);
        Set(fixture, 2, 0, """{"op":"end","mode":"keep"}""", 6);
        Set(fixture, 3, 2, """{"op":"end","mode":"redirect","to":"L0006","limit":2}""", 6);
        Set(fixture, 4, 0, """{"op":"end","mode":"keep"}""", 6);
        AddEntry(fixture, "redirect", 3);
        var world = World(60);
        var owner = world.Objects.Single(o => o.Id == 60);
        var script = new TriggerScriptSession(fixture.Load(), Text((61, "A")), world);
        script.Start("shared", "npc", 60);
        Assert.Equal(1, owner.TriggerIdleCount);
        script.Start("shared", "redirect", 60);
        Assert.Equal(new TriggerPosition("shared", "L0004", 4), script.CurrentPosition);
        Assert.Equal(0, owner.TriggerIdleCount);
    }

    [Fact]
    public void Root_counted_goto_zero_does_not_overwrite_typed_advance_saved_return()
    {
        var fixture = Layout(7);
        Set(fixture, 1, 8, """{"op":"advanceEntry"}""", 6);
        Set(fixture, 2, 3, """{"op":"goto","to":"L0006","limit":0}""", 6);
        Set(fixture, 3, 0, """{"op":"end","mode":"keep"}""", 6);
        var script = new TriggerScriptSession(fixture.Load(), Text((61, "A")));
        script.Start("shared", "npc", 60);
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
        Assert.Equal(2, script.ReturnEntry); // 0x0008 published 2; goto published nothing
        Assert.Equal(new TriggerPosition("shared", "L0006", 6), script.CurrentPosition);
    }

    [Fact]
    public void Counted_goto_rejects_missing_owner_before_prefix_or_identity()
    {
        var fixture = Layout(6);
        Set(fixture, 1, 0xFFFF, """{"op":"showDialog","messageId":61,"descriptionLineWord":0}""", 5);
        Set(fixture, 2, 3, """{"op":"goto","to":"L0005","limit":2}""", 5);
        Set(fixture, 3, 0, """{"op":"end","mode":"keep"}""", 5);
        Set(fixture, 5, 0, """{"op":"end","mode":"keep"}""", 5);
        var script = new TriggerScriptSession(fixture.Load(), Text((61, "A")), World(5));
        var error = Assert.Throws<InvalidDataException>(() => script.Start("shared", "npc", 60));
        Assert.Contains("counted goto", error.Message);
        Assert.Contains("60", error.Message);
        Assert.Equal(TriggerScriptPhase.Ready, script.Phase);
        Assert.Equal(Guid.Empty, script.CallId);
        Assert.Null(script.StartedEntry);
        Assert.Null(script.CurrentPosition);
        Assert.Null(script.PendingDisplay);
        Assert.Equal(0, script.Caller);
        Assert.Equal(0, script.ReturnEntry);
    }

    [Fact]
    public void Zero_limit_goto_remains_legal_without_a_world_or_defined_counter_owner()
    {
        var fixture = Layout(4);
        Set(fixture, 1, 3, """{"op":"goto","to":"L0003","limit":0}""", 3);
        Set(fixture, 2, 0, """{"op":"end","mode":"keep"}""", 3);
        Set(fixture, 3, 0, """{"op":"end","mode":"keep"}""", 3);
        var script = new TriggerScriptSession(fixture.Load(), Text((61, "A")));
        script.Start("shared", "npc", 60);
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
        Assert.Equal(new TriggerPosition("shared", "L0003", 3), script.CurrentPosition);
        Assert.Equal(1, script.ReturnEntry);
        Assert.Equal(60, script.Caller);
    }

    [Theory]
    [InlineData("target")]
    [InlineData("fallthrough")]
    public void Counted_goto_checks_target_and_fallthrough_before_prefix_effects(string fault)
    {
        var fixture = Layout(6);
        Set(fixture, 1, 0xFFFF, """{"op":"showDialog","messageId":61,"descriptionLineWord":0}""", 5);
        Set(fixture, 2, 3, """{"op":"goto","to":"L0004","limit":2}""", 5);
        Set(fixture, 3, 0, """{"op":"end","mode":"keep"}""", 5);
        Set(fixture, 4, 0, """{"op":"end","mode":"keep"}""", 5);
        Set(fixture, 5, 0, """{"op":"end","mode":"keep"}""", 5);
        var faulty = fault == "target" ? 4 : 3;
        Set(fixture, faulty, 134, """{"op":"raw"}""", 5);
        var script = new TriggerScriptSession(fixture.Load(), Text((61, "A")), World(60));
        var error = Assert.Throws<InvalidDataException>(() => script.Start("shared", "npc", 60));
        Assert.Contains($"L{faulty:X4}", error.Message);
        Assert.Null(script.PendingDisplay);
        Assert.Equal(TriggerScriptPhase.Ready, script.Phase);
    }

    [Fact]
    public void Static_checks_keep_counted_goto_outside_the_counter_free_contract()
    {
        var events = StaticCountedFixture("""{"op":"goto","to":"L0005","limit":2}""", 3).Load();
        var error = Assert.Throws<InvalidDataException>(() =>
            TriggerScriptSession.ValidateEntry(events, Text((61, "A")), SimpleContent(), "shared", "npc"));
        Assert.Contains("counted goto", error.Message);
        Assert.Contains("bound caller", error.Message);
    }

    [Fact]
    public void Static_checks_accept_a_counted_gate_with_the_bound_scene_and_caller()
    {
        // #234: the formal story preflight binds the static scene world and the entry owner word
        // Main passes at Start, so a counted 0x0002/0x0003 gate resolves its owner exactly like
        // the runtime (script.c 3196/3229, play.c 153) instead of staying outside the contract.
        var events = StaticCountedFixture("""{"op":"goto","to":"L0005","limit":2}""", 3).Load();
        var world = World(60);
        var requirements = TriggerScriptSession.ValidateEntry(events, Text((61, "A")), SimpleContent(), "shared", "npc",
            world: world, caller: 60);
        Assert.Empty(requirements.BattleTeamIds);
        Assert.Empty(requirements.PortraitIds);
        Assert.Equal(0, world.Objects.Single(o => o.Id == 60).TriggerIdleCount);
    }

    [Fact]
    public void Static_checks_reject_a_counted_gate_with_an_unreachable_scene_owner_before_prefix()
    {
        var events = StaticCountedFixture("""{"op":"goto","to":"L0005","limit":2}""", 3).Load();
        var error = Assert.Throws<InvalidDataException>(() =>
            TriggerScriptSession.ValidateEntry(events, Text((61, "A")), SimpleContent(), "shared", "npc",
                world: World(5), caller: 60));
        Assert.Contains("counted goto", error.Message);
        Assert.Contains("event object 60", error.Message);
    }

    [Fact]
    public void Static_checks_keep_counted_redirect_outside_the_counter_free_contract()
    {
        var events = StaticCountedFixture("""{"op":"end","mode":"redirect","to":"L0005","limit":2}""", 2).Load();
        var error = Assert.Throws<InvalidDataException>(() =>
            TriggerScriptSession.ValidateEntry(events, Text((61, "A")), SimpleContent(), "shared", "npc"));
        Assert.Contains("counted end", error.Message);
        Assert.Contains("bound caller", error.Message);
    }

    [Fact]
    public void Static_counter_free_checks_still_reject_counted_callee()
    {
        var fixture = Layout(7);
        Set(fixture, 1, 4, """{"op":"call","to":"L0005","eventObjectId":60}""", 6);
        Set(fixture, 5, 3, """{"op":"goto","to":"L0006","limit":2}""", 6);
        var error = Assert.Throws<InvalidDataException>(() =>
            TriggerScriptSession.ValidateEntry(fixture.Load(), Text((61, "A")), SimpleContent(), "shared", "npc"));
        Assert.Contains("counted goto", error.Message);
        Assert.Contains("L0005", error.Message);
    }

    [Fact]
    public void Closed_counted_callee_rejects_before_prefix_identity_and_last_caller_changes()
    {
        var fixture = Layout(7);
        Set(fixture, 1, 0xFFFF, """{"op":"showDialog","messageId":61,"descriptionLineWord":0}""", 6);
        Set(fixture, 2, 4, """{"op":"call","to":"L0005","eventObjectId":1}""", 6);
        Set(fixture, 3, 0, """{"op":"end","mode":"keep"}""", 6);
        Set(fixture, 5, 3, """{"op":"goto","to":"L0006","limit":2}""", 6);
        var script = new TriggerScriptSession(fixture.Load(), Text((61, "A")), World(5));
        var error = Assert.Throws<InvalidDataException>(() => script.Start("shared", "npc", 60));
        Assert.Contains("counted goto", error.Message);
        Assert.Contains("1", error.Message);
        Assert.Null(script.PendingDisplay);
        Assert.Null(script.StartedEntry);
        Assert.Equal(Guid.Empty, script.CallId);
        Assert.Equal(TriggerScriptPhase.Ready, script.Phase);
        Assert.Equal(0, script.Caller);
    }

    [Fact]
    public void Root_trigger_counter_copy_retains_value_but_not_mutation_ownership()
    {
        var fixture = Layout(6);
        Set(fixture, 1, 3, """{"op":"goto","to":"L0005","limit":3}""", 5);
        Set(fixture, 2, 0, """{"op":"end","mode":"keep"}""", 5);
        var events = fixture.Load();
        var world = World(60);
        Assert.Equal(1, RunCounted(events, world));
        Assert.Equal(2, RunCounted(events, world));
        var copy = world.CreateIsolatedCopy();
        Assert.Equal(2, copy.Objects.Single(o => o.Id == 60).TriggerIdleCount);
        Assert.Equal(0, RunCounted(events, copy));
        Assert.Equal(2, world.Objects.Single(o => o.Id == 60).TriggerIdleCount);
    }

    [Fact]
    public void Closed_domain_restriction_does_not_remove_existing_update_or_battle_capability()
    {
        var events = MidbattleRoleHookTests.Fixture().Load();
        var content = MidbattleRoleHookTests.Content(60);
        Assert.Equal(new[] { 18 }, TriggerScriptSession.ValidateEntry(events, MidbattleRoleHookTests.Language(),
            content, "hooks", "npc").BattleTeamIds);
        var script = new TriggerScriptSession(events, MidbattleRoleHookTests.Language(), content, new ZeroRandom());
        script.Start("hooks", "npc", 28);
        Assert.Equal(TriggerScriptPhase.WaitingForBattle, script.Phase);
        Assert.Null(script.PendingDisplay);
        Assert.NotNull(script.PendingBattle);
        Assert.Equal(18, script.PendingBattle.EnemyTeamId);
        Assert.Equal(28, script.Caller);
    }

    [Fact]
    public void Battle_owned_entry_resets_success_and_default_delay_before_its_first_display()
    {
        // PAL_RunTriggerScript entry resets g_fScriptSuccess and PAL_DialogSetDelayTime(3) for
        // the hook invocation even though the outer call already failed.
        var fixture = Layout(12);
        Set(fixture, 1, 60, """{"op":"setDialog","box":"upper","portraitId":0,"color":0,"rngWord":0}""", 11);
        Set(fixture, 2, 0xFFFF, """{"op":"showDialog","messageId":10,"descriptionLineWord":0}""", 11);
        Set(fixture, 3, 0x41, """{"op":"markFailed"}""", 11);
        Set(fixture, 4, 7, """{"op":"startBattle","enemyTeamId":18,"lossTarget":null,"isBoss":true,"fleeTarget":null}""", 11);
        Set(fixture, 8, 0xFFFF, """{"op":"showDialog","messageId":11,"descriptionLineWord":0}""", 11);
        var random = HookRandom();
        var script = new TriggerScriptSession(fixture.Load(), Text((10, "pre'$12"), (11, "hook")),
            HookContent(60, dyingScript: 8), random);

        script.Start("shared", "npc", 28);
        var prefix = Assert.IsType<TriggerDialogueRequest>(script.PendingDisplay);
        Assert.Equal(136, prefix.Display.DelayMilliseconds);
        Advance(prefix);
        Assert.True(script.AcknowledgeDisplay(prefix.Id));
        Assert.Equal(TriggerScriptPhase.WaitingForBattle, script.Phase);
        Assert.False(script.ScriptSuccess);
        var battle = script.PendingBattle!;
        battle.Lifecycle.SelectAttack(0, 0);
        battle.Lifecycle.SelectAttack(1, 0);
        Assert.True(battle.Lifecycle.ResolveNextAction());
        Assert.Equal(new ClassicBattleRoleHook(ClassicBattleHookKind.Dying, 1, 1, 8), script.PendingBattleHook);

        script.StartBattleHook();
        Assert.True(script.ScriptSuccess);
        Assert.Equal(1, script.Caller);
        var display = Assert.IsType<TriggerDialogueRequest>(script.PendingDisplay);
        Assert.Equal(11, display.Content.StartMessageId);
        Assert.Equal(24, display.Context.DelayMilliseconds);
        Advance(display);
        Assert.True(script.AcknowledgeDisplay(display.Id));
        Assert.Equal(TriggerScriptPhase.WaitingForBattle, script.Phase);
        Assert.Equal(28, script.Caller);
        Assert.Equal(1, script.ReturnEntry);
    }

    [Fact]
    public void Battle_owned_body_failure_survives_display_ack_and_real_lifecycle_return()
    {
        var fixture = Layout(12);
        Set(fixture, 1, 7, """{"op":"startBattle","enemyTeamId":18,"lossTarget":null,"isBoss":true,"fleeTarget":null}""", 11);
        Set(fixture, 8, 0x41, """{"op":"markFailed"}""", 11);
        Set(fixture, 9, 0xFFFF, """{"op":"showDialog","messageId":11,"descriptionLineWord":0}""", 11);
        var script = new TriggerScriptSession(fixture.Load(), Text((11, "hook")), HookContent(60, dyingScript: 8), HookRandom());
        script.Start("shared", "npc", 28);
        var battle = script.PendingBattle!;
        battle.Lifecycle.SelectAttack(0, 0);
        battle.Lifecycle.SelectAttack(1, 0);
        Assert.True(battle.Lifecycle.ResolveNextAction());

        script.StartBattleHook();
        var display = Assert.IsType<TriggerDialogueRequest>(script.PendingDisplay);
        Assert.False(script.ScriptSuccess);
        Advance(display);
        Assert.True(script.AcknowledgeDisplay(display.Id));
        Assert.Equal(TriggerScriptPhase.WaitingForBattle, script.Phase);
        Assert.Null(script.RunningBattleHook);
        Assert.False(script.ScriptSuccess); // a receipt never rewrites g_fScriptSuccess
        Assert.Equal(28, script.Caller);
        Assert.Equal(ClassicBattlePhase.Resolving, battle.Lifecycle.Current.Combat.Phase);
        Assert.Same(battle, script.PendingBattle);
    }

    [Fact]
    public void Battle_owned_counted_callees_use_the_resolved_event_owner()
    {
        var fixture = Layout(16);
        Set(fixture, 1, 7, """{"op":"startBattle","enemyTeamId":18,"lossTarget":null,"isBoss":true,"fleeTarget":null}""", 15);
        Set(fixture, 8, 4, """{"op":"call","to":"L000C","eventObjectId":1}""", 15);
        Set(fixture, 9, 0, """{"op":"end","mode":"keep"}""", 15);
        Set(fixture, 12, 3, """{"op":"goto","to":"L000E","limit":2}""", 15);
        Set(fixture, 14, 0xFFFF, """{"op":"showDialog","messageId":11,"descriptionLineWord":0}""", 15);
        var world = BattleWorld(1, 5, 6);
        var script = new TriggerScriptSession(fixture.Load(), Text((11, "hook")),
            HookContent(60, dyingScript: 8), HookRandom(), world);
        script.Start("shared", "npc", 28);
        var battle = script.PendingBattle!;
        battle.Lifecycle.SelectAttack(0, 0);
        battle.Lifecycle.SelectAttack(1, 0);
        Assert.True(battle.Lifecycle.ResolveNextAction());

        script.StartBattleHook();
        Assert.Equal(1, script.Caller);
        Assert.Equal(1, world.Objects.Single(o => o.Id == 1).TriggerIdleCount);
        var display = Assert.IsType<TriggerDialogueRequest>(script.PendingDisplay);
        Assert.Equal(11, display.Content.StartMessageId);
        Advance(display);
        Assert.True(script.AcknowledgeDisplay(display.Id));
        Assert.Equal(TriggerScriptPhase.WaitingForBattle, script.Phase);
        Assert.Equal(28, script.Caller);
        Assert.Equal(1, script.ReturnEntry);
    }

    [Fact]
    public void Battle_owned_counted_callees_reject_undefined_owners_before_prefix()
    {
        var fixture = Layout(16);
        Set(fixture, 1, 7, """{"op":"startBattle","enemyTeamId":18,"lossTarget":null,"isBoss":true,"fleeTarget":null}""", 15);
        Set(fixture, 8, 4, """{"op":"call","to":"L000C","eventObjectId":1}""", 15);
        Set(fixture, 12, 3, """{"op":"goto","to":"L000E","limit":2}""", 15);
        var script = new TriggerScriptSession(fixture.Load(), Text((11, "hook")),
            HookContent(60, dyingScript: 8), HookRandom(), BattleWorld(5, 6));
        var error = Assert.Throws<InvalidDataException>(() => script.Start("shared", "npc", 28));
        Assert.Contains("counted goto", error.Message);
        Assert.Contains("event object 1", error.Message);
        Assert.Equal(TriggerScriptPhase.Ready, script.Phase);
        Assert.Null(script.PendingBattle);
        Assert.Null(script.PendingDisplay);
        Assert.Null(script.RunningBattleHook);
    }

    [Fact]
    public void Battle_owned_root_goto_preserves_the_parked_event_return()
    {
        var fixture = Layout(16);
        Set(fixture, 1, 7, """{"op":"startBattle","enemyTeamId":18,"lossTarget":null,"isBoss":true,"fleeTarget":null}""", 15);
        Set(fixture, 8, 3, """{"op":"goto","to":"L000B","limit":0}""", 15);
        Set(fixture, 11, 0xFFFF, """{"op":"showDialog","messageId":11,"descriptionLineWord":0}""", 15);
        Set(fixture, 12, 1, """{"op":"end","mode":"advance"}""", 15);
        var world = BattleWorld(1, 5, 6);
        var script = new TriggerScriptSession(fixture.Load(), Text((11, "hook")),
            HookContent(60, dyingScript: 8), HookRandom(), world);
        script.Start("shared", "npc", 28);
        var battle = script.PendingBattle!;
        battle.Lifecycle.SelectAttack(0, 0);
        battle.Lifecycle.SelectAttack(1, 0);
        Assert.True(battle.Lifecycle.ResolveNextAction());

        script.StartBattleHook();
        Assert.Equal(1, script.ReturnEntry);
        Assert.Equal(0, world.Objects.Single(o => o.Id == 1).TriggerIdleCount);
        var display = Assert.IsType<TriggerDialogueRequest>(script.PendingDisplay);
        Advance(display);
        Assert.True(script.AcknowledgeDisplay(display.Id));
        Assert.Equal(TriggerScriptPhase.WaitingForBattle, script.Phase);
        Assert.Equal(1, script.ReturnEntry); // the hook's own 0x0001 never replaces the parked event word
        Assert.Equal(28, script.Caller);
        Assert.Null(script.RunningBattleHook);
        Assert.Equal(ClassicBattlePhase.Resolving, battle.Lifecycle.Current.Combat.Phase);
    }

    [Fact]
    public void Battle_owned_root_goto_rejects_undefined_counter_owners_before_prefix()
    {
        var fixture = Layout(16);
        Set(fixture, 1, 7, """{"op":"startBattle","enemyTeamId":18,"lossTarget":null,"isBoss":true,"fleeTarget":null}""", 15);
        Set(fixture, 8, 3, """{"op":"goto","to":"L000B","limit":2}""", 15);
        var script = new TriggerScriptSession(fixture.Load(), Text((11, "hook")),
            HookContent(60, dyingScript: 8), HookRandom(), BattleWorld(5, 6));
        var error = Assert.Throws<InvalidDataException>(() => script.Start("shared", "npc", 28));
        Assert.Contains("counted goto", error.Message);
        Assert.Contains("event object 1", error.Message);
        Assert.Equal(TriggerScriptPhase.Ready, script.Phase);
        Assert.Null(script.PendingBattle);
    }

    [Fact]
    public void Single_role_hook_callee_routes_effects_and_restores_owned_hook()
    {
        var fixture = Layout(16);
        Set(fixture, 1, 7, """{"op":"startBattle","enemyTeamId":18,"lossTarget":null,"isBoss":true,"fleeTarget":null}""", 15);
        Set(fixture, 8, 4, """{"op":"call","to":"L000C","eventObjectId":0}""", 15);
        Set(fixture, 12, 27, """{"op":"changeHp","scope":0,"amount":1}""", 15);
        var script = new TriggerScriptSession(fixture.Load(), Text((11, "hook")), HookContent(60, dyingScript: 8), HookRandom());
        script.Start("shared", "npc", 28);
        var battle = script.PendingBattle!;
        battle.Lifecycle.SelectAttack(0, 0);
        battle.Lifecycle.SelectAttack(1, 0);
        Assert.True(battle.Lifecycle.ResolveNextAction());
        Assert.Equal(16, battle.Lifecycle.Current.Roles[1].Hp);

        script.StartBattleHook();
        Assert.Equal(17, battle.Lifecycle.Current.Roles[1].Hp);
        Assert.Equal(17, battle.Lifecycle.Current.Combat.Players[1].Hp);
        Assert.Equal(100, battle.Lifecycle.Current.Roles[0].Hp);
        Assert.Equal(TriggerScriptPhase.WaitingForBattle, script.Phase);
        Assert.Null(script.RunningBattleHook);
        Assert.Equal(28, script.Caller);
        Assert.Equal(1, script.ReturnEntry);
    }

    [Fact]
    public void Single_role_hook_call_rejects_undefined_participant_before_prefix()
    {
        var fixture = Layout(16);
        Set(fixture, 1, 7, """{"op":"startBattle","enemyTeamId":18,"lossTarget":null,"isBoss":true,"fleeTarget":null}""", 15);
        Set(fixture, 8, 4, """{"op":"call","to":"L000C","eventObjectId":9}""", 15);
        Set(fixture, 12, 27, """{"op":"changeHp","scope":0,"amount":1}""", 15);
        var script = new TriggerScriptSession(fixture.Load(), Text((11, "hook")), HookContent(60, dyingScript: 8), HookRandom());
        var error = Assert.Throws<InvalidDataException>(() => script.Start("shared", "npc", 28));
        Assert.Contains("role hook caller 9", error.Message);
        Assert.Equal(TriggerScriptPhase.Ready, script.Phase);
        Assert.Null(script.PendingBattle);
    }

    [Fact]
    public void Role_hook_nested_call_rejects_nonparticipant_before_prefix()
    {
        var fixture = Layout(18);
        Set(fixture, 1, 7, """{"op":"startBattle","enemyTeamId":18,"lossTarget":null,"isBoss":true,"fleeTarget":null}""", 17);
        Set(fixture, 8, 4, """{"op":"call","to":"L000C","eventObjectId":0}""", 17);
        Set(fixture, 12, 4, """{"op":"call","to":"L0010","eventObjectId":9}""", 17);
        Set(fixture, 16, 27, """{"op":"changeHp","scope":0,"amount":1}""", 17);
        var script = new TriggerScriptSession(fixture.Load(), Text((11, "hook")), HookContent(60, dyingScript: 8), HookRandom());
        var error = Assert.Throws<InvalidDataException>(() => script.Start("shared", "npc", 28));
        Assert.Contains("role hook caller 9", error.Message);
        Assert.Equal(TriggerScriptPhase.Ready, script.Phase);
        Assert.Null(script.PendingBattle);
    }

    [Fact]
    public void Role_hook_callee_counterfree_control_returns_to_its_owned_hook()
    {
        var fixture = Layout(16);
        Set(fixture, 1, 7, """{"op":"startBattle","enemyTeamId":18,"lossTarget":null,"isBoss":true,"fleeTarget":null}""", 15);
        Set(fixture, 8, 4, """{"op":"call","to":"L000C","eventObjectId":0}""", 15);
        Set(fixture, 9, 0xFFFF, """{"op":"showDialog","messageId":11,"descriptionLineWord":0}""", 15);
        Set(fixture, 12, 1, """{"op":"end","mode":"advance"}""", 15);
        var script = new TriggerScriptSession(fixture.Load(), Text((11, "hook")), HookContent(60, dyingScript: 8), HookRandom());
        script.Start("shared", "npc", 28);
        var battle = script.PendingBattle!;
        battle.Lifecycle.SelectAttack(0, 0);
        battle.Lifecycle.SelectAttack(1, 0);
        Assert.True(battle.Lifecycle.ResolveNextAction());

        script.StartBattleHook();
        var display = Assert.IsType<TriggerDialogueRequest>(script.PendingDisplay);
        Assert.Equal(11, display.Content.StartMessageId);
        Assert.Equal(1, script.Caller);
        Assert.Equal(1, script.ReturnEntry);
        Advance(display);
        Assert.True(script.AcknowledgeDisplay(display.Id));
        Assert.Equal(TriggerScriptPhase.WaitingForBattle, script.Phase);
        Assert.Equal(28, script.Caller);
        Assert.Equal(1, script.ReturnEntry);
    }

    [Theory]
    [InlineData("battle")]
    [InlineData("foreign-boost")]
    public void Role_hook_control_extension_preserves_rejections_before_prefix(string fault)
    {
        var fixture = Layout(16);
        Set(fixture, 1, 7, """{"op":"startBattle","enemyTeamId":18,"lossTarget":null,"isBoss":true,"fleeTarget":null}""", 15);
        Set(fixture, 8, 4, """{"op":"call","to":"L000C","eventObjectId":0}""", 15);
        if (fault == "battle")
            Set(fixture, 12, 7, """{"op":"startBattle","enemyTeamId":18,"lossTarget":null,"isBoss":true,"fleeTarget":null}""", 15);
        else
            Set(fixture, 12, 0x30, """{"op":"boostStat","field":17,"percent":5,"roleOperand":1}""", 15);
        var script = new TriggerScriptSession(fixture.Load(), Text((11, "hook")), HookContent(60, dyingScript: 8), HookRandom());
        var error = Assert.Throws<InvalidDataException>(() => script.Start("shared", "npc", 28));
        Assert.Contains(fault == "battle" ? "unsupported trigger command/context 'startBattle'" : "boostStat must target the role hook's caller", error.Message);
        Assert.Equal(TriggerScriptPhase.Ready, script.Phase);
        Assert.Null(script.PendingBattle);
    }

    [Fact]
    public void Role_hook_nested_and_last_caller_calls_route_without_restoring_last()
    {
        // fight.c 768-880: the friend-death script runs with the coverer's role word; a callee's
        // literal caller routes its single-role effects, and the return restores only the caller.
        var fixture = Layout(16);
        Set(fixture, 1, 7, """{"op":"startBattle","enemyTeamId":18,"lossTarget":null,"isBoss":true,"fleeTarget":null}""", 15);
        Set(fixture, 8, 4, """{"op":"call","to":"L000C","eventObjectId":1}""", 15);
        Set(fixture, 9, 0xFFFF, """{"op":"showDialog","messageId":11,"descriptionLineWord":0}""", 15);
        Set(fixture, 12, 0x30, """{"op":"boostStat","field":17,"percent":10,"roleOperand":0}""", 15);
        var script = new TriggerScriptSession(fixture.Load(), Text((11, "hook")),
            HookContent(40, dyingScript: 0, friendDeathScript: 8), HookRandom());
        script.Start("shared", "npc", 28);
        var battle = script.PendingBattle!;
        battle.Lifecycle.SelectAttack(0, 0);
        battle.Lifecycle.SelectAttack(1, 0);
        Assert.True(battle.Lifecycle.ResolveNextAction());
        Assert.Equal(new ClassicBattleRoleHook(ClassicBattleHookKind.FriendDeath, 0, 0, 8), script.PendingBattleHook);

        script.StartBattleHook();
        Assert.Equal(0, script.Caller); // the nested literal role1 was returned from
        Assert.True(battle.Lifecycle.Current.Combat.Players[1].BoostAttack > 0);
        Assert.Equal(0, battle.Lifecycle.Current.Combat.Players[0].BoostAttack);
        var display = Assert.IsType<TriggerDialogueRequest>(script.PendingDisplay);
        Assert.Equal(11, display.Content.StartMessageId);
        Advance(display);
        Assert.True(script.AcknowledgeDisplay(display.Id));
        Assert.Equal(TriggerScriptPhase.WaitingForBattle, script.Phase);
        Assert.Equal(28, script.Caller);
    }

    [Fact]
    public void Role_hook_FFFF_call_resolves_the_owning_role_instead_of_literal_65535()
    {
        // fight.c 768-880 invokes the hook as its own PAL_RunTriggerScript with the member's role
        // word. A nested entry reached through operand word 0xFFFF resolves it to that global last
        // event object, so the callee routes its single-role effect to the owning participant
        // instead of satisfying the participant check with literal 65535.
        var fixture = Layout(16);
        Set(fixture, 1, 7, """{"op":"startBattle","enemyTeamId":18,"lossTarget":null,"isBoss":true,"fleeTarget":null}""", 15);
        Set(fixture, 8, 4, """{"op":"call","to":"L000C","eventObjectId":65535}""", 15);
        Set(fixture, 9, 0xFFFF, """{"op":"showDialog","messageId":11,"descriptionLineWord":0}""", 15);
        Set(fixture, 12, 0x30, """{"op":"boostStat","field":17,"percent":10,"roleOperand":0}""", 15);
        var script = new TriggerScriptSession(fixture.Load(), Text((11, "hook")),
            HookContent(60, dyingScript: 8), HookRandom());
        script.Start("shared", "npc", 28);
        var battle = script.PendingBattle!;
        battle.Lifecycle.SelectAttack(0, 0);
        battle.Lifecycle.SelectAttack(1, 0);
        Assert.True(battle.Lifecycle.ResolveNextAction());
        Assert.Equal(new ClassicBattleRoleHook(ClassicBattleHookKind.Dying, 1, 1, 8), script.PendingBattleHook);

        script.StartBattleHook();
        Assert.Equal(1, script.Caller);
        Assert.True(battle.Lifecycle.Current.Combat.Players[1].BoostAttack > 0);
        Assert.Equal(0, battle.Lifecycle.Current.Combat.Players[0].BoostAttack);
        var display = Assert.IsType<TriggerDialogueRequest>(script.PendingDisplay);
        Assert.Equal(11, display.Content.StartMessageId);
        Advance(display);
        Assert.True(script.AcknowledgeDisplay(display.Id));
        Assert.Equal(TriggerScriptPhase.WaitingForBattle, script.Phase);
        Assert.Equal(28, script.Caller);
        Assert.Equal(1, script.ReturnEntry);
    }

    [Fact]
    public void Battle_end_ordinary_calls_resume_the_owned_end_script()
    {
        var fixture = Layout(16);
        Set(fixture, 1, 7, """{"op":"startBattle","enemyTeamId":18,"lossTarget":null,"isBoss":true,"fleeTarget":null}""", 15);
        Set(fixture, 8, 4, """{"op":"call","to":"L000C","eventObjectId":0}""", 15);
        Set(fixture, 12, 0xFFFF, """{"op":"showDialog","messageId":10,"descriptionLineWord":0}""", 15);
        var script = new TriggerScriptSession(fixture.Load(), Text((10, "end")),
            EndContent(8), new WinRandom());
        script.Start("shared", "npc", 28);
        var battle = script.PendingBattle!;
        battle.Lifecycle.SelectAttack(7);
        Assert.True(battle.Lifecycle.ResolveNextAction());
        Assert.Equal(ClassicBattlePhase.Won, battle.Lifecycle.Current.Combat.Phase);
        Assert.Equal(new ClassicBattleEndScript(0, 7, 8), battle.Lifecycle.PendingBattleEndScript);

        script.StartBattleEndScript();
        Assert.Equal(0, script.Caller);
        Assert.True(script.ScriptSuccess);
        Assert.Equal(1, script.ReturnEntry);
        var display = Assert.IsType<TriggerDialogueRequest>(script.PendingDisplay);
        Assert.Equal(10, display.Content.StartMessageId);
        Advance(display);
        Assert.True(script.AcknowledgeDisplay(display.Id));
        Assert.Equal(TriggerScriptPhase.WaitingForBattle, script.Phase);
        Assert.Null(battle.Lifecycle.PendingBattleEndScript);
        Assert.Null(script.RunningBattleEndScript);
        Assert.Equal(28, script.Caller);
        Assert.True(script.AcknowledgeBattle(battle.Id, battle.Lifecycle.Completion));
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
    }

    [Fact]
    public void Battle_end_double_enemy_control_uses_actual_end_slot_one()
    {
        var fixture = Layout(30);
        Set(fixture, 1, 7, """{"op":"startBattle","enemyTeamId":18,"lossTarget":null,"isBoss":true,"fleeTarget":null}""", 29);
        // The settled event continuation calls 0xFFFF after the battle: the second end script is
        // the last battle-owned invocation, so the inherited caller is slot 1, not pre-battle 28.
        Set(fixture, 2, 4, """{"op":"call","to":"L0004","eventObjectId":65535}""", 29);
        Set(fixture, 3, 0, """{"op":"end","mode":"keep"}""", 29);
        Set(fixture, 4, 3, """{"op":"goto","to":"L0006","limit":2}""", 29);
        Set(fixture, 6, 0xFFFF, """{"op":"showDialog","messageId":12,"descriptionLineWord":0}""", 29);
        Set(fixture, 7, 0, """{"op":"end","mode":"keep"}""", 29);
        Set(fixture, 20, 0xFFFF, """{"op":"showDialog","messageId":10,"descriptionLineWord":0}""", 29);
        Set(fixture, 24, 0xFFFF, """{"op":"showDialog","messageId":11,"descriptionLineWord":0}""", 29);
        var role = BattleRole(1, 101, 150, 21, 50, 0);
        var content = new TriggerBattleContent([role], [new ClassicBattlePlayer(1, 101, 150, 40, 20, 100)],
            new Dictionary<int, ClassicBattleGrowthData> { [1] = SimpleGrowth() },
            new Dictionary<int, IReadOnlyList<ClassicBattleEnemy>>
            {
                [18] = [new(7, 0, 43, -36, 0, 500, 1) { BattleEndScript = 20 }, new(7, 0, 43, -36, 0, 500, 1) { BattleEndScript = 24 }]
            });
        Draw[] Strike() => [I(0, 0, 0), I(0, 16, 5), I(0, 2, 0), I(0, 1, 0), I(1, 10, 1)];
        Draw[] Hit() => [I(1, 2, 2), I(0, 5, 5), F(1, 1.125f, 1), I(2, 3, 2)];
        var random = new ScriptedRandom([F(.9f, 1.1f, 1), F(.9f, 1.1f, 1), F(.9f, 1.1f, 1),
            .. Strike(), .. Strike(), .. Hit(), F(.9f, 1.1f, 1), F(.9f, 1.1f, 1), .. Strike(), .. Hit()]);
        var world = BattleWorld(28, 1);
        var script = new TriggerScriptSession(fixture.Load(), Text((10, "slot zero"), (11, "slot one"), (12, "post")), content, random,
            world);
        script.Start("shared", "npc", 28);
        var battle = script.PendingBattle!;
        battle.Lifecycle.SelectAttack(0, 0);
        while (battle.Lifecycle.Completion is null && battle.Lifecycle.PendingBattleEndScript is null)
            if (!battle.Lifecycle.ResolveNextAction()) break;
        if (battle.Lifecycle.PendingBattleEndScript is null && battle.Lifecycle.Completion is null) battle.Lifecycle.SelectAttack(0, 1);
        while (battle.Lifecycle.Completion is null && battle.Lifecycle.PendingBattleEndScript is null)
            if (!battle.Lifecycle.ResolveNextAction()) break;
        Assert.Equal(new ClassicBattleEndScript(0, 7, 20), battle.Lifecycle.PendingBattleEndScript);

        script.StartBattleEndScript();
        Assert.Equal(0, script.Caller);
        var first = Assert.IsType<TriggerDialogueRequest>(script.PendingDisplay);
        Advance(first);
        Assert.True(script.AcknowledgeDisplay(first.Id));
        Assert.Equal(new ClassicBattleEndScript(1, 7, 24), battle.Lifecycle.PendingBattleEndScript);

        script.StartBattleEndScript();
        Assert.Equal(1, script.Caller); // the actual second enemy slot, not a stale zero
        var second = Assert.IsType<TriggerDialogueRequest>(script.PendingDisplay);
        Advance(second);
        Assert.True(script.AcknowledgeDisplay(second.Id));
        Assert.Null(battle.Lifecycle.PendingBattleEndScript);
        Assert.Equal(ClassicBattlePhase.Won, battle.Lifecycle.Completion!.Outcome);
        Assert.Equal(TriggerScriptPhase.WaitingForBattle, script.Phase);
        Assert.Equal(28, script.Caller);
        Assert.Equal(1, script.ReturnEntry);
        Assert.True(script.AcknowledgeBattle(battle.Id, battle.Lifecycle.Completion));
        var post = Assert.IsType<TriggerDialogueRequest>(script.PendingDisplay);
        Assert.Equal(1, script.Caller); // the slot word the last end script left in wLastEventObject
        Assert.Equal(12, Assert.Single(post.MessageReferences).StartMessageId);
        Advance(post);
        Assert.True(script.AcknowledgeDisplay(post.Id));
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
        Assert.Equal(28, script.Caller);
        Assert.Equal(1, world.Objects.Single(o => o.Id == 1).TriggerIdleCount);
        Assert.Equal(0, world.Objects.Single(o => o.Id == 28).TriggerIdleCount);
    }

    [Fact]
    public void Post_battle_ffff_counted_gate_uses_the_battle_owned_enemy_slot_owner()
    {
        // script.c 3163-3182/3263-3267 with battle.c 1334-1337: the enemy end script is its own
        // PAL_RunTriggerScript entry and leaves wLastEventObject at that slot word, so a 0xFFFF
        // call in the settled event continuation resolves through the slot and owns its trigger
        // counter, not the pre-battle caller word (which stays untouched).
        var world = BattleWorld(28, 1);
        var script = new TriggerScriptSession(PostBattleLastCallerFixture().Load(),
            Text((11, "slot one"), (12, "post")), PostBattleEndContent(24), PostBattleRandom(), world);
        script.Start("shared", "npc", 28);
        var battle = script.PendingBattle!;
        ResolveToPendingEndScript(battle);
        Assert.Equal(new ClassicBattleEndScript(1, 7, 24), battle.Lifecycle.PendingBattleEndScript);

        script.StartBattleEndScript();
        Assert.Equal(1, script.Caller);
        var end = Assert.IsType<TriggerDialogueRequest>(script.PendingDisplay);
        Assert.Equal(11, end.Content.StartMessageId);
        Advance(end);
        Assert.True(script.AcknowledgeDisplay(end.Id));
        Assert.Null(battle.Lifecycle.PendingBattleEndScript);
        Assert.Equal(ClassicBattlePhase.Won, battle.Lifecycle.Completion!.Outcome);

        Assert.True(script.AcknowledgeBattle(battle.Id, battle.Lifecycle.Completion));
        var post = Assert.IsType<TriggerDialogueRequest>(script.PendingDisplay);
        Assert.Equal(1, script.Caller); // the last event object the end-script invocation left
        Assert.Equal(12, Assert.Single(post.MessageReferences).StartMessageId);
        Advance(post);
        Assert.True(script.AcknowledgeDisplay(post.Id));
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
        Assert.Equal(28, script.Caller); // a return still restores only the current caller word
        Assert.Equal(1, world.Objects.Single(o => o.Id == 1).TriggerIdleCount);
        Assert.Equal(0, world.Objects.Single(o => o.Id == 28).TriggerIdleCount);
    }

    [Fact]
    public void Post_battle_ffff_counted_gate_rejects_a_missing_battle_owned_owner_before_starting()
    {
        // Without the battle-owned owner in the closure, Start would accept the pre-battle word 28,
        // begin the battle, and only then fail inside the counted gate after the enemy end script.
        var script = new TriggerScriptSession(PostBattleLastCallerFixture().Load(),
            Text((11, "slot one"), (12, "post")), PostBattleEndContent(24), PostBattleRandom(), BattleWorld(28));
        var error = Assert.Throws<InvalidDataException>(() => script.Start("shared", "npc", 28));
        Assert.Contains("counted goto", error.Message);
        Assert.Contains("event object 1", error.Message);
        Assert.Equal(TriggerScriptPhase.Ready, script.Phase);
        Assert.Null(script.PendingBattle);
        Assert.Null(script.PendingDisplay);
    }

    [Fact]
    public void Post_battle_ffff_counted_gate_rejects_a_missing_pre_battle_owner_before_starting()
    {
        // The pre-battle word stays reachable when no battle-owned script runs (a lost or escaped
        // battle, or empty hook seeds), so the closure requires every possible owner, not just the
        // runtime one: a battle that could settle on owner 28 must not start without it.
        var script = new TriggerScriptSession(PostBattleLastCallerFixture().Load(),
            Text((11, "slot one"), (12, "post")), PostBattleEndContent(24), PostBattleRandom(), BattleWorld(1));
        var error = Assert.Throws<InvalidDataException>(() => script.Start("shared", "npc", 28));
        Assert.Contains("counted goto", error.Message);
        Assert.Contains("event object 28", error.Message);
        Assert.Equal(TriggerScriptPhase.Ready, script.Phase);
        Assert.Null(script.PendingBattle);
    }

    [Fact]
    public void Post_battle_ffff_counted_gate_rejects_a_missing_role_hook_owner_before_starting()
    {
        // fight.c 768-880 rewrites wLastEventObject to the hook's role word, so the same 0xFFFF
        // continuation must also be preflighted against the dying hook's participant word.
        var fixture = Layout(16);
        Set(fixture, 1, 7, """{"op":"startBattle","enemyTeamId":18,"lossTarget":null,"isBoss":true,"fleeTarget":null}""", 15);
        Set(fixture, 2, 4, """{"op":"call","to":"L0004","eventObjectId":65535}""", 15);
        Set(fixture, 3, 0, """{"op":"end","mode":"keep"}""", 15);
        Set(fixture, 4, 3, """{"op":"goto","to":"L0007","limit":2}""", 15);
        Set(fixture, 8, 0, """{"op":"end","mode":"keep"}""", 15);
        var script = new TriggerScriptSession(fixture.Load(), Text((12, "post")),
            HookContent(60, dyingScript: 8), HookRandom(), BattleWorld(28));
        var error = Assert.Throws<InvalidDataException>(() => script.Start("shared", "npc", 28));
        Assert.Contains("counted goto", error.Message);
        Assert.Contains("event object 1", error.Message);
        Assert.Equal(TriggerScriptPhase.Ready, script.Phase);
        Assert.Null(script.PendingBattle);
    }

    [Fact]
    public void Post_battle_ffff_after_a_call_frame_uses_the_battle_owned_owner()
    {
        // script.c 3263-3267 with battle.c 1334-1337: the event's 0x0004 frame parks on startBattle,
        // the enemy end script leaves its slot word in wLastEventObject, the callee then returns,
        // and the caller's own 0xFFFF call runs with that word, never the pre-battle callee word.
        var world = BattleWorld(28, 1);
        var script = new TriggerScriptSession(PostBattleCallFrameFixture().Load(),
            Text((11, "slot one"), (12, "post")), PostBattleEndContent(24), PostBattleRandom(), world);
        script.Start("shared", "npc", 28);
        var battle = script.PendingBattle!;
        ResolveToPendingEndScript(battle);
        Assert.Equal(new ClassicBattleEndScript(1, 7, 24), battle.Lifecycle.PendingBattleEndScript);

        script.StartBattleEndScript();
        var end = Assert.IsType<TriggerDialogueRequest>(script.PendingDisplay);
        Assert.Equal(11, end.Content.StartMessageId);
        Advance(end);
        Assert.True(script.AcknowledgeDisplay(end.Id));
        Assert.Null(battle.Lifecycle.PendingBattleEndScript);
        Assert.Equal(ClassicBattlePhase.Won, battle.Lifecycle.Completion!.Outcome);

        Assert.True(script.AcknowledgeBattle(battle.Id, battle.Lifecycle.Completion));
        var post = Assert.IsType<TriggerDialogueRequest>(script.PendingDisplay);
        Assert.Equal(1, script.Caller); // the slot word the end script left for the resumed caller
        Assert.Equal(12, Assert.Single(post.MessageReferences).StartMessageId);
        Assert.Equal(1, world.Objects.Single(o => o.Id == 1).TriggerIdleCount);
        Assert.Equal(0, world.Objects.Single(o => o.Id == 28).TriggerIdleCount);
        Advance(post);
        Assert.True(script.AcknowledgeDisplay(post.Id));
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
        Assert.Equal(28, script.Caller); // the resumed frame restores only its own caller word
    }

    [Fact]
    public void Post_battle_ffff_after_a_call_frame_rejects_a_missing_battle_owned_owner_before_starting()
    {
        // Without the battle-owned word in the closure, Start would accept the pre-battle callee
        // word 28, begin the battle, and only then fail in the counted gate after the end script.
        var script = new TriggerScriptSession(PostBattleCallFrameFixture().Load(),
            Text((11, "slot one"), (12, "post")), PostBattleEndContent(24), PostBattleRandom(), BattleWorld(28));
        var error = Assert.Throws<InvalidDataException>(() => script.Start("shared", "npc", 28));
        Assert.Contains("counted goto", error.Message);
        Assert.Contains("event object 1", error.Message);
        Assert.Equal(TriggerScriptPhase.Ready, script.Phase);
        Assert.Null(script.PendingBattle);
        Assert.Null(script.PendingDisplay);
    }

    [Fact]
    public void Post_battle_ffff_after_a_call_frame_keeps_the_pre_battle_callee_owner_required()
    {
        // A battle with no hook or end script leaves the pre-battle word, so the resumed caller's
        // 0xFFFF call can still own the callee word 28 and that owner must stay preflighted.
        var script = new TriggerScriptSession(PostBattleCallFrameFixture().Load(),
            Text((11, "slot one"), (12, "post")), PostBattleEndContent(24), PostBattleRandom(), BattleWorld(1));
        var error = Assert.Throws<InvalidDataException>(() => script.Start("shared", "npc", 28));
        Assert.Contains("counted goto", error.Message);
        Assert.Contains("event object 28", error.Message);
        Assert.Equal(TriggerScriptPhase.Ready, script.Phase);
        Assert.Null(script.PendingBattle);
    }

    [Fact]
    public void Battle_end_nested_callee_word_owns_the_post_battle_ffff_gate()
    {
        // battle.c 1334-1337 runs the final enemy end script as its own invocation; its nested
        // 0x0004 callee advances wLastEventObject to 9, which survives both returns and is the
        // owner of the settled event continuation's counted 0xFFFF gate.
        var world = BattleWorld(28, 1, 9);
        var script = new TriggerScriptSession(PostBattleEndCalleeFixture().Load(), Text((12, "post")),
            PostBattleEndContent(24), PostBattleRandom(), world);
        script.Start("shared", "npc", 28);
        var battle = script.PendingBattle!;
        ResolveToPendingEndScript(battle);
        Assert.Equal(new ClassicBattleEndScript(1, 7, 24), battle.Lifecycle.PendingBattleEndScript);

        script.StartBattleEndScript();
        Assert.Equal(TriggerScriptPhase.WaitingForBattle, script.Phase);
        Assert.Null(script.PendingDisplay);
        Assert.Null(script.RunningBattleEndScript);
        Assert.Equal(28, script.Caller);
        Assert.Equal(ClassicBattlePhase.Won, battle.Lifecycle.Completion!.Outcome);

        Assert.True(script.AcknowledgeBattle(battle.Id, battle.Lifecycle.Completion));
        var post = Assert.IsType<TriggerDialogueRequest>(script.PendingDisplay);
        Assert.Equal(9, script.Caller); // the nested callee word the end invocation left
        Assert.Equal(12, Assert.Single(post.MessageReferences).StartMessageId);
        Assert.Equal(1, world.Objects.Single(o => o.Id == 9).TriggerIdleCount);
        Assert.Equal(0, world.Objects.Single(o => o.Id == 1).TriggerIdleCount);
        Advance(post);
        Assert.True(script.AcknowledgeDisplay(post.Id));
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
        Assert.Equal(28, script.Caller);
    }

    [Fact]
    public void Battle_end_nested_callee_word_is_required_before_the_battle_starts()
    {
        var script = new TriggerScriptSession(PostBattleEndCalleeFixture().Load(), Text((12, "post")),
            PostBattleEndContent(24), PostBattleRandom(), BattleWorld(28, 1));
        var error = Assert.Throws<InvalidDataException>(() => script.Start("shared", "npc", 28));
        Assert.Contains("counted goto", error.Message);
        Assert.Contains("event object 9", error.Message);
        Assert.Equal(TriggerScriptPhase.Ready, script.Phase);
        Assert.Null(script.PendingBattle);
    }

    [Fact]
    public void Callee_goto_zero_returns_to_the_outer_continuation_whose_ffff_gate_uses_the_nested_callee_word()
    {
        // script.c 3196/3263-3267: a callee's 0x0003 word zero ends its frame and resumes the
        // caller continuation, whose 0xFFFF call then owns the nested callee word 70 left in
        // wLastEventObject, never the outer callee word 28 the eager pre-battle walk walked.
        var world = BattleWorld(28, 70);
        var script = new TriggerScriptSession(GotoZeroReturnFixture().Load(), Text((11, "callee"), (12, "outer")), world);
        script.Start("shared", "npc", 28);
        var callee = Assert.IsType<TriggerDialogueRequest>(script.PendingDisplay);
        Assert.Equal(11, Assert.Single(callee.MessageReferences).StartMessageId);
        Advance(callee);
        Assert.True(script.AcknowledgeDisplay(callee.Id));

        var outer = Assert.IsType<TriggerDialogueRequest>(script.PendingDisplay);
        Assert.Equal(12, Assert.Single(outer.MessageReferences).StartMessageId);
        Assert.Equal(70, script.Caller);
        Assert.Equal(1, world.Objects.Single(o => o.Id == 70).TriggerIdleCount);
        Assert.Equal(0, world.Objects.Single(o => o.Id == 28).TriggerIdleCount);
        Advance(outer);
        Assert.True(script.AcknowledgeDisplay(outer.Id));
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
        Assert.Equal(28, script.Caller);
    }

    [Fact]
    public void Callee_goto_zero_rejects_a_missing_nested_callee_owner_before_the_outer_prefix()
    {
        // Without the frame-ending goto in the closure, Start would validate only the outer callee
        // word 28 and park on the callee display before the resumed caller's counted gate failed.
        var script = new TriggerScriptSession(GotoZeroReturnFixture().Load(), Text((11, "callee"), (12, "outer")), BattleWorld(28));
        var error = Assert.Throws<InvalidDataException>(() => script.Start("shared", "npc", 28));
        Assert.Contains("counted goto", error.Message);
        Assert.Contains("event object 70", error.Message);
        Assert.Equal(TriggerScriptPhase.Ready, script.Phase);
        Assert.Null(script.PendingDisplay);
    }

    private static EventFixture PostBattleCallFrameFixture()
    {
        // L0001 call -> L0005 startBattle -> L0006 end keep returns to L0002, whose 0xFFFF call
        // owns the counted goto at L0008; L0018 is the second enemy's post-victory script.
        var fixture = Layout(30);
        Set(fixture, 1, 4, """{"op":"call","to":"L0005","eventObjectId":0}""", 29);
        Set(fixture, 2, 4, """{"op":"call","to":"L0008","eventObjectId":65535}""", 29);
        Set(fixture, 3, 0, """{"op":"end","mode":"keep"}""", 29);
        Set(fixture, 5, 7, """{"op":"startBattle","enemyTeamId":18,"lossTarget":null,"isBoss":true,"fleeTarget":null}""", 29);
        Set(fixture, 6, 0, """{"op":"end","mode":"keep"}""", 29);
        Set(fixture, 8, 3, """{"op":"goto","to":"L000B","limit":2}""", 29);
        Set(fixture, 11, 0xFFFF, """{"op":"showDialog","messageId":12,"descriptionLineWord":0}""", 29);
        Set(fixture, 12, 0, """{"op":"end","mode":"keep"}""", 29);
        Set(fixture, 24, 0xFFFF, """{"op":"showDialog","messageId":11,"descriptionLineWord":0}""", 29);
        return fixture;
    }

    private static EventFixture PostBattleEndCalleeFixture()
    {
        // The last enemy end script (slot 1) calls the literal caller 9 and ends; the settled event
        // continuation then calls 0xFFFF into the counted gate at L0008.
        var fixture = Layout(32);
        Set(fixture, 1, 7, """{"op":"startBattle","enemyTeamId":18,"lossTarget":null,"isBoss":true,"fleeTarget":null}""", 31);
        Set(fixture, 2, 4, """{"op":"call","to":"L0008","eventObjectId":65535}""", 31);
        Set(fixture, 3, 0, """{"op":"end","mode":"keep"}""", 31);
        Set(fixture, 8, 3, """{"op":"goto","to":"L000B","limit":2}""", 31);
        Set(fixture, 11, 0xFFFF, """{"op":"showDialog","messageId":12,"descriptionLineWord":0}""", 31);
        Set(fixture, 12, 0, """{"op":"end","mode":"keep"}""", 31);
        Set(fixture, 24, 4, """{"op":"call","to":"L001A","eventObjectId":9}""", 31);
        Set(fixture, 26, 0, """{"op":"end","mode":"keep"}""", 31);
        return fixture;
    }

    private static EventFixture PostBattleLastCallerFixture()
    {
        // L0001 startBattle -> L0002 call FFFF -> L0008 counted goto limit 2 -> L000B display 12;
        // L0018 is the second enemy's post-victory script.
        var fixture = Layout(30);
        Set(fixture, 1, 7, """{"op":"startBattle","enemyTeamId":18,"lossTarget":null,"isBoss":true,"fleeTarget":null}""", 29);
        Set(fixture, 2, 4, """{"op":"call","to":"L0008","eventObjectId":65535}""", 29);
        Set(fixture, 3, 0, """{"op":"end","mode":"keep"}""", 29);
        Set(fixture, 8, 3, """{"op":"goto","to":"L000B","limit":2}""", 29);
        Set(fixture, 11, 0xFFFF, """{"op":"showDialog","messageId":12,"descriptionLineWord":0}""", 29);
        Set(fixture, 12, 0, """{"op":"end","mode":"keep"}""", 29);
        Set(fixture, 24, 0xFFFF, """{"op":"showDialog","messageId":11,"descriptionLineWord":0}""", 29);
        return fixture;
    }

    private static EventFixture GotoZeroReturnFixture()
    {
        // L0001 call L0005 (inherit 28) -> L0005 call L0009 (literal 70) -> L0009/L000A display 11
        // then end keep -> L0006 goto L0000 ends the frame -> L0002 call FFFF -> L000B counted
        // goto limit 2 owns the nested callee word 70 -> L000F display 12.
        var fixture = Layout(17);
        Set(fixture, 1, 4, """{"op":"call","to":"L0005","eventObjectId":0}""", 16);
        Set(fixture, 2, 4, """{"op":"call","to":"L000B","eventObjectId":65535}""", 16);
        Set(fixture, 3, 0, """{"op":"end","mode":"keep"}""", 16);
        Set(fixture, 5, 4, """{"op":"call","to":"L0009","eventObjectId":70}""", 16);
        Set(fixture, 6, 3, """{"op":"goto","to":"L0000","limit":0}""", 16);
        Set(fixture, 9, 0xFFFF, """{"op":"showDialog","messageId":11,"descriptionLineWord":0}""", 16);
        Set(fixture, 10, 0, """{"op":"end","mode":"keep"}""", 16);
        Set(fixture, 11, 3, """{"op":"goto","to":"L000F","limit":2}""", 16);
        Set(fixture, 15, 0xFFFF, """{"op":"showDialog","messageId":12,"descriptionLineWord":0}""", 16);
        Set(fixture, 16, 0, """{"op":"end","mode":"keep"}""", 16);
        return fixture;
    }

    private static TriggerBattleContent PostBattleEndContent(int endScript)
    {
        var role = BattleRole(1, 101, 150, 21, 50, 0);
        return new TriggerBattleContent([role], [new ClassicBattlePlayer(1, 101, 150, 40, 20, 100)],
            new Dictionary<int, ClassicBattleGrowthData> { [1] = SimpleGrowth() },
            new Dictionary<int, IReadOnlyList<ClassicBattleEnemy>>
            {
                [18] = [new(7, 0, 43, -36, 0, 500, 1), new(7, 0, 43, -36, 0, 500, 1) { BattleEndScript = endScript }]
            });
    }

    private static ScriptedRandom PostBattleRandom()
    {
        Draw[] Strike() => [I(0, 0, 0), I(0, 16, 5), I(0, 2, 0), I(0, 1, 0), I(1, 10, 1)];
        Draw[] Hit() => [I(1, 2, 2), I(0, 5, 5), F(1, 1.125f, 1), I(2, 3, 2)];
        return new ScriptedRandom([F(.9f, 1.1f, 1), F(.9f, 1.1f, 1), F(.9f, 1.1f, 1),
            .. Strike(), .. Strike(), .. Hit(), F(.9f, 1.1f, 1), F(.9f, 1.1f, 1), .. Strike(), .. Hit()]);
    }

    private static void ResolveToPendingEndScript(TriggerBattleRequest battle)
    {
        battle.Lifecycle.SelectAttack(0, 0);
        while (battle.Lifecycle.Completion is null && battle.Lifecycle.PendingBattleEndScript is null)
            if (!battle.Lifecycle.ResolveNextAction()) break;
        if (battle.Lifecycle.PendingBattleEndScript is null && battle.Lifecycle.Completion is null) battle.Lifecycle.SelectAttack(0, 1);
        while (battle.Lifecycle.Completion is null && battle.Lifecycle.PendingBattleEndScript is null)
            if (!battle.Lifecycle.ResolveNextAction()) break;
    }

    [Theory]
    [InlineData("battle")]
    [InlineData("role-effect")]
    public void Battle_end_call_extension_preserves_rejections_before_prefix(string fault)
    {
        var fixture = Layout(12);
        Set(fixture, 1, 7, """{"op":"startBattle","enemyTeamId":18,"lossTarget":null,"isBoss":true,"fleeTarget":null}""", 11);
        if (fault == "battle")
            Set(fixture, 8, 7, """{"op":"startBattle","enemyTeamId":18,"lossTarget":null,"isBoss":true,"fleeTarget":null}""", 11);
        else
            Set(fixture, 8, 27, """{"op":"changeHp","scope":0,"amount":10}""", 11);
        var script = new TriggerScriptSession(fixture.Load(), Text((10, "end")), EndContent(8), new WinRandom());
        var error = Assert.Throws<InvalidDataException>(() => script.Start("shared", "npc", 28));
        Assert.Contains(fault == "battle" ? "unsupported trigger command/context 'startBattle'" : "changeHp is only supported inside a battle role hook", error.Message);
        Assert.Equal(TriggerScriptPhase.Ready, script.Phase);
        Assert.Null(script.PendingBattle);
    }

    [Fact]
    public void Call_parked_on_a_battle_role_hook_keeps_the_event_frame_and_writes_back_the_hook_return_entry()
    {
        // fight.c 768-880 invokes PAL_RunTriggerScript recursively: the hook gets its own
        // wNextScriptEntry and call depth even when the parking startBattle is reached from a
        // 0x0004 callee, so the hook's 0x0001 return entry is published to the Name-owned slot
        // instead of being suppressed by the outer event frame.
        var fixture = Layout(14);
        Set(fixture, 1, 4, """{"op":"call","to":"L0005","eventObjectId":0}""", 13);
        Set(fixture, 2, 0, """{"op":"end","mode":"keep"}""", 13);
        Set(fixture, 5, 7, """{"op":"startBattle","enemyTeamId":18,"lossTarget":null,"isBoss":true,"fleeTarget":null}""", 13);
        Set(fixture, 6, 1, """{"op":"end","mode":"advance"}""", 13);
        Set(fixture, 8, 0xFFFF, """{"op":"showDialog","messageId":11,"descriptionLineWord":0}""", 13);
        Set(fixture, 9, 1, """{"op":"end","mode":"advance"}""", 13);
        var content = new TriggerBattleContent(
            [BattleRole(0, 100, 200, 20, 50, 2), BattleRole(1, 60, 240, 10, 240, 0)],
            [new ClassicBattlePlayer(0, 100, 200, 40, 20, 100)
                { CoveredByRoleId = 2, NameObjectId = 6, FriendDeathScript = 0, DyingScript = 0 },
             new ClassicBattlePlayer(1, 60, 240, 20, 0, 100)
                { CoveredByRoleId = 0, NameObjectId = 7, FriendDeathScript = 0, DyingScript = 8 }],
            new Dictionary<int, ClassicBattleGrowthData> { [0] = SimpleGrowth(), [1] = SimpleGrowth() },
            new Dictionary<int, IReadOnlyList<ClassicBattleEnemy>> { [18] = [new(7, 0, 43, 8, 0, 500, 1)] });
        var world = World(6, 7);
        var script = new TriggerScriptSession(fixture.Load(), Text((11, "hook")), content, HookRandom(), world);
        script.Start("shared", "npc", 28);
        Assert.Equal(8, world.ObjectScripts[(7, 1)]); // the declared dying seed, before the hook runs
        var battle = script.PendingBattle!;
        battle.Lifecycle.SelectAttack(0, 0);
        battle.Lifecycle.SelectAttack(1, 0);
        Assert.True(battle.Lifecycle.ResolveNextAction());
        Assert.Equal(new ClassicBattleRoleHook(ClassicBattleHookKind.Dying, 1, 1, 8), script.PendingBattleHook);
        Assert.Equal(1, script.ReturnEntry); // still the parked event entry word

        script.StartBattleHook();
        var display = Assert.IsType<TriggerDialogueRequest>(script.PendingDisplay);
        Assert.Equal(11, display.Content.StartMessageId);
        Advance(display);
        Assert.True(script.AcknowledgeDisplay(display.Id));

        Assert.Equal(TriggerScriptPhase.WaitingForBattle, script.Phase);
        Assert.Null(script.RunningBattleHook);
        Assert.Equal(28, script.Caller); // the parked event caller is restored, not the callee frame
        Assert.Equal(1, script.ReturnEntry);
        Assert.Equal(10, world.ObjectScripts[(7, 1)]); // the hook's own 0x0001 return entry
    }

    [Fact]
    public void Call_parked_on_an_enemy_end_script_keeps_the_event_frame_until_the_battle_acknowledges()
    {
        // battle.c 1334-1337 also recurses into PAL_RunTriggerScript: a root end in the end script
        // must not pop the 0x0004 frame that parked the event on startBattle, and the event resumes
        // at its own continuation after the battle receipt.
        var fixture = Layout(14);
        Set(fixture, 1, 4, """{"op":"call","to":"L0005","eventObjectId":0}""", 13);
        Set(fixture, 2, 0, """{"op":"end","mode":"keep"}""", 13);
        Set(fixture, 5, 7, """{"op":"startBattle","enemyTeamId":18,"lossTarget":null,"isBoss":true,"fleeTarget":null}""", 13);
        Set(fixture, 6, 1, """{"op":"end","mode":"advance"}""", 13);
        Set(fixture, 8, 0xFFFF, """{"op":"showDialog","messageId":10,"descriptionLineWord":0}""", 13);
        Set(fixture, 9, 1, """{"op":"end","mode":"advance"}""", 13);
        var script = new TriggerScriptSession(fixture.Load(), Text((10, "end")), EndContent(8), new WinRandom());
        script.Start("shared", "npc", 28);
        var battle = script.PendingBattle!;
        battle.Lifecycle.SelectAttack(7);
        Assert.True(battle.Lifecycle.ResolveNextAction());
        Assert.Equal(ClassicBattlePhase.Won, battle.Lifecycle.Current.Combat.Phase);
        Assert.Equal(new ClassicBattleEndScript(0, 7, 8), battle.Lifecycle.PendingBattleEndScript);

        script.StartBattleEndScript();
        var display = Assert.IsType<TriggerDialogueRequest>(script.PendingDisplay);
        Advance(display);
        Assert.True(script.AcknowledgeDisplay(display.Id));
        Assert.Equal(TriggerScriptPhase.WaitingForBattle, script.Phase);
        Assert.Null(script.RunningBattleEndScript);
        Assert.Equal(28, script.Caller);
        Assert.Equal(1, script.ReturnEntry);

        Assert.True(script.AcknowledgeBattle(battle.Id, battle.Lifecycle.Completion));
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
        // The event frame survived the end script: startBattle continues at L0006, then the
        // callee ends and the outer call resumes at its own L0002 continuation.
        Assert.Equal(new TriggerPosition("shared", "L0002", 2), script.CurrentPosition);
        Assert.Equal(28, script.Caller);
        Assert.Equal(1, script.ReturnEntry);
        Assert.Null(script.PendingBattle);
    }

    private static TriggerBattleContent HookContent(int hp1, int dyingScript, int friendDeathScript = 0) =>
        new([BattleRole(0, 100, 200, 20, 50, 2), BattleRole(1, hp1, 240, 10, 240, 0)],
        [
            new ClassicBattlePlayer(0, 100, 200, 40, 20, 100) { CoveredByRoleId = 2, FriendDeathScript = friendDeathScript },
            new ClassicBattlePlayer(1, hp1, 240, 20, 0, 100) { CoveredByRoleId = 0, DyingScript = dyingScript }
        ],
        new Dictionary<int, ClassicBattleGrowthData> { [0] = SimpleGrowth(), [1] = SimpleGrowth() },
        new Dictionary<int, IReadOnlyList<ClassicBattleEnemy>> { [18] = [new(7, 0, 43, 8, 0, 500, 1)] });

    private static TriggerBattleContent EndContent(int endScript) =>
        new([BattleRole(1, 100, 150, 20, 50, 0)], [new ClassicBattlePlayer(1, 100, 150, 40, 20, 100)],
            new Dictionary<int, ClassicBattleGrowthData> { [1] = SimpleGrowth() },
            new Dictionary<int, IReadOnlyList<ClassicBattleEnemy>> { [18] = [new(7, 0, 43, 0, 0, 0, 1) { BattleEndScript = endScript }] });

    private static BattleRoleSnapshot BattleRole(int id, int hp, int maxHp, int mp, int maxMp, int coveredBy) =>
        new(id, 2, hp, maxHp, mp, maxMp, new(5, 2, 17),
            new Dictionary<string, BattleExperienceSnapshot>
            {
                ["health"] = new(7, 3, 55), ["magic"] = new(7, 3, 55), ["attack"] = new(7, 3, 55),
                ["magicPower"] = new(7, 3, 55), ["defense"] = new(7, 3, 55), ["dexterity"] = new(7, 3, 55), ["flee"] = new(7, 3, 55)
            }, new int[6], new int[32], new int[9], coveredBy);

    private static DynamicEventWorld BattleWorld(params int[] objectIds) => DynamicEventWorld.ForHost(
        new StaticSceneRules(new SceneMap(2, new uint[16384], [new SceneFrame(0, 32, 15)]),
            objectIds.Select(id => new SceneObject((ushort)id, 320, 320, 0, 0, 0, 7, 1, 0, 0)).ToArray()),
        new StaticLeader(320, 320, 0, 0, 0, 3,
            new SceneSprite(17, Enumerable.Range(0, 12).Select(i => new SceneFrame(i, 20, 30)))),
        2, 0, 0);

    private static ScriptedRandom HookRandom() => new([.. Queue3, .. EnemyHit1]);

    private static readonly Draw[] Queue3 = [F(.9f, 1.1f, 1), F(.9f, 1.1f, 1), F(.9f, 1.1f, 1)];
    private static readonly Draw[] EnemyHit1 = [I(0, 1, 1), I(0, 16, 0), I(0, 2, 0), I(0, 1, 0), I(1, 10, 1)];

    private sealed record Draw(bool Floating, float From, float To, float Value);
    private static Draw F(float from, float to, float value) => new(true, from, to, value);
    private static Draw I(int from, int to, int value) => new(false, from, to, value);

    private sealed class ScriptedRandom(IEnumerable<Draw> draws) : IBattleRandom
    {
        private readonly Queue<Draw> remaining = new(draws);
        public int NextInclusive(int from, int to) => (int)Take(false, from, to);
        public float NextInclusive(float from, float to) => Take(true, from, to);
        private float Take(bool floating, float from, float to)
        {
            Assert.NotEmpty(remaining);
            Draw next = remaining.Dequeue();
            Assert.Equal((next.Floating, next.From, next.To), (floating, from, to));
            Assert.InRange(next.Value, from, to);
            return next.Value;
        }
    }

    private sealed class WinRandom : IBattleRandom
    {
        public int NextInclusive(int from, int to) => from == 0 && to == 5 ? 1 : from;
        public float NextInclusive(float from, float to) => 1;
    }

    [Fact]
    public void Root_counted_redirect_to_zero_publishes_the_zero_return_entry()
    {
        // script.c 0x0002 success writes operand0 into wNextScriptEntry even when it is zero
        // (unlike 0x0003 goto, which never publishes a return entry).
        var fixture = Layout(6);
        Set(fixture, 1, 2, """{"op":"end","mode":"redirect","to":"L0000","limit":0}""", 5);
        Set(fixture, 2, 0, """{"op":"end","mode":"keep"}""", 5);
        var script = new TriggerScriptSession(fixture.Load(), Text((61, "A")));
        script.Start("shared", "npc", 60);
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
        Assert.Equal(0, script.ReturnEntry);
        Assert.Equal(60, script.Caller);
        Assert.Null(script.PendingDisplay);
    }

    private static EventFixture RedirectFixture(int limit)
    {
        var fixture = Layout(6);
        Set(fixture, 1, 2, $$"""{"op":"end","mode":"redirect","to":"L0005","limit":{{limit}}}""", 5);
        Set(fixture, 2, 0, """{"op":"end","mode":"keep"}""", 5);
        Set(fixture, 5, 0, """{"op":"end","mode":"keep"}""", 5);
        return fixture;
    }

    private static EventFixture StaticCountedFixture(string semantics, int opcode)
    {
        var fixture = Layout(6);
        Set(fixture, 1, opcode, semantics, 5);
        Set(fixture, 2, 0, """{"op":"end","mode":"keep"}""", 5);
        Set(fixture, 5, 0, """{"op":"end","mode":"keep"}""", 5);
        return fixture;
    }

    private static int RunCounted(TriggerEventCollection events, DynamicEventWorld world)
    {
        var script = new TriggerScriptSession(events, Text((61, "A")), world);
        script.Start("shared", "npc", 60);
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
        return world.Objects.Single(o => o.Id == 60).TriggerIdleCount;
    }

    private static EventFixture Layout(int count)
    {
        var fixture = new EventFixture();
        fixture.Index["source"]!["instructionCount"] = count;
        fixture.Index["source"]!["byteLength"] = count * 8;
        fixture.Commands.Clear();
        for (int address = 0; address < count; address++)
            fixture.Commands.Add(Command(address, 0, """{"op":"end","mode":"keep"}""", count - 1));
        return fixture;
    }

    private static void Set(EventFixture fixture, int address, int opcode, string semantics, int lastAddress) =>
        fixture.Commands[address] = Command(address, opcode, semantics, lastAddress);

    private static void AddEntry(EventFixture fixture, string id, int address) =>
        fixture.Files["shared"]["entries"]!.AsArray().Add(new JsonObject
        {
            ["id"] = id,
            ["owner"] = new JsonObject { ["type"] = "eventObject", ["id"] = 60 },
            ["slot"] = "wTriggerScript", ["kind"] = "trigger",
            ["target"] = $"L{address:X4}", ["sourceAddress"] = address
        });

    private static DialogueLanguagePackage Text(params (int Id, string Text)[] blocks) =>
        new("en", blocks.Select(block => new DialogueMessageBlock(block.Id, block.Id,
            [DialogueMessagePart.FromText(block.Text)])));

    private static TriggerBattleContent SimpleContent() =>
        new(SimpleRole(), new(1, 100, 150, 40, 20, 100), SimpleGrowth(), new Dictionary<int, ClassicBattleEnemy>());

    private static TriggerBattleContent ShopContent() => new(
        [SimpleRole()], [new ClassicBattlePlayer(1, 100, 150, 40, 20, 100)],
        new Dictionary<int, ClassicBattleGrowthData> { [1] = SimpleGrowth() },
        new Dictionary<int, IReadOnlyList<ClassicBattleEnemy>>(),
        shopObjects: [new BattleShopObjectBinding(300, 120, true, 0)],
        stores: [new BattleShopStoreBinding(5, [300])]);

    private static BattleRoleSnapshot SimpleRole() => new(1, 2, 100, 150, 20, 50, new(5, 2, 17),
        new Dictionary<string, BattleExperienceSnapshot>
        {
            ["health"] = new(7, 3, 55), ["magic"] = new(7, 3, 55), ["attack"] = new(7, 3, 55),
            ["magicPower"] = new(7, 3, 55), ["defense"] = new(7, 3, 55), ["dexterity"] = new(7, 3, 55), ["flee"] = new(7, 3, 55)
        }, new int[6], new int[32], new int[9], 0);

    private static ClassicBattleGrowthData SimpleGrowth() => new(Enumerable.Repeat(100, 100).ToArray(), []);

    private sealed class ZeroRandom : IBattleRandom
    {
        public int NextInclusive(int from, int to) => from;
        public float NextInclusive(float from, float to) => from;
    }

    private static EventFixture CountedFixture()
    {
        var fixture = new EventFixture();
        fixture.Index["source"]!["instructionCount"] = 8;
        fixture.Index["source"]!["byteLength"] = 64;
        fixture.Commands.Clear();
        fixture.Commands.Add(Command(0, 134, """{"op":"raw"}""", lastAddress: 7));
        fixture.Commands.Add(Command(1, 3, """{"op":"goto","to":"L0006","limit":3}""", lastAddress: 7));
        fixture.Commands.Add(Command(2, 0, """{"op":"end","mode":"keep"}""", lastAddress: 7));
        fixture.Commands.Add(Command(3, 2, """{"op":"end","mode":"redirect","to":"L0006","limit":2}""", lastAddress: 7));
        fixture.Commands.Add(Command(4, 0, """{"op":"end","mode":"keep"}""", lastAddress: 7));
        fixture.Commands.Add(Command(5, 8, """{"op":"advanceEntry"}""", lastAddress: 7));
        fixture.Commands.Add(Command(6, 0, """{"op":"end","mode":"keep"}""", lastAddress: 7));
        fixture.Commands.Add(Command(7, 0, """{"op":"end","mode":"keep"}""", lastAddress: 7));
        fixture.Files["shared"]["entries"] = JsonNode.Parse("""
            [{"id":"npc","owner":{"type":"eventObject","id":60},"slot":"wTriggerScript","kind":"trigger","target":"L0001","sourceAddress":1},
             {"id":"redirect","owner":{"type":"eventObject","id":60},"slot":"wTriggerScript","kind":"trigger","target":"L0003","sourceAddress":3},
             {"id":"advance","owner":{"type":"eventObject","id":60},"slot":"wTriggerScript","kind":"trigger","target":"L0005","sourceAddress":5}]
            """);
        return fixture;
    }

    // Root caller 60 shows a prefix, calls L0005 with the literal caller 70, then calls L0008 with
    // the 0xFFFF operand; L0008's counted goto therefore owns the last event object's counter.
    private static EventFixture LastCallerCalleeFixture()
    {
        var fixture = Layout(12);
        Set(fixture, 1, 0xFFFF, """{"op":"showDialog","messageId":61,"descriptionLineWord":0}""", 11);
        Set(fixture, 2, 4, """{"op":"call","to":"L0005","eventObjectId":70}""", 11);
        Set(fixture, 3, 4, """{"op":"call","to":"L0008","eventObjectId":65535}""", 11);
        Set(fixture, 5, 0xFFFF, """{"op":"showDialog","messageId":62,"descriptionLineWord":0}""", 11);
        Set(fixture, 8, 3, """{"op":"goto","to":"L000B","limit":2}""", 11);
        return fixture;
    }

    private static DynamicEventWorld World(params int[] objectIds) => DynamicEventWorld.ForHost(
        new StaticSceneRules(new SceneMap(2, new uint[16384], [new SceneFrame(0, 32, 15)]),
            objectIds.Select(id => new SceneObject((ushort)id, 320, 320, 0, 0, 0, 7, 1, 0, 0)).ToArray()),
        new StaticLeader(320, 320, 0, 0, 0, 3,
            new SceneSprite(17, Enumerable.Range(0, 12).Select(i => new SceneFrame(i, 20, 30)))),
        2, 0, 0);

    private static void Advance(TriggerDialogueRequest request)
    {
        for (int i = 0; i < 8 && request.Display.Current.Phase != DialogueDisplayPhase.WaitingForAdvance; i++)
            request.Display.Update(24, false, true);
        request.Display.Update(100, true, false);
        Assert.Equal(DialogueDisplayPhase.Completed, request.Display.Current.Phase);
    }

    private static DialogueLanguagePackage Language() => new("en", [
        new DialogueMessageBlock(61, 61, [DialogueMessagePart.FromText("outer")]),
        new DialogueMessageBlock(62, 62, [DialogueMessagePart.FromText("callee")]),
        new DialogueMessageBlock(63, 63, [DialogueMessagePart.FromText("nested")])]);

    // 0 raw, 1 call(0x0004)/L0005/61, 2 outer display, 3 end keep, 4 filler end keep,
    // 5 callee dialog, 6 callee display, 7 nested call(0x0004)/L000A/0, 8 markFailed,
    // 9 end advance, A nested display, B nested end keep, C filler end keep.
    private static EventFixture CallFixture()
    {
        var fixture = new EventFixture();
        fixture.Index["source"]!["instructionCount"] = 13;
        fixture.Index["source"]!["byteLength"] = 104;
        fixture.Commands.Clear();
        fixture.Commands.Add(Command(0, 134, """{"op":"raw"}"""));
        fixture.Commands.Add(Command(1, 4, """{"op":"call","to":"L0005","eventObjectId":61}"""));
        fixture.Commands.Add(Command(2, 0xffff, """{"op":"showDialog","messageId":61,"descriptionLineWord":0}"""));
        fixture.Commands.Add(Command(3, 0, """{"op":"end","mode":"keep"}"""));
        fixture.Commands.Add(Command(4, 0, """{"op":"end","mode":"keep"}"""));
        fixture.Commands.Add(Command(5, 60, """{"op":"setDialog","box":"upper","portraitId":59,"color":0,"rngWord":0}"""));
        fixture.Commands.Add(Command(6, 0xffff, """{"op":"showDialog","messageId":62,"descriptionLineWord":0}"""));
        fixture.Commands.Add(Command(7, 4, """{"op":"call","to":"L000A","eventObjectId":0}"""));
        fixture.Commands.Add(Command(8, 0x41, """{"op":"markFailed"}"""));
        fixture.Commands.Add(Command(9, 1, """{"op":"end","mode":"advance"}"""));
        fixture.Commands.Add(Command(10, 0xffff, """{"op":"showDialog","messageId":63,"descriptionLineWord":0}"""));
        fixture.Commands.Add(Command(11, 0, """{"op":"end","mode":"keep"}"""));
        fixture.Commands.Add(Command(12, 0, """{"op":"end","mode":"keep"}"""));
        fixture.Entry["target"] = "L0001";
        fixture.Entry["sourceAddress"] = 1;
        return fixture;
    }

    internal static JsonObject Command(int address, int opcode, string semantics, int lastAddress = 12)
    {
        var node = JsonNode.Parse(semantics)!.AsObject();
        node["label"] = $"L{address:X4}";
        node["next"] = address == lastAddress ? null : $"L{address + 1:X4}";
        node["source"] = new JsonObject { ["index"] = address, ["opcode"] = opcode, ["operands"] = new JsonArray(0, 0, 0) };
        return node;
    }
}
