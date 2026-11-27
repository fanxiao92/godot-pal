using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace Pal.Game.Tests.E2E;

/// <summary>
/// T3 GPU YAML End-to-End regression suite. Invoked exclusively by
/// `./pal.ps1 test e2e --gpu` via e2e-gpu.runsettings; excluded from T1 headless CI.
/// </summary>
[TestSuite, RequireGodotRuntime]
[TestCategory("E2E"), TestCategory("RealAssets")]
public class E2EScenarioSuite
{
    [TestCase(Timeout = 240000)]
    public async Task Run_configured_yaml_scenario()
    {
        string scenarioPath = OS.GetEnvironment("PAL_E2E_SCENARIO_PATH");
        AssertThat(string.IsNullOrWhiteSpace(scenarioPath)).IsFalse();
        AssertThat(File.Exists(scenarioPath)).IsTrue();

        string artifactsDir = OS.GetEnvironment("PAL_E2E_ARTIFACTS_DIR");
        string goldenDir = OS.GetEnvironment("PAL_E2E_GOLDEN_DIR");
        AssertThat(string.IsNullOrWhiteSpace(artifactsDir)).IsFalse();
        AssertThat(string.IsNullOrWhiteSpace(goldenDir)).IsFalse();

        int phaseIndex = int.TryParse(OS.GetEnvironment("PAL_E2E_PHASE_INDEX"), out int parsed) ? parsed : 0;
        bool updateGolden = OS.GetEnvironment("PAL_E2E_UPDATE_GOLDEN") == "1";
        string commitSha = OS.GetEnvironment("PAL_E2E_COMMIT_SHA") ?? "";

        var doc = YamlScenarioDocument.Parse(File.ReadAllText(scenarioPath, Encoding.UTF8));
        var result = await YamlE2ERunner.RunAsync(doc, phaseIndex, artifactsDir, goldenDir, updateGolden, commitSha);
        AssertThat(result.Status).IsEqual("passed");
        AssertThat(result.ProbePassed).IsTrue();
    }
}

/// <summary>
/// Headless-safe smoke tests for YAML scenario parsing and GoldenComparator logic so Linux CI
/// always verifies the E2E harness compiles and passes unit-level contracts without a GPU.
/// </summary>
[TestSuite, RequireGodotRuntime]
[TestCategory("Smoke")]
public class E2EHarnessSmokeTests
{
    [TestCase]
    public void Yaml_parser_and_golden_comparator_enforce_strict_0_1_contract()
    {
        string yaml = """
            name: sample-smoke
            version: "1"
            timeoutSeconds: 30
            tolerancePercent: 1.0
            args:
              - --m3-static-acceptance
            steps:
              - action: wait
                condition: "updates >= 1"
              - action: capture
                name: "01-start"
            """;
        var doc = YamlScenarioDocument.Parse(yaml);
        AssertThat(doc.Name).IsEqual("sample-smoke");
        AssertThat(doc.ResolveSessionsForPhase(0).Count).IsEqual(1);
        AssertThat(doc.ResolveSessionsForPhase(0)[0].Steps.Count).IsEqual(2);

        string tmp = Path.Combine(Path.GetTempPath(), "pal-e2e-smoke-" + Guid.NewGuid().ToString("N"));
        string art = Path.Combine(tmp, "artifacts");
        string gold = Path.Combine(tmp, "goldens");
        Directory.CreateDirectory(art);
        Directory.CreateDirectory(gold);
        try
        {
            using var imgA = Image.CreateEmpty(320, 200, false, Image.Format.Rgba8);
            imgA.Fill(new Color(0.1f, 0.2f, 0.3f, 1f));
            for (int x = 0; x < 160; x++)
                for (int y = 0; y < 100; y++)
                    imgA.SetPixel(x, y, new Color(0.8f, 0.6f, 0.2f, 1f));

            // 1. Missing golden must fail strictly (never silently skip)
            var missing = GoldenComparator.CompareOrUpdate(imgA, "smoke", "1", "01-frame", 1.0, art, gold, updateGolden: false, "abc1234");
            AssertThat(missing.Status).IsEqual("failed");
            AssertThat(missing.Error!.Contains("Missing golden")).IsTrue();

            // 2. Explicit update-golden writes baseline & manifest
            var updated = GoldenComparator.CompareOrUpdate(imgA, "smoke", "1", "01-frame", 1.0, art, gold, updateGolden: true, "abc1234");
            AssertThat(updated.Status).IsEqual("updated");
            AssertThat(File.Exists(Path.Combine(gold, "golden-manifest.json"))).IsTrue();

            // 3. Identical image passes with 0% diff
            var matched = GoldenComparator.CompareOrUpdate(imgA, "smoke", "1", "01-frame", 1.0, art, gold, updateGolden: false, "abc1234");
            AssertThat(matched.Status).IsEqual("passed");
            AssertThat(matched.DiffPercent).IsEqual(0.0);

            // 4. Forged image exceeding tolerance fails and emits diff.png
            using var imgB = Image.CreateEmpty(320, 200, false, Image.Format.Rgba8);
            imgB.Fill(new Color(0.9f, 0.1f, 0.1f, 1f));
            imgB.SetPixel(0, 0, new Color(0.0f, 0.0f, 0.0f, 1f));
            var failed = GoldenComparator.CompareOrUpdate(imgB, "smoke", "1", "01-frame", 1.0, art, gold, updateGolden: false, "abc1234");
            AssertThat(failed.Status).IsEqual("failed");
            AssertThat(failed.DiffPercent > 1.0).IsTrue();
            AssertThat(string.IsNullOrEmpty(failed.DiffPath)).IsFalse();
            AssertThat(File.Exists(Path.Combine(art, failed.DiffPath!))).IsTrue();
        }
        finally
        {
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        }
    }
}
