using Pal.Core.Events;
using Pal.Extract;
using Xunit;

namespace Pal.Extract.UnitTests;

public sealed class CapabilityLedgerExporterTests
{
    [Fact]
    public void RenderMarkdown_is_deterministic_LF_only_and_covers_all_165_opcodes()
    {
        string first = CapabilityLedgerExporter.RenderMarkdown();
        string second = CapabilityLedgerExporter.RenderMarkdown();

        Assert.Equal(first, second);
        Assert.DoesNotContain("\r\n", first);
        Assert.Contains("## 1. 能力覆盖总览", first);
        Assert.Contains("## 2. `reference/sdlpal` 核心模块对位索引", first);
        Assert.Contains("## 3. 已接入运行时 Opcode 速览（54 条）", first);
        Assert.Contains("## 4. 全量 Opcode 明细（165 条，按地址升序）", first);
        Assert.Contains("<details>", first);
        Assert.Contains("| `0x0000` | `end` |", first);
        Assert.Contains("| `0xFFFF` | `showDialog` |", first);
        Assert.Equal(165, OpcodeRegistry.ByOpcode.Count);
    }

    [Fact]
    public void SupportedOpcodes_align_with_OpcodeRegistry_verbs_and_executors()
    {
        foreach (var (code, cap) in TriggerScriptSession.SupportedOpcodes)
        {
            Assert.True(OpcodeRegistry.ByOpcode.TryGetValue(code, out var desc), $"Missing 0x{code:X4}");
            Assert.Equal(desc!.Op, cap.Verb);
            Assert.NotEqual(OpcodeExecutor.AutoSwitchOnly, desc.Executor);
        }

        foreach (var (code, cap) in DynamicEventSession.SupportedOpcodes)
        {
            Assert.True(OpcodeRegistry.ByOpcode.TryGetValue(code, out var desc), $"Missing 0x{code:X4}");
            Assert.Equal(desc!.Op, cap.Verb);
            Assert.NotEqual(OpcodeExecutor.TriggerSwitchOnly, desc.Executor);
        }
    }
}
