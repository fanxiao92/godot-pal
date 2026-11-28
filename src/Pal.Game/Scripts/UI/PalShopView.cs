using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Godot;
using Pal.Core;
using Pal.Game.Resources;
using Pal.Core.Menu;

namespace Pal.Game.UI;

/// <summary>#217 shop presentation. Draws the Core shop session's current state with the
/// prepared dialogue font and the active scene palette (authored layout, not retail art);
/// every drawn rune is validated against the font before presenting, so missing glyphs reject
/// explicitly instead of rendering placeholders. Input and state stay owned by Main/Core.</summary>
public sealed partial class PalShopView : Node2D
{
    private ExplorationShopSession? shop;
    private PalDialogueFont? font;
    private Color[] palette = [];
    private string[] buyLines = [];
    private int selectedBuyItem;
    private string buyStatusText = "";
    private string[] sellLines = [];
    private int selectedSellItem;
    private string sellStatusText = "";
    private string confirmNoText = "";
    private string confirmYesText = "";
    private int confirmIndex;
    // #217/#218: the prepared native BALL sprites keyed by shop object and the selected row's
    // resolved frame (uigame.c PAL_BuyMenu_OnItemChange and itemmenu.c draw the selected row's
    // item.wBitmap chunk; resolution happens in Present so a missing binding fails before any
    // partial frame is submitted, exactly like the glyph prevalidation).
    private IReadOnlyDictionary<int, PalSprite>? itemBitmaps;
    private (int ObjectId, int Bitmap, PalFrame Frame)? presentedItemBitmap;

    /// <summary>The selected row's presented BALL bitmap (object id, bitmap word, frame size);
    /// null while no row is selected or the binding carries the reference's empty-chunk word 0.</summary>
    public (int ObjectId, int Bitmap, int Width, int Height)? PresentedItemBitmap =>
        presentedItemBitmap is { } bitmap ? (bitmap.ObjectId, bitmap.Bitmap, bitmap.Frame.Width, bitmap.Frame.Height) : null;

    /// <summary>Publishes the session's current state for drawing. Composition and glyph
    /// validation happen here so an unrenderable state fails before any partial frame is
    /// submitted.</summary>
    public void Present(ExplorationShopSession shop, PalDialogueFont font, Color[] palette,
        IReadOnlyDictionary<int, PalSprite>? itemBitmaps = null)
    {
        ArgumentNullException.ThrowIfNull(shop);
        ArgumentNullException.ThrowIfNull(palette);
        if (palette.Length != 256) throw new InvalidDataException("Shop rendering requires the active 256-color scene palette.");
        this.shop = shop;
        this.font = font;
        this.palette = palette.ToArray();
        this.itemBitmaps = itemBitmaps;
        buyLines = [];
        buyStatusText = "";
        sellLines = [];
        sellStatusText = "";
        confirmNoText = "";
        confirmYesText = "";
        presentedItemBitmap = null;
        if (shop.Mode is ExplorationShopMode.BuyList or ExplorationShopMode.BuyConfirm)
        {
            var rows = shop.BuyRowsView();
            selectedBuyItem = shop.SelectedBuyItem;
            buyLines = rows.Select(row => $"{row.Name} {row.Price}").ToArray();
            var status = shop.BuyStatusView();
            buyStatusText = $"{status.CashLabel}:{status.Cash} {status.OwnedLabel}:{status.Owned}";
            presentedItemBitmap = ResolveItemBitmap(rows[selectedBuyItem].ObjectId, rows[selectedBuyItem].Bitmap);
            if (shop.Mode == ExplorationShopMode.BuyConfirm) LoadConfirm();
        }
        else if (shop.Mode is ExplorationShopMode.SellList or ExplorationShopMode.SellConfirm)
        {
            var rows = shop.SellRowsView();
            selectedSellItem = shop.SelectedSellItem;
            sellLines = rows.Select(row => $"{row.Name} X{row.Amount} {row.Price}").ToArray();
            var status = shop.SellStatusView();
            // The empty bag keeps the sell page presentable (itemmenu.c counts an empty
            // inventory without rejecting): uigame.c PAL_SellMenu_OnItemChange over object 0
            // draws the cash panel and leaves the price box empty; no row means no picture.
            sellStatusText = rows.Count == 0
                ? $"{status.CashLabel}:{status.Cash}"
                : $"{status.CashLabel}:{status.Cash} {status.PriceLabel}:{status.Price}";
            presentedItemBitmap = rows.Count > 0
                ? ResolveItemBitmap(rows[selectedSellItem].ObjectId, rows[selectedSellItem].Bitmap)
                : null;
            if (shop.Mode == ExplorationShopMode.SellConfirm) LoadConfirm();
        }
        PalMenuView.ValidateTexts(font!, buyLines.Append(buyStatusText).Concat(sellLines).Append(sellStatusText)
            .Append(confirmNoText).Append(confirmYesText));
        QueueRedraw();
        void LoadConfirm()
        {
            var confirm = shop.ConfirmView();
            confirmNoText = confirm.NoLabel;
            confirmYesText = confirm.YesLabel;
            confirmIndex = confirm.SelectedIndex;
        }
    }

