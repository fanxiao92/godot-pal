using Xunit;
using Pal.Core.State;

namespace Pal.Core.Tests;

public sealed class CashLiveStateTests
{
    // Independent literals from script.c 0x001E/0x008F and battle.c unsigned DWORD addition.
    // First frozen RED: +7 must change initial10 to17, not leave the no-op shell's10.
    [Fact]
    public void ExplicitOwnerPreservesDwordCashAndReferenceArithmetic()
    {
        var cash = new CashLiveState(10);
        Assert.Equal(10u, cash.Cash);
        Assert.True(cash.TryChange(7));
        Assert.Equal(17u, cash.Cash);
        Assert.True(cash.TryChange(-17));
        Assert.Equal(0u, cash.Cash);
        Assert.False(cash.TryChange(-1));
        Assert.Equal(0u, cash.Cash);
        Assert.True(cash.TryChange(0));
        Assert.Equal(0u, cash.Cash);
        Assert.True(cash.TryChange(32767));
        Assert.Equal(32767u, cash.Cash);
        Assert.False(cash.TryChange(-32768));
        Assert.Equal(32767u, cash.Cash);
        cash.Credit(1);
        Assert.Equal(32768u, cash.Cash);
        Assert.True(cash.TryChange(-32768));
        Assert.Equal(0u, cash.Cash);

        // Full unsigned range, not int.MaxValue or a modern wallet cap. Positive wrap is legal.
        var large = new CashLiveState(4294967295u);
        Assert.Equal(4294967295u, large.Cash);
        Assert.True(large.TryChange(1));
        Assert.Equal(0u, large.Cash);
        large.Credit(4294967295u);
        Assert.Equal(4294967295u, large.Cash);
        large.Credit(2);
        Assert.Equal(1u, large.Cash);
        large.Credit(0);
        Assert.Equal(1u, large.Cash);
        var aboveInt = new CashLiveState(2147483648u);
        Assert.True(aboveInt.TryChange(-32768));
        Assert.Equal(2147450880u, aboveInt.Cash);

        // Separate literal halving oracles: floor, including both ends of the DWORD domain.
        foreach (var (initial, expected) in new (uint, uint)[]
            { (0, 0), (1, 0), (2, 1), (3, 1), (10, 5), (4294967295, 2147483647) })
        {
            var owner = new CashLiveState(initial);
            owner.Halve();
            Assert.Equal(expected, owner.Cash);
        }
        large.Halve();
        large.Halve();
        Assert.Equal(0u, large.Cash);

        // Out-of-domain operands are API misuse, not a shortage or truncated WORD.
        var guarded = new CashLiveState(100);
        foreach (int invalid in new[] { -32769, 32768, int.MinValue, int.MaxValue })
        {
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() => guarded.TryChange(invalid));
            Assert.Equal("amount", exception.ParamName);
            Assert.Equal(100u, guarded.Cash);
        }
        Assert.Equal(0u, cash.Cash);
        Assert.Equal(0u, large.Cash);
        Assert.Equal(2147450880u, aboveInt.Cash);
        Assert.True(cash.TryChange(5));
        Assert.Equal(5u, cash.Cash);
        Assert.Equal(100u, guarded.Cash); // another owner is never mutated or reseeded
    }
}
