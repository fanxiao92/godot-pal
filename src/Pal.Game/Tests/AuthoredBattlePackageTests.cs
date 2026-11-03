using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Buffers.Binary;
using GdUnit4;
using Godot;
using Pal.Core;
using Pal.Game.Editor;
using Pal.Game.Profiles;
using static GdUnit4.Assertions;

namespace Pal.Game.Tests;

[TestSuite, RequireGodotRuntime]
public class AuthoredBattlePackageTests
{
    [TestCase("Win")]
    [TestCase("Loss")]
    public void CommittedAuthoredContentPreparesLoadsAndResumesRealResult(string scenario)
    {
        string repository = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "../.."));
        string source = Path.Combine(repository, "samples/authored-battle", scenario);
        string input = Path.Combine(Path.GetTempPath(), "pal-authored-" + Guid.NewGuid().ToString("N"));
        string uri = "res://Assets/RuntimeTests/authored-" + Guid.NewGuid().ToString("N");
        string output = ProjectSettings.GlobalizePath(uri);
        Directory.CreateDirectory(input);
        Directory.CreateDirectory(output);
        try
        {
            string staticOutput = Path.Combine(output, "Static/Scenes");
            Directory.CreateDirectory(staticOutput);
            File.Copy(ProjectSettings.GlobalizePath("res://Samples/AuthoredBattle/Static/Scenes/Scene.tscn"), Path.Combine(staticOutput, "Scene.tscn"));
            string numericUri = PrepareBattleData.Prepare(Path.Combine(source, "Numbers"), Path.Combine(output, "Numbers"));
            Directory.CreateDirectory(Path.Combine(input, "events"));
            foreach (string path in Directory.GetFiles(Path.Combine(source, "Story/events")))
                File.Copy(path, Path.Combine(input, "events", Path.GetFileName(path)));
            var metadata = JsonNode.Parse(File.ReadAllText(Path.Combine(source, "Story/story-package.json")))!;
            metadata["staticResources"] = uri + "/Static";
            metadata["battleContentUri"] = numericUri;
            File.WriteAllText(Path.Combine(input, "story-package.json"), metadata.ToJsonString());
            var prepared = PrepareStoryPackage.Prepare(input, Path.Combine(output, "Story"));
            foreach (string language in new[] { "zh-Hans", "en" })
            {
                var story = NativeStoryProfile.Load(prepared.StoryUri, language);
                var visual = NativeBattleVisualProfile.Load(prepared.VisualUri, story);
                visual.RequireReferencePresentation();
                visual.RequireNumberPresentation();
                AssertThat(visual.NumberSprite!.FrameCount).IsEqual(1);
                AssertThat(visual.YellowNumberFrames[0].Index).IsEqual(19);
                AssertThat(visual.BlueNumberFrames[0].Index).IsEqual(29);
                AssertThat(visual.CyanNumberFrames[9].Index).IsEqual(65);
                AssertThat(visual.BlueNumberFrames[0].Width).IsEqual(6);
                AssertThat(visual.BlueNumberFrames[0].Height).IsEqual(8);
                AssertThat(visual.YellowNumberFrames[0].Indices[1]).IsEqual((byte)5);
                AssertThat(visual.BlueNumberFrames[0].Indices[1]).IsEqual((byte)1);
                AssertThat(visual.CyanNumberFrames[0].Indices[1]).IsEqual((byte)4);
                AssertThat(visual.BlueNumberFrames[0].Indices[7 * 6 + 2]).IsEqual((byte)79);
                AssertThat(visual.BlueNumberFrames[0].Alpha[0]).IsEqual((byte)0);
                AssertThat(visual.BlueNumberFrames[0].Alpha[1]).IsEqual((byte)255);
                AssertThat(visual.ResourceSha256.ContainsKey("res://Samples/AuthoredBattle/Visual/Numbers.tres")).IsTrue();
                AssertThat(visual.AttackEffectIndex).IsEqual(0);
                AssertThat(visual.Teams[18].IdleAnimSpeed).IsEqual(2);
                AssertThat(visual.AttackEffectFrames.Select(x => x.Index).SequenceEqual(new[] { 0, 1, 2 })).IsTrue();
                AssertThat(visual.AttackEffectFrames[0].Indices[4 * 16 + 6]).IsEqual((byte)5);
                AssertThat(visual.AttackEffectFrames[1].Indices[6 * 16 + 8]).IsEqual((byte)79);
                AssertThat(visual.AttackEffectFrames[2].Indices[8 * 16 + 10]).IsEqual((byte)5);
                AssertThat(visual.AttackEffectFrames[0].Alpha[0]).IsEqual((byte)0);
                AssertThat(visual.AttackEffectFrames[1].Alpha[6 * 16 + 8]).IsEqual((byte)255);
                AssertThat(visual.AttackEffectFrames[0].Indices.SequenceEqual(visual.AttackEffectFrames[1].Indices)).IsFalse();
                AssertThat(visual.ResourceSha256.ContainsKey("res://Samples/AuthoredBattle/Visual/AttackEffect.tres")).IsTrue();
                AssertThat(story.Events.InstructionCount).IsEqual(10);
                var bytes = new byte[80];
                for (int i = 0; i < 10; i++)
                {
                    var instruction = story.Events.GetSource(i);
                    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 8), checked((ushort)instruction.Opcode));
                    for (int n = 0; n < 3; n++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 8 + 2 + n * 2), checked((ushort)instruction.Operands[n]));
                }
                AssertThat(Convert.ToHexStringLower(SHA256.HashData(bytes))).IsEqual("0ad42c6e02646a69b76934eb478f4a917ac11e81436f61c35cd91f65e61f7ca8");
                AssertThat(story.Exploration.Rules.IsBlocked(320, 320)).IsFalse();
                var match = story.Exploration.Rules.FindSearchTarget(story.Exploration.Leader)!;
                AssertThat(match.ObjectId).IsEqual(1);
                AssertThat(match.SampleIndex).IsEqual(1);
                AssertThat(story.Exploration.Rules.IsBlocked(0, 0)).IsTrue();
                AssertThat(story.Language.Resolve(1, 0).Parts[0].Text!).IsEqual(language == "en" ? "FIGHT" : "战");
                AssertThat(story.Language.Resolve(2, 0).Parts[0].Text!).IsEqual(language == "en" ? "WIN" : "胜");
                AssertThat(story.Language.Resolve(3, 0).Parts[0].Text!).IsEqual(language == "en" ? "LOSE" : "败");
                foreach (var glyph in story.Font.Glyphs) AssertThat(glyph.Bitmap.Any(b => b != 0)).IsTrue();
                AssertThat(story.Font.GetGlyph('F').IsSet(2, 1)).IsTrue();
                AssertThat(story.Palette[79]).IsEqual(new Color(1, 248f / 255, 224f / 255));
                AssertThat(visual.PlayerSprite.SpriteId).IsEqual(41);
                AssertThat(visual.PlayerSprite.Frames.Count).IsEqual(12);
                AssertThat(visual.PlayerSprite.Frames[0].Indices[10 * 16 + 6]).IsEqual((byte)4);
                AssertThat(visual.PlayerSprite.Frames[0].Texture.GetImage().GetPixel(6, 10)).IsEqual(new Color(64f / 255, 224f / 255, 240f / 255));
                AssertThat(visual.Teams[18].EnemySprite.Frames[0].Indices[10 * 24 + 6]).IsEqual((byte)6);
                AssertThat(visual.Teams[18].Background.PaletteIndices[0]).IsEqual((byte)1);
                AssertThat(visual.Teams[18].Background.PaletteIndices[120 * 320 + 32]).IsEqual((byte)3);
                AssertThat(visual.Teams[18].ActWaitFrames).IsEqual(0);
                var script = new TriggerScriptSession(story.Events, story.Language, story.BattleContent, new VerificationRandom());
                script.Start("arena", "challenge", match.ObjectId);
                FinishDisplay(script, 1);
                var request = script.PendingBattle!;
                AssertThat(script.AcknowledgeBattle(request.Id, null)).IsFalse();
                request.Lifecycle.SelectAttack(7);
                AssertThat(request.Lifecycle.ResolveNextAction()).IsTrue();
                var completion = request.Lifecycle.Completion!;
                AssertThat(completion.Outcome).IsEqual(scenario == "Win" ? ClassicBattlePhase.Won : ClassicBattlePhase.Lost);
                AssertThat(request.Lifecycle.Current.Role.Hp).IsEqual(scenario == "Win" ? 125 : 0);
                AssertThat(request.Lifecycle.Current.Role.Mp).IsEqual(scenario == "Win" ? 35 : 20);
                AssertThat(script.AcknowledgeBattle(request.Id, completion)).IsTrue();
                FinishDisplay(script, scenario == "Win" ? 2 : 3);
                AssertThat(script.Phase).IsEqual(TriggerScriptPhase.Completed);
                AssertThat(script.CurrentPosition!.SourceAddress).IsEqual(scenario == "Win" ? 6 : 9);
                AssertThat(script.AcknowledgeBattle(request.Id, completion)).IsFalse();
            }
        }
        finally
        {
            Directory.Delete(input, true);
            Directory.Delete(output, true);
        }
    }

    private static void FinishDisplay(TriggerScriptSession script, int messageId)
    {
        AssertThat(script.Phase).IsEqual(TriggerScriptPhase.WaitingForDisplay);
        var display = script.PendingDisplay!;
        AssertThat(display.Content.StartMessageId).IsEqual(messageId);
        display.Display.Update(24, false, true);
        display.Display.Update(100, true, false);
        AssertThat(script.AcknowledgeDisplay(display.Id)).IsTrue();
    }

    // Verification input only; never part of authored production data or preparation.
    private sealed class VerificationRandom : IBattleRandom
    {
        public int NextInclusive(int from, int to) => from == 0 && to == 5 ? 1 : from;
        public float NextInclusive(float from, float to) => 1;
    }
}
