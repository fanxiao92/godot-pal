using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Pal.Core;
using Pal.Game.Profiles;
using Pal.Game.Resources;

namespace Pal.Game.Dialogue;

public enum StoryBattlePresentation { AttackMenu, TargetMenu, Resolving, Feedback }

/// <summary>Indexed native art and authored WORD labels. Main alone advances combat and presentation.</summary>
internal partial class PalStoryBattleView : Node2D
{
    // Modern demo WORD convention, not retail SDL IDs (SDL attack selection uses an icon).
    internal const int AttackWord = 60000, TargetWord = 60001, MissWord = 60002, CriticalWord = 60003, HpWord = 60004;
    private CheckedStoryProfile story = null!;
    private CheckedBattleVisualProfile visual = null!;
    private CheckedBattleVisualTeam team = null!;
    private readonly List<ImageTexture> owned = new();
    private readonly Dictionary<(bool Player, int Frame, int Shift), ImageTexture> frames = new();
    private ImageTexture background = null!;
    private ClassicBattleView combat = null!;
    private StoryBattlePresentation mode;
    private int pose;
    internal StoryEnemyAttackSnapshot? EnemyPresentation { get; private set; }
    internal int DisplayedPlayerHp => EnemyPresentation?.DisplayedPlayerHp ?? combat.PlayerHp;
    internal bool ResultVisible => EnemyPresentation?.ResultVisible ?? (combat.LastAction is not null);

    internal static void ValidateUi(CheckedStoryProfile story)
    {
        foreach (int id in new[] { AttackWord, TargetWord, MissWord, CriticalWord, HpWord })
        {
            if (!story.SelectedWords.TryGetValue(id, out string? text) || string.IsNullOrWhiteSpace(text))
                throw new InvalidDataException($"Story battle UI requires nonempty WORD {id}.");
            ValidateText(story.Font, text);
        }
        ValidateText(story.Font, "0123456789:/ ");
    }
    private static void ValidateText(PalDialogueFont font, string text)
    {
        foreach (var rune in text.EnumerateRunes()) if (rune.Value != ' ') _ = font.GetGlyph(rune.Value).IsSet(0, 0);
    }
    internal void Initialize(CheckedStoryProfile story, CheckedBattleVisualProfile visual, int teamId)
    {
        this.story = story; this.visual = visual; team = visual.Teams[teamId];
        background = Texture(team.Background.Width, team.Background.Height, team.Background.PaletteIndices, team.Background.Alpha);
        foreach (var frame in visual.PlayerSprite.Frames)
        {
            frames.Add((true, frame.Index, 0), Texture(frame.Width, frame.Height, frame.Indices, frame.Alpha));
            if (frame.Index == 4)
                frames.Add((true, frame.Index, 6), Texture(frame.Width, frame.Height, frame.Indices, frame.Alpha, 6));
        }
        foreach (var frame in team.EnemySprite.Frames)
            frames.Add((false, frame.Index, 0), Texture(frame.Width, frame.Height, frame.Indices, frame.Alpha));
        ZIndex = 100; TextureFilter = TextureFilterEnum.Nearest;
    }
    private ImageTexture Texture(int width, int height, byte[] indices, byte[] alpha, int shift = 0)
    {
        var bytes = new byte[indices.Length * 4];
        for (int i = 0; i < indices.Length; i++)
        {
            Color c = story.Palette[StoryEnemyAttackPresentation.ShiftIndex(indices[i], shift)];
            bytes[i * 4] = (byte)Mathf.RoundToInt(c.R * 255); bytes[i * 4 + 1] = (byte)Mathf.RoundToInt(c.G * 255);
            bytes[i * 4 + 2] = (byte)Mathf.RoundToInt(c.B * 255); bytes[i * 4 + 3] = alpha[i];
        }
        using var image = Image.CreateFromData(width, height, false, Image.Format.Rgba8, bytes);
        var result = ImageTexture.CreateFromImage(image); owned.Add(result); return result;
    }
    internal void Present(ClassicBattleView combat, StoryBattlePresentation mode, int pose, StoryEnemyAttackSnapshot? enemyPresentation = null)
    { this.combat = combat; this.mode = mode; this.pose = pose; EnemyPresentation = enemyPresentation; QueueRedraw(); }

    public override void _Draw()
    {
        if (combat is null) return;
        DrawTexture(background, Vector2.Zero);
        Vector2 player = visual.PlayerPosition, enemy = team.EnemyPosition;
        int pf = StoryEnemyAttackPresentation.RestFrame(combat.PlayerHp, story.BattleContent.Player.MaxHp), ef = 0;
        if (mode == StoryBattlePresentation.Feedback && combat.LastAction is { } action)
        {
            if (action.Attacker == ClassicBattleSide.Player)
            {
                // Minimal C player motion; frame7/full effects remain D2 (fight.c3670, 2079–2260).
                pf = pose < 3 ? 8 : 9;
                player = enemy + new Vector2(64 - (pose >= 2 ? 10 : 0) - (pose >= 3 ? 16 : 0), 20 - (pose >= 2 ? 2 : 0) - (pose >= 3 ? 4 : 0));
            }
        }
        int shift = 0;
        if (EnemyPresentation is { } shown)
        {
            pf = shown.PlayerFrame; ef = shown.EnemyFrame;
            player = shown.PlayerPosition; enemy = shown.EnemyPosition; shift = shown.PlayerColorShift;
        }
        Sprite(false, ef, enemy); Sprite(true, pf, player, shift);
        DrawRect(new Rect2(0, 174, 320, 26), new Color(0, 0, 0, .9f));
        Text(story.SelectedWords[HpWord] + ":" + DisplayedPlayerHp + "/" + combat.EnemyHp, new Vector2(4, 176));
        string label = mode == StoryBattlePresentation.AttackMenu ? story.SelectedWords[AttackWord]
            : mode == StoryBattlePresentation.TargetMenu ? story.SelectedWords[TargetWord] + ":" + team.TargetId
            : ResultVisible && combat.LastAction is { } last ? last.Evaded ? story.SelectedWords[MissWord]
            : (last.Critical ? story.SelectedWords[CriticalWord] + " " : "") + last.Damage : "";
        Text(label, new Vector2(155, 176));
    }
    private void Sprite(bool player, int frame, Vector2 bottomCenter, int shift = 0)
    {
        var texture = frames[(player, frame, shift)];
        DrawTexture(texture, bottomCenter - new Vector2(texture.GetWidth() / 2, texture.GetHeight()));
    }
    private void Text(string text, Vector2 position)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value == ' ') { position.X += 8; continue; }
            var glyph = story.Font.GetGlyph(rune.Value);
            for (int y = 0; y < glyph.Height; y++) for (int x = 0; x < glyph.Width; x++)
                if (glyph.IsSet(x, y)) DrawRect(new Rect2(position + new Vector2(x, y), Vector2.One), story.Palette[79]);
            position.X += glyph.Width;
        }
    }
    public override void _ExitTree() { ReleaseTextures(); }
    internal void ReleaseTextures() { foreach (var texture in owned) texture.Dispose(); owned.Clear(); frames.Clear(); }
}
