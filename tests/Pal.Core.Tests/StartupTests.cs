using Pal.Core;
using Xunit;

namespace Pal.Core.Tests;

public class StartupTests
{
    [Fact]
    public void Core_reports_ready_for_startup()
    {
        Assert.Equal("Core ready", Startup.Status);
    }
}
