using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Pal.Core;

namespace Pal.Game.Tests.E2E;

public sealed class ProbeSnapshot
{
    public int SchemaVersion { get; init; } = 1;
    public int Updates { get; init; }
    public string? Failure { get; init; }
    public string? Language { get; init; }
    public int? LeaderX { get; init; }
    public int? LeaderY { get; init; }
    public int? LeaderDirection { get; init; }
    public int? LeaderStepPhase { get; init; }
    public string? InteractionPhase { get; init; }
    public bool DialogueActive { get; init; }
    public string? DialoguePhase { get; init; }
    public int DialogueVisibleLength { get; init; }
    public string? MenuMode { get; init; }
    public string? MenuSystemIntent { get; init; }
    public int? MenuSelectedSaveSlot { get; init; }
    public int? RecoverySlot { get; init; }
    public bool Slot1Exists { get; init; }
    public bool Slot2Exists { get; init; }
    public bool Slot3Exists { get; init; }
    public bool Slot4Exists { get; init; }
    public bool Slot5Exists { get; init; }
    public bool BattleActive { get; init; }
    public string? BattleMode { get; init; }
    public int BattleEnemyCount { get; init; }
    public int BattleEnemyHpTotal { get; init; }
    public int BattlePlayerHpTotal { get; init; }
    public bool LostTerminal { get; init; }
    public string? LostReloadFailure { get; init; }
    public int LostReloadDiagnostics { get; init; }
    public int StoryCashGranted { get; init; }
    public int StoryExperienceGranted { get; init; }
    public string? DynamicPhase { get; init; }
    public bool DynamicTriggerActive { get; init; }
    public bool DynamicLastScriptSuccess { get; init; }

    public static ProbeSnapshot Capture(Main main, int updates)
    {
        var exp = main.Exploration;
        var pendingDisplay = main.Dialogue?.Session?.PendingDisplay
            ?? main.Dynamic?.Trigger?.PendingDisplay;
        var displayPhase = pendingDisplay?.Display.Current.Phase
            ?? main.Display?.Current?.Phase;
        var visibleLen = pendingDisplay?.Display.Current.VisibleText.Length
            ?? main.Display?.Current?.VisibleText.Length ?? 0;
        bool dialogueActive = pendingDisplay is not null;
        var battle = main.StoryBattle;
        var storage = main.SaveStorage;

        return new ProbeSnapshot
        {
            Updates = updates,
            Failure = main.Failure,
            Language = main.SelectedLanguage,
            LeaderX = exp?.Leader.X,
            LeaderY = exp?.Leader.Y,
            LeaderDirection = exp?.Leader.Direction,
            LeaderStepPhase = exp?.Leader.StepPhase,
            InteractionPhase = main.Interaction?.Phase.ToString(),
            DialogueActive = dialogueActive,
            DialoguePhase = displayPhase?.ToString(),
            DialogueVisibleLength = visibleLen,
            MenuMode = main.Menu?.Mode.ToString(),
            MenuSystemIntent = main.Menu?.SystemIntent.ToString(),
            MenuSelectedSaveSlot = main.Menu?.SelectedSaveSlot,
            RecoverySlot = main.CurrentRecoverySlot,
            Slot1Exists = storage?.SlotExists(1) ?? false,
            Slot2Exists = storage?.SlotExists(2) ?? false,
            Slot3Exists = storage?.SlotExists(3) ?? false,
            Slot4Exists = storage?.SlotExists(4) ?? false,
            Slot5Exists = storage?.SlotExists(5) ?? false,
            BattleActive = battle is not null,
            BattleMode = main.StoryBattleMode?.ToString(),
            BattleEnemyCount = battle?.Current.Combat.Enemies.Count ?? 0,
            BattleEnemyHpTotal = battle?.Current.Combat.Enemies.Sum(e => e.Hp) ?? 0,
            BattlePlayerHpTotal = battle?.Current.Combat.Players.Sum(p => p.Hp) ?? 0,
            LostTerminal = main.StoryLostTerminal,
            LostReloadFailure = main.StoryLostReloadFailure,
            LostReloadDiagnostics = main.StoryLostReloadDiagnostics,
            StoryCashGranted = main.StoryCashGranted,
            StoryExperienceGranted = main.StoryExperienceGranted,
            DynamicPhase = main.Dynamic?.Phase.ToString(),
            DynamicTriggerActive = main.Dynamic?.Trigger is not null || main.Dynamic?.PendingTrigger is not null,
            DynamicLastScriptSuccess = main.Dynamic?.LastScriptSuccess ?? false
        };
    }

