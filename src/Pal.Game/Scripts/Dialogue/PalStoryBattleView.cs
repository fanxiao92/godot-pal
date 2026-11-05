using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Pal.Core;
using Pal.Game.Profiles;
using Pal.Game.Resources;

namespace Pal.Game.Dialogue;

public enum StoryBattlePresentation { AttackMenu, TargetMenu, Resolving, Feedback, HookDialogue, Settlement }

/// <summary>Indexed native art and authored WORD labels. Main alone advances combat and presentation.</summary>
internal partial class PalStoryBattleView : Node2D
{
    // Modern demo WORD convention, not retail SDL IDs (SDL attack selection uses an icon).
    internal const int AttackWord = 60000, TargetWord = 60001, MissWord = 60002, CriticalWord = 60003, HpWord = 60004, BoostWord = 60005;
    // #50 settlement labels (battle.c 1019-1032/1130/1314: exp/cash box, level-up, learned magic).
    internal const int ExpWord = 60006, CashWord = 60007, LevelUpWord = 60008, LearnedMagicWord = 60009;
    // #51 failed escape label (fight.c 4168 BATTLE_LABEL_ESCAPEFAIL); authored WORD, not retail SDL id 31.
    internal const int EscapeFailWord = 60010;
    private CheckedStoryProfile story = null!;
    private CheckedBattleVisualProfile visual = null!;
    private CheckedBattleVisualTeam team = null!;
    private readonly List<ImageTexture> owned = new();
    private readonly Dictionary<(bool Player, int Frame, int Shift), ImageTexture> frames = new();
    private ImageTexture background = null!;
    private ClassicBattleView combat = null!;
    private StoryBattlePresentation mode;
    private int highlightedSlot;
    private StoryPlayerAttackSnapshot? playerPresentation;
    private StoryPlayerFleeSnapshot? fleePresentation;
    private int idleFrame;
    private int idleCounter;
    private readonly Dictionary<int, ImageTexture> effects = new();
    private readonly Dictionary<(StoryNumberColor Color, int Digit), ImageTexture> numbers = new();
    internal StoryEnemyAttackSnapshot? EnemyPresentation { get; private set; }
    internal int DisplayedPlayerHp => DrawSnapshot.DisplayedPlayerHp;
    internal int DisplayedEnemyHp => DrawSnapshot.DisplayedEnemyHp;
    internal bool ResultVisible => DrawSnapshot.ResultVisible;

