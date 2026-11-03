import io
# 1. PalTriggerDialogue: overload initializing from the verified first-battle resources.
p='src/Pal.Game/Scripts/Dialogue/PalTriggerDialogue.cs'
s=io.open(p,encoding='utf-8').read()
old='''    private CheckedStoryProfile? _story;

    public void Initialize(PalDialogueDisplay display, CheckedStoryProfile story, IBattleRandom random)'''
new='''    private CheckedStoryProfile? _story;

    /// <summary>#53 first-battle host: the same production dialogue/battle executor over the
    /// strict-loaded resources the verified closure bound (default language selection).</summary>
    public void Initialize(PalDialogueDisplay display, FirstBattleRealResources resources, string language, IBattleRandom random)
    {
        ArgumentNullException.ThrowIfNull(display); ArgumentNullException.ThrowIfNull(resources); ArgumentNullException.ThrowIfNull(random);
        if (language is not ("zh-Hans" or "en")) throw new InvalidDataException($"Unsupported dialogue language: {language}");
        if (_display is not null || display.Session is { Current.Phase: not DialogueDisplayPhase.Completed })
            throw new InvalidOperationException("Trigger dialogue or display is already in use.");
        _events = resources.Events;
        _language = language == "zh-Hans" ? resources.Chinese : resources.English;
        _font = resources.Font;
        _prompts = resources.Prompts.ToArray();
        _palette = PaletteFromScene is { } palette ? palette : throw new InvalidOperationException("The first-battle host must supply the active scene palette first.");
        foreach (var pair in resources.Portraits) _portraits.Add(pair.Key, pair.Value);
        Session = new TriggerScriptSession(resources.Events, _language, resources.BattleContent, random);
        _display = display; display.UpdateCompleted += OnDisplayUpdated;
    }

    /// <summary>The active 256-color scene palette, set by the host before initialization.</summary>
    public Color[]? PaletteFromScene { private get; set; }

    public void Initialize(PalDialogueDisplay display, CheckedStoryProfile story, IBattleRandom random)'''
assert old in s; s=s.replace(old,new)
io.open(p,'w',encoding='utf-8').write(s)
print('dialogue ok')
