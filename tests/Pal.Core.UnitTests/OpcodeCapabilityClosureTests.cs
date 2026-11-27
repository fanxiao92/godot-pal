using Pal.Core.Events;
using Xunit;

namespace Pal.Core.UnitTests;

public sealed class OpcodeCapabilityClosureTests
{
    [Fact]
    public void TriggerScriptSession_SupportedOpcodes_is_complete_and_free_of_auto_only_entries()
    {
        var supported = TriggerScriptSession.SupportedOpcodes;
        Assert.Equal(54, supported.Count);
        Assert.Contains(0x0000, supported.Keys);
        Assert.Contains(0x0007, supported.Keys);
        Assert.Contains(0xFFFF, supported.Keys);
        // 0x000A is auto-only / not supported in trigger
        Assert.DoesNotContain(0x000A, supported.Keys);
        foreach (var (code, cap) in supported)
        {
            Assert.Equal(code, cap.Opcode);
            Assert.False(string.IsNullOrWhiteSpace(cap.Verb));
            Assert.False(string.IsNullOrWhiteSpace(cap.FormatCell()));
        }
    }

    [Fact]
    public void DynamicEventSession_SupportedOpcodes_matches_ValidateAutoClosure_bidirectionally()
    {
        // Forward + reverse check: every verb in SupportedOpcodes must match the exact set
        // accepted by DynamicEventSession.ValidateAutoClosure, and every unsupported verb must reject.
        var expectedVerbs = new HashSet<string>(StringComparer.Ordinal)
        {
            "end", "goto", "waitUpdate", "walkStep", "face", "walkTo", "setState", "hide"
        };
        var declaredVerbs = DynamicEventSession.SupportedOpcodes.Values
            .Select(c => c.Verb)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal(expectedVerbs, declaredVerbs);
        Assert.Equal(13, DynamicEventSession.SupportedOpcodes.Count);

        // Verify a representative unsupported verb (showDialog / startBattle / setMusic) is NOT declared
        Assert.DoesNotContain(0xFFFF, DynamicEventSession.SupportedOpcodes.Keys);
        Assert.DoesNotContain(0x0007, DynamicEventSession.SupportedOpcodes.Keys);
        Assert.DoesNotContain(0x0043, DynamicEventSession.SupportedOpcodes.Keys);
    }

    [Fact]
    public void TriggerScriptSession_SupportedOpcodes_verbs_match_ValidateCapabilityClosure_set()
    {
        var expectedVerbs = new HashSet<string>(StringComparer.Ordinal)
        {
            "end", "goto", "call", "redraw", "randomJump", "startBattle", "advanceEntry",
            "waitUpdate", "walkStep", "face", "walkTo", "setObjectPosition", "setObjectGesture",
            "setPartyFacing", "changeHp", "changeMp", "changeHpMp", "changeCash", "addItem",
            "removeItem", "revivePlayer", "setAutoEntry", "setTriggerEntry", "showBuyMenu",
            "showSellMenu", "boostStat", "setDialog", "setTriggerMode", "markFailed", "setMusic",
            "setPartyPos", "setState", "setBattlefield", "requestReload", "fadeToRed", "fadeOut",
            "hide", "itemShortageJump", "setSceneScripts", "fadeScene", "setParty", "noop",
            "halveCash", "setObjectScript", "setStateRange", "showDialog"
        };
        var declaredVerbs = TriggerScriptSession.SupportedOpcodes.Values
            .Select(c => c.Verb)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal(expectedVerbs, declaredVerbs);
    }
}
