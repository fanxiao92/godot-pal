using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Godot;
using Pal.Core;
using Pal.Game.Resources;

namespace Pal.Game.Dialogue;

/// <summary>#213 exploration-menu presentation. Draws the Core menu session's current state with
/// the prepared dialogue font and the active scene palette (authored layout, not retail art);
/// every drawn rune is validated against the font before presenting, so missing glyphs reject
/// explicitly instead of rendering placeholders. Input and state stay owned by Main/Core.</summary>
public sealed partial class PalMenuView : Node2D
{
    private ExplorationMenuSession? menu;
    private PalDialogueFont? font;
    private Color[] palette = [];
    private string[] entryLabels = [];
    private string[] statusLines = [];
    private string[] rightLines = [];
    private string nameText = "";
    private int selectedEntry;
    // #214 equipment page: composed row texts are validated here and only drawn afterwards.
    private string[] equipItemLines = [];
    private int selectedEquipItem;
    private string equipSelectionText = "";
    private string[] equipRoleLines = [];
    private string[] equipGearLines = [];
    private int selectedEquipRole;
    private bool equipRolesAnyEligible;

    internal ExplorationMenuMode Mode => menu?.Mode ?? ExplorationMenuMode.Closed;

    /// <summary>Preflight for every text the current menu state would draw. Throws with the font's
    /// resource path when any rune has no glyph, mirroring the dialogue display's prevalidation.</summary>
    internal static void ValidateTexts(PalDialogueFont font, IEnumerable<string> texts)
    {
        ArgumentNullException.ThrowIfNull(font);
        var checkedGlyphs = new HashSet<int>();
        foreach (string text in texts)
        {
            foreach (Rune rune in text.EnumerateRunes())
                if (checkedGlyphs.Add(rune.Value))
                {
                    try { font.GetGlyph(rune.Value).IsSet(0, 0); }
                    catch (InvalidDataException error) { throw new InvalidDataException($"{font.ResourcePath}: {error.Message}", error); }
                }
        }
    }