    public object? GetField(string field) => field switch
    {
        "updates" => Updates,
        "failure" => Failure,
        "language" => Language,
        "leader.x" => LeaderX,
        "leader.y" => LeaderY,
        "leader.direction" => LeaderDirection,
        "leader.stepPhase" => LeaderStepPhase,
        "interaction.phase" => InteractionPhase,
        "dialogue.active" => DialogueActive,
        "dialogue.phase" => DialoguePhase,
        "dialogue.visibleLength" => DialogueVisibleLength,
        "menu.mode" => MenuMode,
        "menu.systemIntent" => MenuSystemIntent,
        "menu.selectedSaveSlot" => MenuSelectedSaveSlot,
        "recoverySlot" => RecoverySlot,
        "slot1.exists" => Slot1Exists,
        "slot2.exists" => Slot2Exists,
        "slot3.exists" => Slot3Exists,
        "slot4.exists" => Slot4Exists,
        "slot5.exists" => Slot5Exists,
        "battle.active" => BattleActive,
        "battle.mode" => BattleMode,
        "battle.enemyCount" => BattleEnemyCount,
        "battle.enemyHpTotal" => BattleEnemyHpTotal,
        "battle.playerHpTotal" => BattlePlayerHpTotal,
        "lostTerminal" => LostTerminal,
        "lostReloadFailure" => LostReloadFailure,
        "lostReloadDiagnostics" => LostReloadDiagnostics,
        "storyCashGranted" => StoryCashGranted,
        "storyExperienceGranted" => StoryExperienceGranted,
        "dynamic.phase" => DynamicPhase,
        "dynamic.triggerActive" => DynamicTriggerActive,
        "dynamic.lastScriptSuccess" => DynamicLastScriptSuccess,
        _ => throw new ArgumentException($"Unknown probe field '{field}'.", nameof(field))
    };

    public bool Evaluate(string expression, out string diagnostic)
    {
        if (string.IsNullOrWhiteSpace(expression))
            throw new ArgumentException("Probe condition expression cannot be empty.", nameof(expression));

        // Support '&&' compound expressions.
        if (expression.Contains("&&", StringComparison.Ordinal))
        {
            var parts = expression.Split("&&", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var diags = new List<string>();
            bool all = true;
            foreach (var part in parts)
            {
                if (!Evaluate(part, out var d)) all = false;
                diags.Add(d);
            }
            diagnostic = string.Join(" && ", diags);
            return all;
        }

        string[] operators = ["==", "!=", ">=", "<=", ">", "<"];
        string? op = null;
        int idx = -1;
        foreach (var candidate in operators)
        {
            int found = expression.IndexOf(candidate, StringComparison.Ordinal);
            if (found > 0) { op = candidate; idx = found; break; }
        }
        if (op is null)
            throw new ArgumentException($"Unsupported probe expression '{expression}'. Expected '<field> <op> <value>'.");

        string field = expression[..idx].Trim();
        string rawExpected = expression[(idx + op.Length)..].Trim();
        object? actual = GetField(field);
        string actualText = actual is null ? "null" : Convert.ToString(actual, CultureInfo.InvariantCulture) ?? "null";

        bool result;
        if (rawExpected.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            result = op switch
            {
                "==" => actual is null,
                "!=" => actual is not null,
                _ => throw new ArgumentException($"Operator '{op}' is not valid with null in '{expression}'.")
            };
        }
        else if (actual is bool b && bool.TryParse(rawExpected, out bool expBool))
        {
            result = op switch
            {
                "==" => b == expBool,
                "!=" => b != expBool,
                _ => throw new ArgumentException($"Operator '{op}' is not valid for bool in '{expression}'.")
            };
        }
        else if (actual is int i && int.TryParse(rawExpected, NumberStyles.Integer, CultureInfo.InvariantCulture, out int expInt))
        {
            result = op switch
            {
                "==" => i == expInt,
                "!=" => i != expInt,
                ">=" => i >= expInt,
                "<=" => i <= expInt,
                ">" => i > expInt,
                "<" => i < expInt,
                _ => false
            };
        }
        else
        {
            string expectedUnquoted = rawExpected.Trim('"', '\'');
            result = op switch
            {
                "==" => string.Equals(actualText, expectedUnquoted, StringComparison.Ordinal),
                "!=" => !string.Equals(actualText, expectedUnquoted, StringComparison.Ordinal),
                _ => throw new ArgumentException($"Operator '{op}' requires numeric operands in '{expression}' (actual={actualText}).")
            };
        }

        diagnostic = $"{field} {op} {rawExpected} (actual={actualText}) => {(result ? "PASS" : "FAIL")}";
        return result;
    }
}
