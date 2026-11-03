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
    private StoryPlayerAttackSnapshot? playerPresentation;
    private int idleFrame;
    private int idleCounter;
    private readonly Dictionary<int, ImageTexture> effects = new();
    internal StoryEnemyAttackSnapshot? EnemyPresentation { get; private set; }
    internal int DisplayedPlayerHp => DrawSnapshot.DisplayedPlayerHp;
    internal int DisplayedEnemyHp => DrawSnapshot.DisplayedEnemyHp;
    internal bool ResultVisible => DrawSnapshot.ResultVisible;

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
        foreach (var frame in team.EnemySprite.Frames)
            frames.Add((false, frame.Index, 6), Texture(frame.Width, frame.Height, frame.Indices, frame.Alpha, 6));
        for (int i = 0; i < visual.AttackEffectFrames.Count; i++)
        {
            var frame = visual.AttackEffectFrames[i];
            effects.Add(i, Texture(frame.Width, frame.Height, frame.Indices, frame.Alpha));
        }
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
    internal void Present(ClassicBattleView combat, StoryBattlePresentation mode, int idleFrame, int idleCounter,
        StoryEnemyAttackSnapshot? enemyPresentation = null, StoryPlayerAttackSnapshot? playerPresentation = null)
    {
        this.combat = combat; this.mode = mode; this.idleFrame = idleFrame; this.idleCounter = idleCounter;
        EnemyPresentation = enemyPresentation; this.playerPresentation = playerPresentation;
        BuildDrawSnapshot(); QueueRedraw();
    }

    internal StoryBattleDrawSnapshot DrawSnapshot { get; private set; }
    private void BuildDrawSnapshot()
    {
        Vector2 player = visual.PlayerPosition, enemy = team.EnemyPosition, fxPosition = Vector2.Zero;
        int pf = StoryEnemyAttackPresentation.RestFrame(combat.PlayerHp, story.BattleContent.Player.MaxHp), ef = idleFrame;
        int playerShift = 0, enemyShift = 0, php = combat.PlayerHp, ehp = combat.EnemyHp;
        int? fx = null;
        bool result = false;
        if (EnemyPresentation is { } shown)
        {
            pf = shown.PlayerFrame; ef = shown.EnemyFrame;
            player = shown.PlayerPosition; enemy = shown.EnemyPosition; playerShift = shown.PlayerColorShift;
            php = shown.DisplayedPlayerHp; result = shown.ResultVisible;
        }
        if (playerPresentation is { } hit)
        {
            pf = hit.PlayerFrame; player = hit.PlayerPosition; enemy = hit.EnemyPosition;
            enemyShift = hit.EnemyColorShift; ehp = hit.DisplayedEnemyHp; result = hit.ResultVisible;
            fx = hit.EffectFrame; fxPosition = hit.EffectPosition;
        }
        DrawSnapshot = new(pf, ef, player, enemy, playerShift, enemyShift, fx, fxPosition, php, ehp, result, idleCounter);
    }

    public override void _Draw()
    {
        if (combat is null) return;
        DrawTexture(background, Vector2.Zero);
        var shown = DrawSnapshot;
        Sprite(false, shown.EnemyFrame, shown.EnemyPosition, shown.EnemyColorShift); Sprite(true, shown.PlayerFrame, shown.PlayerPosition, shown.PlayerColorShift);
        if (shown.EffectFrame is { } effect)
        {
            var texture = effects[effect];
            DrawTexture(texture, shown.EffectPosition - new Vector2(texture.GetWidth() / 2, texture.GetHeight()));
        }
        DrawRect(new Rect2(0, 174, 320, 26), new Color(0, 0, 0, .9f));
        Text(story.SelectedWords[HpWord] + ":" + DisplayedPlayerHp + "/" + DisplayedEnemyHp, new Vector2(4, 176));
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
    internal void ReleaseTextures() { foreach (var texture in owned) texture.Dispose(); owned.Clear(); frames.Clear(); effects.Clear(); }
}

internal readonly record struct StoryBattleDrawSnapshot(int PlayerFrame, int EnemyFrame, Vector2 PlayerPosition, Vector2 EnemyPosition, int PlayerColorShift,
    int EnemyColorShift, int? EffectFrame, Vector2 EffectPosition, int DisplayedPlayerHp, int DisplayedEnemyHp,
    bool ResultVisible, int EnemyIdleCounter);
