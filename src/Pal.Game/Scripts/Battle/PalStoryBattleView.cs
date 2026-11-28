using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Pal.Core;
using Pal.Game.Profiles;
using Pal.Game.Resources;
using Pal.Core.Battle;

namespace Pal.Game.Battle;

public enum StoryBattlePresentation { AttackMenu, TargetMenu, Resolving, Feedback, HookDialogue, Settlement }

/// <summary>Indexed native art and authored WORD labels. Main alone advances combat and presentation.</summary>
public partial class PalStoryBattleView : Node2D
{
    // Modern demo WORD convention, not retail SDL IDs (SDL attack selection uses an icon).
    public const int AttackWord = 60000, TargetWord = 60001, MissWord = 60002, CriticalWord = 60003, HpWord = 60004, BoostWord = 60005;
    // #50 settlement labels (battle.c 1019-1032/1130/1314: exp/cash box, level-up, learned magic).
    public const int ExpWord = 60006, CashWord = 60007, LevelUpWord = 60008, LearnedMagicWord = 60009;
    // #51 failed escape label (fight.c 4168 BATTLE_LABEL_ESCAPEFAIL); authored WORD, not retail SDL id 31.
    public const int EscapeFailWord = 60010;
    private CheckedStoryProfile story = null!;
    private CheckedBattleVisualProfile visual = null!;
    private CheckedBattleVisualTeam team = null!;
    private readonly List<ImageTexture> owned = new();
    // Instance-local observation runs only after this view owns the native texture.
    public Action<int?, ImageTexture>? TextureOwned { get; init; }
    public int TextureReferenceCount => owned.Count + frames.Count + effects.Count + numbers.Count;
    private readonly Dictionary<(bool Player, int Slot, int Frame, int Shift), ImageTexture> frames = new();
    private ImageTexture background = null!;
    private ClassicBattleView combat = null!;
    private StoryBattlePresentation mode;
    private int highlightedSlot;
    private StoryPlayerAttackSnapshot? playerPresentation;
    private StoryPlayerFleeSnapshot? fleePresentation;
    private IReadOnlyList<StoryPlayerFleeSnapshot>? escapePresentation;
    private int[] idleFrames = [];
    private int[] idleCounters = [];
    private readonly Dictionary<(int Member, int Frame), ImageTexture> effects = new();
    private readonly Dictionary<(StoryNumberColor Color, int Digit), ImageTexture> numbers = new();
    public StoryEnemyAttackSnapshot? EnemyPresentation { get; private set; }
    public int DisplayedPlayerHp => DrawSnapshot.DisplayedPlayerHp;
    public int DisplayedEnemyHp => DrawSnapshot.DisplayedEnemyHp;
    public bool ResultVisible => DrawSnapshot.ResultVisible;

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
        // Each member owns a texture set keyed by party position; shared sprites stay distinct bindings.
        for (int member = 0; member < visual.Players.Count; member++)
            foreach (var frame in visual.Players[member].PlayerSprite.Frames)
            {
                frames.Add((true, member, frame.Index, 0), Texture(frame.Width, frame.Height, frame.Indices, frame.Alpha));
                if (frame.Index == 4)
                    frames.Add((true, member, frame.Index, 6), Texture(frame.Width, frame.Height, frame.Indices, frame.Alpha, 6));
            }
        for (int slot = 0; slot < team.EnemySlots.Count; slot++)
        {
            // #141: a numeric-empty runtime slot owns no sprite, frames or textures; it keeps its
            // slot position/identity for the party-order arrays and never draws.
            if (team.EnemySlots[slot].IsEmpty) continue;
            foreach (var frame in team.EnemySlots[slot].EnemySprite!.Frames)
            {
                frames.Add((false, slot, frame.Index, 0), Texture(frame.Width, frame.Height, frame.Indices, frame.Alpha, enemySlot: slot));
                frames.Add((false, slot, frame.Index, 6), Texture(frame.Width, frame.Height, frame.Indices, frame.Alpha, 6, enemySlot: slot));
            }
        }
        // Per-role effect frames: the acting member's own resolved table group (fight.c:2029).
        for (int member = 0; member < visual.Players.Count; member++)
            for (int i = 0; i < visual.Players[member].AttackEffectFrames.Count; i++)
            {
                var frame = visual.Players[member].AttackEffectFrames[i];
                effects.Add((member, i), Texture(frame.Width, frame.Height, frame.Indices, frame.Alpha));
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
    private ImageTexture Texture(int width, int height, byte[] indices, byte[] alpha, int shift = 0, int? enemySlot = null)
    {
        var bytes = new byte[indices.Length * 4];
        for (int i = 0; i < indices.Length; i++)
        {
            Color c = story.Palette[StoryEnemyAttackPresentation.ShiftIndex(indices[i], shift)];
            bytes[i * 4] = (byte)Mathf.RoundToInt(c.R * 255); bytes[i * 4 + 1] = (byte)Mathf.RoundToInt(c.G * 255);
            bytes[i * 4 + 2] = (byte)Mathf.RoundToInt(c.B * 255); bytes[i * 4 + 3] = alpha[i];
        }
        using var image = Image.CreateFromData(width, height, false, Image.Format.Rgba8, bytes);
        var result = ImageTexture.CreateFromImage(image);
        owned.Add(result);
        TextureOwned?.Invoke(enemySlot, result);
        return result;
    }
    internal void Present(ClassicBattleView combat, StoryBattlePresentation mode, IReadOnlyList<StoryEnemyIdleState> enemyIdle,
        StoryEnemyAttackSnapshot? enemyPresentation = null, StoryPlayerAttackSnapshot? playerPresentation = null,
        IReadOnlyList<StoryNumberDigit>? numberDigits = null, int? feedbackWord = null, int highlightedEnemySlot = 0,
        string? settlementLine = null, StoryPlayerFleeSnapshot? fleePresentation = null,
        IReadOnlyList<StoryPlayerFleeSnapshot>? escapePresentation = null)
    {
        this.combat = combat; this.mode = mode;
        idleFrames = enemyIdle.Select(idle => idle.Frame).ToArray();
        idleCounters = enemyIdle.Select(idle => (int)idle.Counter).ToArray();
        highlightedSlot = highlightedEnemySlot;
        EnemyPresentation = enemyPresentation; this.playerPresentation = playerPresentation;
        this.fleePresentation = fleePresentation;
        this.escapePresentation = escapePresentation;
        NumberDigits = numberDigits ?? Array.Empty<StoryNumberDigit>();
        FeedbackWord = feedbackWord;
        SettlementLine = settlementLine;
        BuildDrawSnapshot(); QueueRedraw();
    }

    public IReadOnlyList<StoryNumberDigit> NumberDigits { get; private set; } = Array.Empty<StoryNumberDigit>();
    public int? FeedbackWord { get; private set; }
    /// <summary>The current victory-settlement line (authored WORD labels plus numbers); null outside Settlement.</summary>
    public string? SettlementLine { get; private set; }
    public StoryBattleDrawSnapshot DrawSnapshot { get; private set; }
    private void BuildDrawSnapshot()
    {
        // Either side can have multiple actors independently; scalar fields project actor zero.
        bool party = combat.Players.Count > 1 || combat.Enemies.Count > 1;
        var action = combat.LastAction;
        var partyPlayers = party ? PlayerDraws(action) : Array.Empty<StoryActorDraw>();
        var partyEnemies = party ? EnemyDraws(action) : Array.Empty<StoryActorDraw>();
        Vector2 player = visual.Players[0].PlayerPosition, enemy = team.EnemySlots[0].EnemyPosition, fxPosition = Vector2.Zero;
        int pf = StoryEnemyAttackPresentation.RestFrame(combat.PlayerHp, story.BattleContent.Players[0].MaxHp), ef = idleFrames[0];
        // fight.c 977-980 (UpdateFighters): a defending member holds the defend frame 3 for the
        // rest of its round; an active presentation below still overrides it.
        if (pf == 0 && combat.Players[0].IsDefending) pf = 3;
        int playerShift = 0, enemyShift = 0, php = combat.PlayerHp, ehp = combat.EnemyHp;
        int? fx = null;
        bool result = false;
        IReadOnlyList<StoryStruckEnemy>? struckEnemies = null;
        if (fleePresentation is { } fleeing)
        {
            // fight.c 4153-4171: the failed escaper steps +4/+2 three times, then stands at
            // frame 1 under the escape-fail label; no strike, effect or result frame shows.
            pf = fleeing.Frame;
            player = visual.Players[0].PlayerPosition + fleeing.Offset;
        }
        if (escapePresentation is { } walking && walking.Count > 0)
        {
            // battle.c 1458-1528: the successful escape walks every living member off-screen;
            // the single-pair projection follows the sole member (multi-member draws below).
            pf = walking[0].Frame;
            player = visual.Players[0].PlayerPosition + walking[0].Offset;
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
            struckEnemies = hit.StruckEnemies;
        }
        if (party)
        {
            php = partyPlayers[0].Hp; ehp = partyEnemies[0].Hp;
            pf = partyPlayers[0].Frame; player = partyPlayers[0].Position; playerShift = partyPlayers[0].ColorShift;
            ef = partyEnemies[0].Frame; enemy = partyEnemies[0].Position; enemyShift = partyEnemies[0].ColorShift;
        }
        DrawSnapshot = new(pf, ef, player, enemy, playerShift, enemyShift, fx, fxPosition, php, ehp, result, idleCounters[0])
        { EnemyIdleFrames = Array.AsReadOnly(idleFrames), EnemyIdleCounters = Array.AsReadOnly(idleCounters), NumberDigits = NumberDigits, FeedbackWord = FeedbackWord, PartyPlayers = partyPlayers, PartyEnemies = partyEnemies,
            SelectingPlayer = party ? combat.SelectingPlayerIndex : null,
            EnemyVisible = IsEnemyVisible(0),
            PartyBoosts = BoostSums(combat), StruckEnemies = struckEnemies };
    }

    private StoryPlayerFleeSnapshot? EscapeWalk(int partyIndex)
    {
        if (escapePresentation is null) return null;
        foreach (var walking in escapePresentation)
            if (walking.PartyIndex == partyIndex) return walking;
        return null;
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
            else if (EscapeWalk(i) is { } walking)
                // battle.c 1493-1516: each living member walks off at its own party-column step;
                // the dead stay visible at their home positions through the walk-off and leave
                // the screen with the party at the final removal frame (battle.c 1519-1526).
                draws[i] = new(walking.Frame, origins[i] + walking.Offset, 0, member.Hp);
            else if (action is { Attacker: ClassicBattleSide.Enemy, TargetIndex: var target, CovererPartyIndex: null }
                && target == i && EnemyPresentation is { } incoming)
                draws[i] = new(incoming.PlayerFrame, incoming.PlayerPosition, incoming.PlayerColorShift, incoming.DisplayedPlayerHp);
            else
            {
                // fight.c 977-980: the defend frame 3 replaces only the plain rest gesture —
                // dying/dead members keep their own frames and an acting member keeps its pose.
                int frame = StoryEnemyAttackPresentation.RestFrame(member.Hp, story.BattleContent.Players[i].MaxHp);
                if (frame == 0 && member.IsDefending) frame = 3;
                draws[i] = action is { Attacker: ClassicBattleSide.Player, ActorIndex: var actor } && actor == i
                    && playerPresentation is { } acting
                    ? new(acting.PlayerFrame, acting.PlayerPosition, 0, member.Hp)
                    : new(frame, origins[i], 0, member.Hp);
            }
        }
        return draws;
    }

