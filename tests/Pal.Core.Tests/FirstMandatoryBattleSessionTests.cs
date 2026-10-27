using System.Security.Cryptography;
using System.Text;
using Pal.TestFixtures;
using Xunit;

namespace Pal.Core.Tests;

public sealed class FirstMandatoryBattleSessionTests
{
    [Fact]
    public void Search_for_owner28_rejects_before_any_session_side_effect()
    {
        FirstMandatoryBattleProfile profile = LoadGeometryFixture();
        var sprite = new SceneSprite(17, [new SceneFrame(0, 1, 1), new SceneFrame(9, 1, 1)]);
        var owner = new SceneObject(28, 1280, 256, 2, 0, 0, 210, 0, 0, 0)
        {
            TriggerMode = 1,
            TriggerEntry = 0x09c3
        };
        var rules = new StaticSceneRules(new SceneMap(12, new uint[64 * 128 * 2], [new SceneFrame(0, 1, 1)]), [owner]);
        var leader = new StaticLeader(1264, 248, 3, 0, 0, 0, sprite);
        var session = new FirstMandatoryBattleSession(profile, rules, leader);
        FirstMandatoryBattleSessionState before = session.State;
        ExplorationState explorationBefore = session.Current;

        session.PressSearch();
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => session.Update(0));

        Assert.Equal("First mandatory battle trigger @09C3 is unavailable: closure tickets #46-#53 are not implemented; no side effects were applied.", error.Message);
        Assert.Same(before, session.State);
        Assert.Same(explorationBefore, session.Current);
        Assert.Equal(0x09c3, session.State.World.EventObjects.Single(item => item.OwnerId == 28).TriggerEntry);
    }

    [Fact]
    public void Profile_creates_independent_runtime_sessions()
    {
        FirstMandatoryBattleProfile profile = LoadGeometryFixture();
        var sprite = new SceneSprite(17, [new SceneFrame(0, 1, 1), new SceneFrame(9, 1, 1)]);
        var rules = new StaticSceneRules(new SceneMap(12, new uint[64 * 128 * 2], [new SceneFrame(0, 1, 1)]),
            [new SceneObject(28, 1280, 256, 2, 0, 0, 210, 0, 0, 0) { TriggerMode = 1, TriggerEntry = 0x09c3 }]);
        var leader = new StaticLeader(1264, 248, 3, 0, 0, 0, sprite);

        var first = new FirstMandatoryBattleSession(profile, rules, leader);
        var second = new FirstMandatoryBattleSession(profile, rules, leader);
        first.State.Roles[0].Hp = 1;
        first.State.World.EventObjects[0].State = 0;

        Assert.Equal(150, second.State.Roles[0].Hp);
        Assert.Equal(2, second.State.World.EventObjects[0].State);
        Assert.NotSame(first.State, second.State);
    }

    private static FirstMandatoryBattleProfile LoadGeometryFixture()
    {
        var fixture = BattleContextTestFixture.Valid();
        fixture.Document["snapshot"]!["world"]!["leader"]!["x"] = 1264;
        fixture.Document["snapshot"]!["world"]!["leader"]!["y"] = 248;
        fixture.Document["snapshot"]!["world"]!["searchProof"]!["sampleIndex"] = 1;
        byte[] context = Encoding.UTF8.GetBytes(fixture.Document.ToJsonString());
        string hash = Convert.ToHexStringLower(SHA256.HashData(context));
        byte[] index = Encoding.UTF8.GetBytes($"{{\"formatVersion\":1,\"profile\":\"first-mandatory-battle-pre-trigger-v1\",\"contextFile\":\"battle-context.json\",\"contextSha256\":\"{hash}\"}}");
        return FirstMandatoryBattleProfile.Load(index, new Dictionary<string, byte[]> { ["battle-context.json"] = context });
    }
}