    internal static void ValidateUi(CheckedStoryProfile story)
    {
        foreach (int id in new[] { AttackWord, TargetWord, MissWord, CriticalWord, HpWord, BoostWord,
            ExpWord, CashWord, LevelUpWord, LearnedMagicWord, EscapeFailWord })
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
    internal void Initialize(CheckedStoryProfile story, CheckedBattleVisualProfile visual, int enemyTeamId)
    {
        this.story = story; this.visual = visual; team = visual.Teams[enemyTeamId];
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
        foreach (var (color, group) in new[] { (StoryNumberColor.Yellow, visual.YellowNumberFrames),
            (StoryNumberColor.Blue, visual.BlueNumberFrames), (StoryNumberColor.Cyan, visual.CyanNumberFrames) })
            for (int digit = 0; digit < group.Count; digit++)
            {
                var frame = group[digit];
                numbers.Add((color, digit), Texture(frame.Width, frame.Height, frame.Indices, frame.Alpha));
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
        StoryEnemyAttackSnapshot? enemyPresentation = null, StoryPlayerAttackSnapshot? playerPresentation = null,
        IReadOnlyList<StoryNumberDigit>? numberDigits = null, int? feedbackWord = null, int highlightedEnemySlot = 0,
        string? settlementLine = null, StoryPlayerFleeSnapshot? fleePresentation = null)
    {
        this.combat = combat; this.mode = mode; this.idleFrame = idleFrame; this.idleCounter = idleCounter;
        highlightedSlot = highlightedEnemySlot;
        EnemyPresentation = enemyPresentation; this.playerPresentation = playerPresentation;
        this.fleePresentation = fleePresentation;
        NumberDigits = numberDigits ?? Array.Empty<StoryNumberDigit>();
        FeedbackWord = feedbackWord;
        SettlementLine = settlementLine;
        BuildDrawSnapshot(); QueueRedraw();
    }

    internal IReadOnlyList<StoryNumberDigit> NumberDigits { get; private set; } = Array.Empty<StoryNumberDigit>();
    internal int? FeedbackWord { get; private set; }
    /// <summary>The current victory-settlement line (authored WORD labels plus numbers); null outside Settlement.</summary>
    internal string? SettlementLine { get; private set; }
    internal StoryBattleDrawSnapshot DrawSnapshot { get; private set; }
    private void BuildDrawSnapshot()
    {
        // Party battles draw every actor; the legacy scalar fields remain index-zero projections.
        bool party = combat.Players.Count > 1;
        var action = combat.LastAction;
        var partyPlayers = party ? PlayerDraws(action) : Array.Empty<StoryActorDraw>();
        var partyEnemies = party ? EnemyDraws(action) : Array.Empty<StoryActorDraw>();
        Vector2 player = visual.PlayerPosition, enemy = team.EnemySlots[0].EnemyPosition, fxPosition = Vector2.Zero;
        int pf = StoryEnemyAttackPresentation.RestFrame(combat.PlayerHp, story.BattleContent.Players[0].MaxHp), ef = idleFrame;
        int playerShift = 0, enemyShift = 0, php = combat.PlayerHp, ehp = combat.EnemyHp;
        int? fx = null;
        bool result = false;
        if (fleePresentation is { } fleeing)
        {
            // fight.c 4153-4171: the failed escaper steps +4/+2 three times, then stands at
            // frame 1 under the escape-fail label; no strike, effect or result frame shows.
            pf = fleeing.Frame;
            player = visual.PlayerPosition + fleeing.Offset;
        }
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
        if (party)
        {
            php = partyPlayers[0].Hp; ehp = partyEnemies[0].Hp;
            pf = partyPlayers[0].Frame; player = partyPlayers[0].Position; playerShift = partyPlayers[0].ColorShift;
            ef = partyEnemies[0].Frame; enemy = partyEnemies[0].Position; enemyShift = partyEnemies[0].ColorShift;
        }
        DrawSnapshot = new(pf, ef, player, enemy, playerShift, enemyShift, fx, fxPosition, php, ehp, result, idleCounter)
        { NumberDigits = NumberDigits, FeedbackWord = FeedbackWord, PartyPlayers = partyPlayers, PartyEnemies = partyEnemies,
            SelectingPlayer = party ? combat.SelectingPlayerIndex : null,
            PartyBoosts = BoostSums(combat) };
    }

    // The boost panel exists only while a role-hook boost register actually changed a member's
    // panel (script.c 0x0030); each entry is that member's five-register sum.
    private static int[] BoostSums(ClassicBattleView combat)
    {
        var sums = new int[combat.Players.Count];
        for (int i = 0; i < sums.Length; i++)
        {
            var member = combat.Players[i];
            sums[i] = member.BoostAttack + member.BoostMagicPower + member.BoostDefense + member.BoostDexterity + member.BoostFlee;
        }
        return sums.Any(value => value != 0) ? sums : Array.Empty<int>();
    }

    private StoryActorDraw[] PlayerDraws(ClassicBattleAction? action)
    {
        var origins = visual.PlayerOrigins(combat.Players.Count);
        var draws = new StoryActorDraw[combat.Players.Count];
        for (int i = 0; i < draws.Length; i++)
        {
            var member = combat.Players[i];
            // fight.c:5012-5022: while a member covers, it steps next to the target with the
            // defend frame 3 instead of the target reacting.
            if (action is { Attacker: ClassicBattleSide.Enemy, CovererPartyIndex: var coverer } && coverer == i
                && EnemyPresentation is not null)
                draws[i] = new(3, origins[action.TargetIndex] + new Vector2(-24, -12), 0, member.Hp);
            else if (fleePresentation is { } fleeingMember && fleeingMember.PartyIndex == i)
                draws[i] = new(fleeingMember.Frame, origins[i] + fleeingMember.Offset, 0, member.Hp);
            else
                draws[i] = action is { Attacker: ClassicBattleSide.Player, ActorIndex: var actor } && actor == i
                    && playerPresentation is { } acting
                    ? new(acting.PlayerFrame, acting.PlayerPosition, 0, member.Hp)
                    : new(StoryEnemyAttackPresentation.RestFrame(member.Hp, story.BattleContent.Players[i].MaxHp),
                        origins[i], 0, member.Hp);
        }
        return draws;
    }

    private StoryActorDraw[] EnemyDraws(ClassicBattleAction? action)
    {
        var draws = new StoryActorDraw[combat.Enemies.Count];
        for (int i = 0; i < draws.Length; i++)
        {
            var slot = combat.Enemies[i];
            draws[i] = action is { Attacker: ClassicBattleSide.Player, TargetIndex: var targeted } && targeted == i
                && playerPresentation is { } strike
                ? new(idleFrame, strike.EnemyPosition, strike.EnemyColorShift, strike.DisplayedEnemyHp)
                : action is { Attacker: ClassicBattleSide.Enemy, ActorIndex: var mover } && mover == i
                    && EnemyPresentation is { } attacker
                    ? new(attacker.EnemyFrame, attacker.EnemyPosition, 0, slot.Hp)
                    : new(idleFrame, team.EnemySlots[i].EnemyPosition, 0, slot.Hp);
        }
        return draws;
    }

    public override void _Draw()
    {
        if (combat is null) return;
        DrawTexture(background, Vector2.Zero);
        var shown = DrawSnapshot;
        if (shown.PartyEnemies.Count > 0)
        {
            foreach (var enemyDraw in shown.PartyEnemies) Sprite(false, enemyDraw.Frame, enemyDraw.Position, enemyDraw.ColorShift);
            foreach (var memberDraw in shown.PartyPlayers) Sprite(true, memberDraw.Frame, memberDraw.Position, memberDraw.ColorShift);
            // Demo selectors: palette caret under the selecting member and highlighted slot.
            if (mode is StoryBattlePresentation.AttackMenu or StoryBattlePresentation.TargetMenu
                && shown.SelectingPlayer is { } selecting)
                DrawRect(new Rect2(shown.PartyPlayers[selecting].Position - new Vector2(6, 3), new Vector2(12, 2)), story.Palette[79]);
            if (mode == StoryBattlePresentation.TargetMenu)
            {
                int highlighted = Math.Clamp(highlightedSlot, 0, shown.PartyEnemies.Count - 1);
                DrawRect(new Rect2(shown.PartyEnemies[highlighted].Position - new Vector2(6, 3), new Vector2(12, 2)), story.Palette[79]);
            }
        }
        else
        {
            Sprite(false, shown.EnemyFrame, shown.EnemyPosition, shown.EnemyColorShift); Sprite(true, shown.PlayerFrame, shown.PlayerPosition, shown.PlayerColorShift);
        }
        if (shown.EffectFrame is { } effect)
        {
            var texture = effects[effect];
            DrawTexture(texture, shown.EffectPosition - new Vector2(texture.GetWidth() / 2, texture.GetHeight()));
        }
        DrawRect(new Rect2(0, 174, 320, shown.PartyBoosts.Count > 0 ? 33 : 26), new Color(0, 0, 0, .9f));
        if (shown.PartyEnemies.Count > 0)
        {
            // Per-actor HP panel: members then enemy slots, slash-separated in slot order.
            Text(story.SelectedWords[HpWord] + ":" + string.Join("/", shown.PartyPlayers.Select(p => p.Hp))
                + "/" + string.Join("/", shown.PartyEnemies.Select(e => e.Hp)), new Vector2(4, 176));
        }
        else
            Text(story.SelectedWords[HpWord] + ":" + DisplayedPlayerHp + "/" + DisplayedEnemyHp, new Vector2(4, 176));
        if (shown.PartyBoosts.Count > 0)
            Text(story.SelectedWords[BoostWord] + ":" + string.Join("/", shown.PartyBoosts), new Vector2(4, 191));
        string label = mode == StoryBattlePresentation.AttackMenu
            ? shown.PartyEnemies.Count > 0 && shown.SelectingPlayer is { } member
                ? story.SelectedWords[AttackWord] + ":" + (member + 1) : story.SelectedWords[AttackWord]
            : mode == StoryBattlePresentation.TargetMenu
                ? shown.PartyEnemies.Count > 0 ? story.SelectedWords[TargetWord] + ":" + (Math.Clamp(highlightedSlot, 0, shown.PartyEnemies.Count - 1) + 1)
                    : story.SelectedWords[TargetWord] + ":" + team.TargetId
            : shown.FeedbackWord is { } word ? story.SelectedWords[word] : "";
        Text(label, new Vector2(155, 176));
        if (mode == StoryBattlePresentation.Settlement && SettlementLine is { } settlement)
        {
            // The reference draws the settlement boxes over the battle screen (battle.c 1019-1032).
            DrawRect(new Rect2(20, 62, 280, 28), new Color(0, 0, 0, .92f));
            Text(settlement, new Vector2(28, 71));
        }
        foreach (var digit in shown.NumberDigits) DrawTexture(numbers[(digit.Color, digit.Digit)], digit.Position);
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
    internal void ReleaseTextures() { foreach (var texture in owned) texture.Dispose(); owned.Clear(); frames.Clear(); effects.Clear(); numbers.Clear(); NumberDigits = Array.Empty<StoryNumberDigit>(); FeedbackWord = null; SettlementLine = null; fleePresentation = null; }
}

/// <summary>fight.c 4153-4171: the failed escaper's stepped frame/offset for one pose.</summary>
internal readonly record struct StoryPlayerFleeSnapshot(int PartyIndex, int Frame, Vector2 Offset);

internal readonly record struct StoryBattleDrawSnapshot(int PlayerFrame, int EnemyFrame, Vector2 PlayerPosition, Vector2 EnemyPosition, int PlayerColorShift,
    int EnemyColorShift, int? EffectFrame, Vector2 EffectPosition, int DisplayedPlayerHp, int DisplayedEnemyHp,
    bool ResultVisible, int EnemyIdleCounter)
{
    internal IReadOnlyList<StoryNumberDigit> NumberDigits { get; init; } = Array.Empty<StoryNumberDigit>();
    internal int? FeedbackWord { get; init; }
    // Party per-actor truth in party/slot order; empty keeps the legacy single-pair projection.
    internal IReadOnlyList<StoryActorDraw> PartyPlayers { get; init; } = Array.Empty<StoryActorDraw>();
    internal IReadOnlyList<StoryActorDraw> PartyEnemies { get; init; } = Array.Empty<StoryActorDraw>();
    // Per-member role-hook boost sums; empty while no boost register is set.
    internal IReadOnlyList<int> PartyBoosts { get; init; } = Array.Empty<int>();
    internal int? SelectingPlayer { get; init; }
}

/// <summary>One drawn battle actor: frame, bottom-center position, palette shift and displayed HP.</summary>
internal readonly record struct StoryActorDraw(int Frame, Vector2 Position, int ColorShift, int Hp);