    private StoryActorDraw[] EnemyDraws(ClassicBattleAction? action)
    {
        var draws = new StoryActorDraw[combat.Enemies.Count];
        for (int i = 0; i < draws.Length; i++)
        {
            var slot = combat.Enemies[i];
            // fight.c 2173-2256: an all-target strike moves, shifts and reveals every struck
            // enemy together; the un-struck slots keep their idle gesture.
            if (playerPresentation is { StruckEnemies: { } struck } && struck.Any(x => x.Slot == i))
            {
                var struckSlot = struck.Single(x => x.Slot == i);
                draws[i] = new(idleFrames[i], struckSlot.Position, struckSlot.ColorShift, struckSlot.DisplayedHp);
            }
            else if (action is { Attacker: ClassicBattleSide.Player, TargetIndex: var targeted } && targeted == i
                && playerPresentation is { } strike)
                draws[i] = new(idleFrames[i], strike.EnemyPosition, strike.EnemyColorShift, strike.DisplayedEnemyHp);
            else if (action is { Attacker: ClassicBattleSide.Enemy, ActorIndex: var mover } && mover == i
                && EnemyPresentation is { } attacker)
                draws[i] = new(attacker.EnemyFrame, attacker.EnemyPosition, 0, slot.Hp);
            else
                draws[i] = new(idleFrames[i], team.EnemySlots[i].EnemyPosition, 0, slot.Hp);
            draws[i] = draws[i] with { Visible = IsEnemyVisible(i) };
        }
        return draws;
    }

