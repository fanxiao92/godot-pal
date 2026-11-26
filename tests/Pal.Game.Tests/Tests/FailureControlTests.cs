using System.Threading.Tasks;
using GdUnit4;
using static GdUnit4.Assertions;

namespace Pal.Game.Tests;

// Deliberately failing controls: always exclude this class from the positive suite.
[TestSuite]
public class FailureControlTests
{
    [TestCase]
    public void IntentionalAssertionMismatchPropagates() => AssertThat(320).IsEqual(321);

    [TestCase(Timeout = 500)]
    public async Task IntentionalTimeoutPropagates() => await Task.Delay(System.Threading.Timeout.Infinite);

    [TestCase(Timeout = 10000), RequireGodotRuntime, GodotExceptionMonitor]
    public async Task NativeReadyExceptionIsNotSwallowed()
    {
        using var runner = ISceneRunner.Load("res://Tests/ExpectedReadyException.tscn", true);
        await runner.SimulateFrames(3);
    }
}
