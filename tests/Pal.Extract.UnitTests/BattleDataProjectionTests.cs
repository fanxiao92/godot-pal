using Pal.Extract;
using Xunit;

namespace Pal.Extract.Tests;

public sealed class BattleDataProjectionTests
{
    [Fact]
    public void Projection_refuses_equipment_without_script_closure_instead_of_zeroing_it()
    {
        var files = BattleProjectionFixture.Create();
        BattleProjectionFixture.Mutate(files, "player-roles.json", d => d["records"]![0]!["equipment"]![2]![4] = 8);

        var error = Assert.Throws<InvalidDataException>(() => BattleDataProjector.Project(files, [4, 0], [0]));

        Assert.Contains("player-roles.json.records[0].equipment[2][4]", error.Message);
    }
}
