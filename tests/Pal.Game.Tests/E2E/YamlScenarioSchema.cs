using System.Collections.Generic;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Pal.Game.Tests.E2E;

public sealed class YamlScenarioDocument
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "1";
    public string Description { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 90;
    public double TolerancePercent { get; set; } = 1.0;

    /// <summary>
    /// Optional multi-process phases. When non-empty, pal.ps1 invokes one separate
    /// dotnet test process per phase (passing PAL_E2E_PHASE_INDEX=0, 1, ...).
    /// When empty, Sessions (or top-level Args/Steps) run in a single process.
    /// </summary>
    public List<YamlScenarioPhase> Phases { get; set; } = new();

    /// <summary>Optional multi-session list inside a single process (e.g. zh-Hans then en).</summary>
    public List<YamlScenarioSession> Sessions { get; set; } = new();

    /// <summary>Single-session shorthand: Main CLI arguments.</summary>
    public List<string> Args { get; set; } = new();

    public string? SaveNamespace { get; set; }
    public bool CleanupSaveBefore { get; set; }
    public bool CleanupSaveAfter { get; set; }
    public List<YamlScenarioStep> Steps { get; set; } = new();

    public static YamlScenarioDocument Parse(string yamlText)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        return deserializer.Deserialize<YamlScenarioDocument>(yamlText)
            ?? throw new System.IO.InvalidDataException("YAML scenario deserialized to null.");
    }

    public List<YamlScenarioSession> ResolveSessionsForPhase(int phaseIndex)
    {
        if (Phases.Count > 0)
        {
            if (phaseIndex < 0 || phaseIndex >= Phases.Count)
                throw new System.ArgumentOutOfRangeException(nameof(phaseIndex), $"Phase index {phaseIndex} out of range (0..{Phases.Count - 1}).");
            var phase = Phases[phaseIndex];
            if (phase.Sessions.Count > 0) return phase.Sessions;
            return new List<YamlScenarioSession>
            {
                new()
                {
                    Name = string.IsNullOrWhiteSpace(phase.Name) ? $"phase-{phaseIndex}" : phase.Name,
                    Args = phase.Args,
                    SaveNamespace = phase.SaveNamespace,
                    CleanupSaveBefore = phase.CleanupSaveBefore,
                    CleanupSaveAfter = phase.CleanupSaveAfter,
                    Steps = phase.Steps
                }
            };
        }

        if (Sessions.Count > 0) return Sessions;
        return new List<YamlScenarioSession>
        {
            new()
            {
                Name = Name,
                Args = Args,
                SaveNamespace = SaveNamespace,
                CleanupSaveBefore = CleanupSaveBefore,
                CleanupSaveAfter = CleanupSaveAfter,
                Steps = Steps
            }
        };
    }
}

public sealed class YamlScenarioPhase
{
    public string Name { get; set; } = "";
    public List<YamlScenarioSession> Sessions { get; set; } = new();
    public List<string> Args { get; set; } = new();
    public string? SaveNamespace { get; set; }
    public bool CleanupSaveBefore { get; set; }
    public bool CleanupSaveAfter { get; set; }
    public List<YamlScenarioStep> Steps { get; set; } = new();
}

public sealed class YamlScenarioSession
{
    public string Name { get; set; } = "";
    public List<string> Args { get; set; } = new();
    public string? SaveNamespace { get; set; }
    public bool CleanupSaveBefore { get; set; }
    public bool CleanupSaveAfter { get; set; }
    public List<YamlScenarioStep> Steps { get; set; } = new();
}

public sealed class YamlScenarioStep
{
    /// <summary>
    /// Supported actions: wait, key_tap, key_hold, advance_dialogue,
    /// battle_attack_rounds, assert, capture, probe_dump.
    /// </summary>
    public string Action { get; set; } = "";
    public string? Name { get; set; }
    public string? Key { get; set; }
    public string? Condition { get; set; }
    public List<string> Assertions { get; set; } = new();
    public int TimeoutMs { get; set; } = 15000;
    public int HoldUpdates { get; set; } = 0;
    public int HoldMs { get; set; } = 0;
    public int Repeat { get; set; } = 1;
    public int MaxRounds { get; set; } = 200;
    public int SettleFrames { get; set; } = 2;
    public double? TolerancePercent { get; set; }
}