    // fight.c post-action check removes the KO'ed instance, not every slot sharing its ID.
    // Core resolves damage before feedback; keep every struck target visible until its animation
    // ends — an all-target strike strikes several slots at once (fight.c 2173-2190).
    private bool IsEnemyVisible(int slot) => combat.Enemies[slot].Hp > 0
        || (mode == StoryBattlePresentation.Feedback && playerPresentation is not null
            && combat.LastActionTargets.Any(record => record.Attacker == ClassicBattleSide.Player && record.TargetIndex == slot));

    public override void _Draw()
    {
        if (combat is null) return;
        DrawTexture(background, Vector2.Zero);
        var shown = DrawSnapshot;
        if (shown.PartyEnemies.Count > 0)
        {
            for (int slot = 0; slot < shown.PartyEnemies.Count; slot++)
            {
                var enemyDraw = shown.PartyEnemies[slot];
                if (enemyDraw.Visible) Sprite(false, enemyDraw.Frame, enemyDraw.Position, enemyDraw.ColorShift, slot);
            }
            for (int member = 0; member < shown.PartyPlayers.Count; member++)
            {
                var memberDraw = shown.PartyPlayers[member];
                Sprite(true, memberDraw.Frame, memberDraw.Position, memberDraw.ColorShift, member);
            }
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
            if (shown.EnemyVisible) Sprite(false, shown.EnemyFrame, shown.EnemyPosition, shown.EnemyColorShift);
            Sprite(true, shown.PlayerFrame, shown.PlayerPosition, shown.PlayerColorShift);
        }
        if (shown.EffectFrame is { } effect)
        {
            // fight.c:2029: the acting member's own battle sprite resolves the effect group.
            int actingMember = combat.LastAction is { Attacker: ClassicBattleSide.Player, ActorIndex: var actor } ? actor : 0;
            var texture = effects[(actingMember, effect)];
            if (shown.StruckEnemies is { } multi)
            {
                // fight.c 2173-2190: the same FX frame blits at every struck enemy's own position.
                foreach (var struck in multi)
                    DrawTexture(texture, struck.EffectPosition - new Vector2(texture.GetWidth() / 2, texture.GetHeight()));
            }
            else
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
            ? shown.PartyEnemies.Count > 0 && shown.SelectingPlayer is { } picker
                ? story.SelectedWords[AttackWord] + ":" + (picker + 1) : story.SelectedWords[AttackWord]
            : mode == StoryBattlePresentation.TargetMenu
                ? shown.PartyEnemies.Count > 0 ? story.SelectedWords[TargetWord] + ":" + (Math.Clamp(highlightedSlot, 0, shown.PartyEnemies.Count - 1) + 1)
                    : story.SelectedWords[TargetWord] + ":" + team.EnemySlots[0].TargetId
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
    private void Sprite(bool player, int frame, Vector2 bottomCenter, int shift = 0, int slot = 0)
    {
        var texture = frames[(player, slot, frame, shift)];
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
    public void ReleaseTextures() { foreach (var texture in owned) texture.Dispose(); owned.Clear(); frames.Clear(); effects.Clear(); numbers.Clear(); NumberDigits = Array.Empty<StoryNumberDigit>(); FeedbackWord = null; SettlementLine = null; fleePresentation = null; escapePresentation = null; }
}

/// <summary>fight.c 4153-4171: the failed escaper's stepped frame/offset for one pose.</summary>
public readonly record struct StoryPlayerFleeSnapshot(int PartyIndex, int Frame, Vector2 Offset);

public readonly record struct StoryBattleDrawSnapshot(int PlayerFrame, int EnemyFrame, Vector2 PlayerPosition, Vector2 EnemyPosition, int PlayerColorShift,
    int EnemyColorShift, int? EffectFrame, Vector2 EffectPosition, int DisplayedPlayerHp, int DisplayedEnemyHp,
    bool ResultVisible, int EnemyIdleCounter)
{
    public bool EnemyVisible { get; init; }
    public IReadOnlyList<int> EnemyIdleFrames { get; init; } = Array.Empty<int>();
    public IReadOnlyList<int> EnemyIdleCounters { get; init; } = Array.Empty<int>();
    public IReadOnlyList<StoryNumberDigit> NumberDigits { get; init; } = Array.Empty<StoryNumberDigit>();
    public int? FeedbackWord { get; init; }
    // Party per-actor truth in party/slot order; empty keeps the legacy single-pair projection.
    public IReadOnlyList<StoryActorDraw> PartyPlayers { get; init; } = Array.Empty<StoryActorDraw>();
    public IReadOnlyList<StoryActorDraw> PartyEnemies { get; init; } = Array.Empty<StoryActorDraw>();
    // Per-struck-enemy state of one all-target strike; null keeps the single-target projection.
    public IReadOnlyList<StoryStruckEnemy>? StruckEnemies { get; init; }
    // Per-member role-hook boost sums; empty while no boost register is set.
    public IReadOnlyList<int> PartyBoosts { get; init; } = Array.Empty<int>();
    public int? SelectingPlayer { get; init; }
}

/// <summary>One drawn battle actor: frame, bottom-center position, palette shift and displayed HP.</summary>
public readonly record struct StoryActorDraw(int Frame, Vector2 Position, int ColorShift, int Hp)
{
    public bool Visible { get; init; } = true;
}