    public override void _Draw()
    {
        if (shop is null || font is null || palette.Length != 256 || shop.Mode == ExplorationShopMode.Closed) return;
        DrawRect(new Rect2(0, 0, 320, 200), palette[0], filled: true);
        DrawRect(new Rect2(2, 2, 316, 196), PaletteColor(0x4F), width: 1);
        if (shop.Mode is ExplorationShopMode.BuyList or ExplorationShopMode.BuyConfirm)
        {
            // uigame.c PAL_BuyMenu: the store rows with their price numbers; the selected row's
            // item box draws the object's BALL bitmap beside the cash/owned panel.
            for (int index = 0; index < buyLines.Length; index++)
            {
                bool selected = index == selectedBuyItem;
                DrawBitmapText(buyLines[index], 16, 12 + index * 18, selected ? 5 : 0x4F);
                if (selected) DrawRect(new Rect2(8, 20 + index * 18, 6, 6), PaletteColor(5));
            }
            DrawBitmapText(buyStatusText, 150, 12, 0x4F);
            if (presentedItemBitmap is { } buyBitmap)
            {
                DrawRect(new Rect2(236, 30, 48, buyBitmap.Frame.Height + 8), PaletteColor(0x4F), width: 1);
                DrawItemBitmap(buyBitmap.Frame, 240, 34);
            }
            DrawConfirmBox();
            return;
        }
        // The sell pages (itemmenu.c over the compressed bag with sale prices; PAL_SellMenu's
        // cash and price panel beside the list).
        for (int index = 0; index < sellLines.Length; index++)
        {
            bool selected = index == selectedSellItem;
            DrawBitmapText(sellLines[index], 16, 12 + index * 18, selected ? 5 : 0x4F);
            if (selected) DrawRect(new Rect2(8, 20 + index * 18, 6, 6), PaletteColor(5));
        }
        DrawBitmapText(sellStatusText, 150, 12, 0x4F);
        if (presentedItemBitmap is { } sellBitmap)
        {
            DrawRect(new Rect2(236, 30, 48, sellBitmap.Frame.Height + 8), PaletteColor(0x4F), width: 1);
            DrawItemBitmap(sellBitmap.Frame, 240, 34);
        }
        DrawConfirmBox();
    }

    /// <summary>uigame.c PAL_ConfirmMenu's No/Yes box: two single rows, the cursor on the
    /// selected one; absent outside the confirm phases.</summary>
    private void DrawConfirmBox()
    {
        if (shop!.Mode is not (ExplorationShopMode.BuyConfirm or ExplorationShopMode.SellConfirm)) return;
        DrawRect(new Rect2(112, 96, 96, 40), palette[0], filled: true);
        DrawRect(new Rect2(112, 96, 96, 40), PaletteColor(0x4F), width: 1);
        DrawBitmapText(confirmNoText, 120, 100, confirmIndex == 0 ? 5 : 0x4F);
        DrawBitmapText(confirmYesText, 120, 118, confirmIndex == 1 ? 5 : 0x4F);
        DrawRect(new Rect2(114, 106 + confirmIndex * 18, 6, 6), PaletteColor(5));
    }

    /// <summary>Resolves one shop row's BALL bitmap through the prepared native chain. The
    /// empty-chunk word 0 draws no picture; any other word must have its prepared chunk bound
    /// — a missing binding fails closed instead of drawing a placeholder.</summary>
    private (int ObjectId, int Bitmap, PalFrame Frame)? ResolveItemBitmap(int objectId, int bitmap)
    {
        if (bitmap == 0) return null;
        if (itemBitmaps is null || !itemBitmaps.TryGetValue(objectId, out PalSprite? sprite))
            throw new InvalidDataException($"Shop object {objectId} bitmap {bitmap} has no prepared native item bitmap.");
        var frame = sprite.Frames.FirstOrDefault(candidate => candidate.Index == 0)
            ?? throw new InvalidDataException($"BALL chunk {bitmap} has no frame for shop object {objectId}.");
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
