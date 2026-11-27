using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Godot;
using Pal.Core;
using Pal.Game.Resources;
using Pal.Core.Menu;
using Pal.Core.Save;

namespace Pal.Game.UI;

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
    // #215 items page: composed row texts are validated here and only drawn afterwards.
    private string[] useItemLines = [];
    private int selectedUseItem;
    private string useHeaderText = "";
    private string[] useTargetLines = [];
    private string[] useTargetStatLines = [];
    private int selectedUseRole;
    // #218 item bitmaps: the prepared native BALL sprites keyed by use-menu object and the
    // selected row's resolved frame; resolution happens in Present so a missing binding fails
    // before any partial frame is submitted, exactly like the glyph prevalidation.
    private IReadOnlyDictionary<int, PalSprite>? itemBitmaps;
    private (int ObjectId, int Bitmap, PalFrame Frame)? presentedItemBitmap;
    // #216 magic page: composed row texts are validated here and only drawn afterwards.
    private string[] magicCasterLines = [];
    private bool[] magicCasterAlive = [];
    private int selectedMagicCaster;
    private string[] magicLines = [];
    private int selectedMagicRow;
    private string magicHeaderText = "";
    private string[] magicTargetLines = [];
    private string[] magicTargetStatLines = [];
    private int selectedMagicTarget;
    // #224 system layer: the five manual slot rows (uigame.c PAL_SaveSlotMenu) with the
    // remembered cursor; composed row texts are validated like every other page.
    private string[] saveSlotLines = [];
    private int selectedSaveSlot;
    // #227 system items: the reference system menu's SAVE/LOAD rows (uigame.c PAL_SystemMenu)
    // with the remembered cursor; composed row texts are validated like every other page.
    private string[] systemItemLines = [];
    private int selectedSystemItem;

    internal ExplorationMenuMode Mode => menu?.Mode ?? ExplorationMenuMode.Closed;

    /// <summary>The selected item row's presented BALL bitmap (object id, bitmap word, frame
    /// size); null while no item row is selected or the binding carries the reference's
    /// empty-chunk word 0 (PAL_MKFReadChunk returns no bitmap, so no picture is drawn).</summary>
    public (int ObjectId, int Bitmap, int Width, int Height)? PresentedItemBitmap =>
        presentedItemBitmap is { } bitmap ? (bitmap.ObjectId, bitmap.Bitmap, bitmap.Frame.Width, bitmap.Frame.Height) : null;

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
    public void Present(ExplorationMenuSession menu, PalDialogueFont font, Color[] palette,
        IReadOnlyDictionary<int, PalSprite>? itemBitmaps = null)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(palette);
        if (palette.Length != 256) throw new InvalidDataException("Menu rendering requires the active 256-color scene palette.");
        this.menu = menu;
        this.font = font;
        this.palette = palette.ToArray();
        this.itemBitmaps = itemBitmaps;
        selectedEntry = menu.SelectedEntry;
        entryLabels = Enumerable.Range(0, menu.Entries.Count).Select(menu.EntryLabel).ToArray();
        statusLines = [];
        rightLines = [];
        nameText = "";
        equipItemLines = [];
        equipSelectionText = "";
        equipRoleLines = [];
        equipGearLines = [];
        useItemLines = [];
        useHeaderText = "";
        useTargetLines = [];
        useTargetStatLines = [];
        presentedItemBitmap = null;
        magicCasterLines = [];
        magicCasterAlive = [];
        magicLines = [];
        magicHeaderText = "";
        magicTargetLines = [];
        magicTargetStatLines = [];
        saveSlotLines = [];
        selectedSaveSlot = menu.SelectedSaveSlot;
        systemItemLines = [];
        selectedSystemItem = menu.SelectedSystemItem;
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
        else if (menu.Mode is ExplorationMenuMode.Items or ExplorationMenuMode.ItemScript)
        {
            // itemmenu.c: the usable stock list with available amounts; the equipped appendix
            // rows mark their equipment origin instead of counting it as stock. The selected
            // row's BALL bitmap resolves now (PAL_MKFReadChunk+PAL_RLEBlitToSurface of the
            // item's wBitmap chunk; the empty-chunk word 0 draws no picture).
            var rows = menu.ItemsView();
            selectedUseItem = menu.SelectedEquipItem;
            useItemLines = rows.Select(row =>
                (row.Equipped ? "* " : "") + row.Name + (row.Equipped ? "" : $" X{row.Amount}")).ToArray();
            nameText = useItemLines.Length > 0 ? useItemLines[Math.Clamp(selectedUseItem, 0, useItemLines.Length - 1)] : "";
            presentedItemBitmap = rows.Count > 0
                ? ResolveItemBitmap(rows[Math.Clamp(selectedUseItem, 0, rows.Count - 1)].ObjectId,
                    rows[Math.Clamp(selectedUseItem, 0, rows.Count - 1)].Bitmap) : null;
        }
        else if (menu.Mode == ExplorationMenuMode.MagicCasters)
        {
            // uigame.c PAL_InGameMagicMenu's caster select: the whole party with its live alive
            // flag — dead members stay listed but disabled, so the cursor never highlights one.
            var rows = menu.MagicCastersView();
            selectedMagicCaster = menu.SelectedMagicCaster;
            magicCasterAlive = rows.Select(row => row.Alive).ToArray();
            magicCasterLines = rows.Select(row => row.Name).ToArray();
        }
        else if (menu.Mode is ExplorationMenuMode.MagicList or ExplorationMenuMode.MagicOnUse
            or ExplorationMenuMode.MagicOnSuccess)
        {
            // magicmenu.c: the caster's learned magics with their lprgMagic costs; the parked
            // script modes keep the same list behind the cast's dialogue.
            var rows = menu.MagicListView();
            magicLines = rows.Select(row => $"{row.Name} {row.CostMp}").ToArray();
            int index = 0;
            for (int row = 0; row < rows.Count; row++)
                if (rows[row].ObjectId == menu.SelectedMagic) { index = row; break; }
            selectedMagicRow = index;
        }
        else if (menu.Mode == ExplorationMenuMode.MagicTargets)
        {
            // uigame.c's player loop: the magic header, the party rows and the selected member's
            // live HP/MP words (the reference draws the full stat box while picking).
            var selection = menu.MagicTargetsView();
            selectedMagicTarget = menu.SelectedMagicTarget;
            magicHeaderText = selection.Name;
            magicTargetLines = selection.Targets.Select(target => target.Name).ToArray();
            var target = selection.Targets[Math.Clamp(selectedMagicTarget, 0, selection.Targets.Count - 1)];
            magicTargetStatLines = [$"{selection.HpLabel}:{target.Hp}/{target.MaxHp}",
                $"{selection.MpLabel}:{target.Mp}/{target.MaxMp}"];
        }
        else if (menu.Mode == ExplorationMenuMode.SystemItems)
        {
            // uigame.c PAL_SystemMenu: the M6-C item rows (SAVE/LOAD) with the remembered cursor;
            // the reference's remaining system items stay outside this milestone's scope.
            selectedSystemItem = menu.SelectedSystemItem;
            systemItemLines = Enumerable.Range(0, menu.SystemItemCount).Select(menu.SystemItemLabel).ToArray();
        }
        else if (menu.Mode == ExplorationMenuMode.SystemMenu)
        {
            // uigame.c PAL_SaveSlotMenu: the five manual slots with the remembered cursor; the
            // row text is the slot number itself (digits share the menu's numeric coverage).
            saveSlotLines = Enumerable.Range(PlayerSaveContract.FirstSlot, PlayerSaveContract.SaveSlotCount)
                .Select(slot => slot.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        }
        else if (menu.Mode == ExplorationMenuMode.ItemTargets)
        {
            // uigame.c PAL_ItemUseMenu: the item header, the party rows and the selected member's
            // live HP/MP words (the reference draws the full stat box while picking), with the
            // item's BALL bitmap picture beside the rows.
            var selection = menu.ItemTargetsView();
            selectedUseRole = menu.SelectedUseRole;
            useHeaderText = selection.Name;
            useTargetLines = selection.Targets.Select(target => target.Name).ToArray();
            var target = selection.Targets[Math.Clamp(selectedUseRole, 0, selection.Targets.Count - 1)];
            useTargetStatLines = [$"{selection.HpLabel}:{target.Hp}/{target.MaxHp}",
                $"{selection.MpLabel}:{target.Mp}/{target.MaxMp}"];
            presentedItemBitmap = ResolveItemBitmap(selection.ObjectId, selection.Bitmap);
        }
        ValidateTexts(font!, entryLabels.Concat(statusLines).Concat(rightLines).Append(nameText)
            .Concat(equipItemLines).Append(equipSelectionText).Concat(equipRoleLines).Concat(equipGearLines)
            .Concat(useItemLines).Append(useHeaderText).Concat(useTargetLines).Concat(useTargetStatLines)
            .Concat(magicCasterLines).Concat(magicLines).Append(magicHeaderText)
            .Concat(magicTargetLines).Concat(magicTargetStatLines).Concat(saveSlotLines)
            .Concat(systemItemLines));
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
        if (menu.Mode == ExplorationMenuMode.SystemItems)
        {
            // uigame.c PAL_SystemMenu: the M6-C SAVE/LOAD item rows with the remembered cursor.
            for (int index = 0; index < systemItemLines.Length; index++)
            {
                bool selected = index == selectedSystemItem;
                DrawBitmapText(systemItemLines[index], 16, 12 + index * 18, selected ? 5 : 0x4F);
                if (selected) DrawRect(new Rect2(8, 20 + index * 18, 6, 6), PaletteColor(5));
            }
            return;
        }
        if (menu.Mode == ExplorationMenuMode.SystemMenu)
        {
            // uigame.c PAL_SaveSlotMenu: the five manual slot rows with the remembered cursor.
            for (int index = 0; index < saveSlotLines.Length; index++)
            {
                bool selected = PlayerSaveContract.FirstSlot + index == selectedSaveSlot;
                DrawBitmapText(saveSlotLines[index], 16, 12 + index * 18, selected ? 5 : 0x4F);
                if (selected) DrawRect(new Rect2(8, 20 + index * 18, 6, 6), PaletteColor(5));
            }
            return;
        }
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
        if (menu.Mode is ExplorationMenuMode.Items or ExplorationMenuMode.ItemScript)
        {
            // itemmenu.c: the usable stock list with available amounts; equipped appendix rows
            // carry the * marker and no stock count. The selected row's item box draws the
            // object's BALL bitmap (PAL_RLEBlitToSurface with the active screen palette).
            for (int index = 0; index < useItemLines.Length; index++)
            {
                bool selected = index == selectedUseItem;
                DrawBitmapText(useItemLines[index], 16, 12 + index * 18, selected ? 5 : 0x4F);
                if (selected) DrawRect(new Rect2(8, 20 + index * 18, 6, 6), PaletteColor(5));
            }
            if (presentedItemBitmap is { } itemBitmap)
            {
                DrawRect(new Rect2(236, 26, 48, itemBitmap.Frame.Height + 8), PaletteColor(0x4F), width: 1);
                DrawItemBitmap(itemBitmap.Frame, 240, 30);
            }
            return;
        }
        if (menu.Mode == ExplorationMenuMode.ItemTargets)
        {
            // uigame.c PAL_ItemUseMenu: the item header, the party rows and the selected member's
            // live HP/MP words.
            DrawBitmapText(useHeaderText, 16, 8, 5);
            for (int index = 0; index < useTargetLines.Length; index++)
            {
                bool selected = index == selectedUseRole;
                DrawBitmapText(useTargetLines[index], 16, 34 + index * 18, selected ? 5 : 0x4F);
                if (selected) DrawRect(new Rect2(8, 42 + index * 18, 6, 6), PaletteColor(5));
            }
            for (int index = 0; index < useTargetStatLines.Length; index++)
                DrawBitmapText(useTargetStatLines[index], 150, 34 + index * 16, 0x4F);
            // uigame.c PAL_ItemUseMenu: the item's BALL bitmap picture beside the party rows.
            if (presentedItemBitmap is { } targetBitmap)
            {
                DrawRect(new Rect2(146, 96, 48, targetBitmap.Frame.Height + 8), PaletteColor(0x4F), width: 1);
                DrawItemBitmap(targetBitmap.Frame, 150, 100);
            }
            return;
        }
        if (menu.Mode == ExplorationMenuMode.MagicCasters)
        {
            // uigame.c's caster select: the party rows; a dead selected member keeps the plain
            // label color (the reference's disabled item), and the cursor only highlights when
            // the selection can actually confirm.
            for (int index = 0; index < magicCasterLines.Length; index++)
            {
                bool selected = index == selectedMagicCaster;
                bool alive = magicCasterAlive.Length > index && magicCasterAlive[index];
                DrawBitmapText(magicCasterLines[index], 16, 12 + index * 18, selected && alive ? 5 : 0x4F);
                if (selected) DrawRect(new Rect2(8, 20 + index * 18, 6, 6), PaletteColor(alive ? 5 : 0x4F));
            }
            return;
        }
        if (menu.Mode is ExplorationMenuMode.MagicList or ExplorationMenuMode.MagicOnUse
            or ExplorationMenuMode.MagicOnSuccess)
        {
            // magicmenu.c: the learned-magic list with each magic's cost word; the parked script
            // modes draw the same list behind the cast's dialogue.
            for (int index = 0; index < magicLines.Length; index++)
            {
                bool selected = index == selectedMagicRow;
                DrawBitmapText(magicLines[index], 16, 12 + index * 18, selected ? 5 : 0x4F);
                if (selected) DrawRect(new Rect2(8, 20 + index * 18, 6, 6), PaletteColor(5));
            }
            return;
        }
        if (menu.Mode == ExplorationMenuMode.MagicTargets)
        {
            // uigame.c's player loop: the magic header, the party rows and the selected member's
            // live HP/MP words.
            DrawBitmapText(magicHeaderText, 16, 8, 5);
            for (int index = 0; index < magicTargetLines.Length; index++)
            {
                bool selected = index == selectedMagicTarget;
                DrawBitmapText(magicTargetLines[index], 16, 34 + index * 18, selected ? 5 : 0x4F);
                if (selected) DrawRect(new Rect2(8, 42 + index * 18, 6, 6), PaletteColor(5));
            }
            for (int index = 0; index < magicTargetStatLines.Length; index++)
                DrawBitmapText(magicTargetStatLines[index], 150, 34 + index * 16, 0x4F);
            return;
        }
        DrawBitmapText(nameText, 16, 8, 5);
        for (int index = 0; index < statusLines.Length; index++)
            DrawBitmapText(statusLines[index], 16, 30 + index * 18, 0x4F);
        for (int index = 0; index < rightLines.Length; index++)
            DrawBitmapText(rightLines[index], 150, 30 + index * 16, 0x4F);
    }

    /// <summary>Resolves one use-menu row's BALL bitmap through the prepared native chain.
    /// The empty-chunk word 0 draws no picture (itemmenu.c/uigame.c's read guard); any other
    /// word must have its prepared chunk bound — a missing binding fails closed instead of
    /// drawing a placeholder.</summary>
    private (int ObjectId, int Bitmap, PalFrame Frame)? ResolveItemBitmap(int objectId, int bitmap)
    {
        if (bitmap == 0) return null;
        if (itemBitmaps is null || !itemBitmaps.TryGetValue(objectId, out PalSprite? sprite))
            throw new InvalidDataException($"Use-menu object {objectId} bitmap {bitmap} has no prepared native item bitmap.");
        var frame = sprite.Frames.FirstOrDefault(candidate => candidate.Index == 0)
            ?? throw new InvalidDataException($"BALL chunk {bitmap} has no frame for use-menu object {objectId}.");
        return (objectId, bitmap, frame);
    }

    private void DrawItemBitmap(PalFrame frame, int x, int y)
    {
        // PAL_RLEBlitToSurface: every visible pixel is one palette index of the active screen
        // palette; transparent (alpha 0) pixels leave the underlying box untouched.
        for (int row = 0; row < frame.Height; row++)
            for (int column = 0; column < frame.Width; column++)
            {
                int pixel = row * frame.Width + column;
                if (frame.Alpha[pixel] == 0) continue;
                DrawRect(new Rect2(x + column, y + row, 1, 1), PaletteColor(frame.Indices[pixel]));
            }
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