    /// <summary>Publishes the session's current state for drawing. Composition and glyph validation
    /// happen here so an unrenderable state fails before any partial frame is submitted.</summary>
    public void Present(ExplorationMenuSession menu, PalDialogueFont font, Color[] palette)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(palette);
        if (palette.Length != 256) throw new InvalidDataException("Menu rendering requires the active 256-color scene palette.");
        this.menu = menu;
        this.font = font;
        this.palette = palette.ToArray();
        selectedEntry = menu.SelectedEntry;
        entryLabels = Enumerable.Range(0, menu.Entries.Count).Select(menu.EntryLabel).ToArray();
        statusLines = [];
        rightLines = [];
        nameText = "";
        equipItemLines = [];
        equipSelectionText = "";
        equipRoleLines = [];
        equipGearLines = [];
        if (menu.Mode == ExplorationMenuMode.Status)
        {
            var view = menu.StatusView();
            nameText = view.Name;
            statusLines =
            [
                $"{view.LevelLabel}:{view.Level}",
                $"{view.HpLabel}:{view.Hp}/{view.MaxHp}",
                $"{view.MpLabel}:{view.Mp}/{view.MaxMp}",
                $"{view.ExperienceLabel}:{view.Experience}/{view.NextLevelExperience}",
            ];
            rightLines =
            [
                $"{view.AttackLabel}:{view.Attack}",
                $"{view.MagicPowerLabel}:{view.MagicPower}",
                $"{view.DefenseLabel}:{view.Defense}",
                $"{view.DexterityLabel}:{view.Dexterity}",
                $"{view.FleeRateLabel}:{view.FleeRate}",
                .. view.Equipment.Select(equipment => equipment.Name),
                .. view.Poisons.Select(poison => poison.Name),
            ];
        }
        else if (menu.Mode == ExplorationMenuMode.EquipItems)
        {
            var rows = menu.EquipItemsView();
            selectedEquipItem = menu.SelectedEquipItem;
            equipItemLines = rows.Select(row => $"{row.PartLabel} {row.Name} X{row.Amount}").ToArray();
            nameText = equipItemLines.Length > 0 ? equipItemLines[Math.Clamp(selectedEquipItem, 0, equipItemLines.Length - 1)] : "";
        }
        else if (menu.Mode == ExplorationMenuMode.EquipRoles)
        {
            var selection = menu.EquipRolesView();
            selectedEquipRole = menu.SelectedEquipRole;
            equipSelectionText = $"{selection.PartLabel} {selection.Name}";
            equipRolesAnyEligible = selection.Roles.Any(role => role.Eligible);
            equipRoleLines = selection.Roles.Select(role => role.Name).ToArray();
            equipGearLines = selection.Roles[Math.Clamp(selectedEquipRole, 0, selection.Roles.Count - 1)].Gear.ToArray();
        }
        ValidateTexts(font!, entryLabels.Concat(statusLines).Concat(rightLines).Append(nameText)
            .Concat(equipItemLines).Append(equipSelectionText).Concat(equipRoleLines).Concat(equipGearLines));
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (menu is null || font is null || palette.Length != 256 || menu.Mode == ExplorationMenuMode.Closed) return;
        if (menu.Mode == ExplorationMenuMode.MainMenu)
        {
            float height = entryLabels.Length * 18 + 8;
            DrawRect(new Rect2(6, 42, 104, height), palette[0], filled: true);
            DrawRect(new Rect2(6, 42, 104, height), PaletteColor(0x4F), width: 1);
            for (int index = 0; index < entryLabels.Length; index++)
            {
                bool selected = index == selectedEntry;
                DrawBitmapText(entryLabels[index], 18, 46 + index * 18, selected ? 5 : 0x4F);
                if (selected) DrawRect(new Rect2(10, 52 + index * 18, 6, 6), PaletteColor(5));
            }
            return;
        }
        DrawRect(new Rect2(0, 0, 320, 200), palette[0], filled: true);
        DrawRect(new Rect2(2, 2, 316, 196), PaletteColor(0x4F), width: 1);
        if (menu.Mode == ExplorationMenuMode.EquipItems)
        {
            // itemmenu.c: the equipable stock list with owned amounts.
            for (int index = 0; index < equipItemLines.Length; index++)
            {
                bool selected = index == selectedEquipItem;
                DrawBitmapText(equipItemLines[index], 16, 12 + index * 18, selected ? 5 : 0x4F);
                if (selected) DrawRect(new Rect2(8, 20 + index * 18, 6, 6), PaletteColor(5));
            }
            return;
        }
        if (menu.Mode == ExplorationMenuMode.EquipRoles)
        {
            // uigame.c PAL_EquipItemMenu: the item header, the party role box (an ineligible
            // selected role keeps the plain label color — the reference's inactive highlight)
            // and the selected role's currently equipped objects.
            DrawBitmapText(equipSelectionText, 16, 8, 5);
            for (int index = 0; index < equipRoleLines.Length; index++)
            {
                bool selected = index == selectedEquipRole;
                DrawBitmapText(equipRoleLines[index], 16, 34 + index * 18, selected && equipRolesAnyEligible ? 5 : 0x4F);
                if (selected) DrawRect(new Rect2(8, 42 + index * 18, 6, 6), PaletteColor(equipRolesAnyEligible ? 5 : 0x4F));
            }
            for (int index = 0; index < equipGearLines.Length; index++)
                DrawBitmapText(equipGearLines[index], 150, 34 + index * 16, 0x4F);
            return;
        }
        DrawBitmapText(nameText, 16, 8, 5);
        for (int index = 0; index < statusLines.Length; index++)
            DrawBitmapText(statusLines[index], 16, 30 + index * 18, 0x4F);
        for (int index = 0; index < rightLines.Length; index++)
            DrawBitmapText(rightLines[index], 150, 30 + index * 16, 0x4F);
    }

    private int DrawBitmapText(string text, int x, int y, int colorIndex)
    {
        foreach (Rune rune in text.EnumerateRunes())
        {
            PalDialogueGlyph glyph = font!.GetGlyph(rune.Value);
            for (int row = 0; row < glyph.Height; row++)
            for (int column = 0; column < glyph.Width; column++)
            {
                if (!glyph.IsSet(column, row)) continue;
                DrawRect(new Rect2(x + column + 1, y + row, 1, 1), palette[0]);
                DrawRect(new Rect2(x + column, y + row + 1, 1, 1), palette[0]);
                DrawRect(new Rect2(x + column + 1, y + row + 1, 1, 1), palette[0]);
                DrawRect(new Rect2(x + column, y + row, 1, 1), PaletteColor(colorIndex));
            }
            x += glyph.Width;
        }
        return x;
    }

    private Color PaletteColor(int index) => palette[index];
}
