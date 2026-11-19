using System.Globalization;
using System.Windows.Input;
using OrthoStreamXP.Setup;
using OrthoStreamXP.Textures;
using OrthoStreamXP.Tuning;

namespace OrthoStreamXP.Gui;

/// <summary>A quality preset as a card on the Settings page and in the setup.</summary>
/// <param name="saveResolution">Keeps the card's resolution when it is changed; null where the card offers none.</param>
/// <param name="chooseBase">
/// Dynamic's card: its base (Extra, Ultra or Sharp) was changed, and the card is now Dynamic from it; null where the
/// card offers no base.
/// </param>
internal sealed class PresetOption(QualityPreset preset, Action<QualityPreset> choose, Action<QualityPreset, TextureResolution>? saveResolution = null,
    Action<QualityPreset>? chooseBase = null)
    : ObservableObject
{
    private bool _isSelected;
    private TextureResolution _resolution = TextureResolution.Full;
    private QualityPreset _preset = preset;

    /// <summary>The preset the card stands for: on Dynamic's card, Dynamic from the base chosen on it.</summary>
    public QualityPreset Preset => _preset;
    public string Title => Preset.Name;
    public string Summary => Preset.Summary;
    /// <summary>The preset the evidence recommends for this PC (<see cref="Tuning.PresetAdvisor"/>).</summary>
    public bool IsRecommended => Preset == PresetMemory.Recommended.Preset;

    /// <summary>The badge: "Recommended", with the resolution when it is not Full ("Recommended at Medium").</summary>
    public string? RecommendedBadge => !IsRecommended ? null
        : PresetMemory.Recommended.Resolution.IsFull ? "Recommended" : $"Recommended at {PresetMemory.Recommended.Resolution.Label}";

    /// <summary>Why it is recommended, under its badge.</summary>
    public string? RecommendedReason => IsRecommended ? PresetMemory.Recommended.Reason : null;

    /// <summary>On the other cards: that it also suits this PC, and at which resolution, or why it does not.</summary>
    public string? FitText => IsRecommended ? null : PresetMemory.FitOf(Preset)?.Text;

    /// <summary>It suits this PC (its line in the accent colour; otherwise in the caution colour).</summary>
    public bool Suits => PresetMemory.FitOf(Preset)?.Suits == true;

    public string Details => Preset is { DynamicBase: { } near, DynamicSteps: [var first, ..] steps }
        ? string.Create(CultureInfo.CurrentCulture,
            $"Below {first.AboveFt:N0} ft as {near.Name}: zoom {Preset.Zl16Zoom} in most areas, {Preset.Zl18Zoom} near airports · higher up as {string.Join(", then ", steps.Select(t => t.Name))} · sharp again before a descent reaches the ground · a new area loads in {Preset.FirstVisit}")
        : string.Create(CultureInfo.CurrentCulture,
            $"Zoom {Preset.Zl16Zoom} in most areas, {Preset.Zl18Zoom} near airports · about {Preset.MetresPerPixelAt(Resolution):0.#} m per pixel · a new area loads in {Preset.FirstVisit}");

    public string BestFor => $"Best for: {Preset.BestFor}";

    /// <summary>The bases Dynamic's card offers: Extra, Ultra and Sharp; none on the other cards.</summary>
    public IReadOnlyList<QualityPreset> Bases { get; } = preset.IsDynamic ? [.. QualityPreset.DynamicChoices.Select(p => p.DynamicBase!)] : [];

    /// <summary>The card offers a base (Dynamic's, on the Settings page and in the setup).</summary>
    public bool OffersBase => chooseBase is not null && Bases.Count > 0;

    /// <summary>Dynamic's base: the preset it is near the ground. Changing it makes the card Dynamic from that base.</summary>
    public QualityPreset? Base
    {
        get => Preset.DynamicBase;
        set
        {
            if (value is null || value == Preset.DynamicBase || QualityPreset.DynamicOn(value) is not { } dynamic) return;
            _preset = dynamic;
            RaisePreset();
            chooseBase?.Invoke(dynamic);
        }
    }

    /// <summary>Shows Dynamic from the base the settings have, without choosing it (when loading).</summary>
    public void ShowBase(QualityPreset dynamic)
    {
        if (!Preset.IsDynamic || !dynamic.IsDynamic || dynamic == Preset) return;
        _preset = dynamic;
        RaisePreset();
    }

    /// <summary>The resolutions offered beside the preset.</summary>
    public IReadOnlyList<TextureResolution> Resolutions { get; } = TextureResolution.All;

    /// <summary>The card offers a resolution (the Settings page and the setup).</summary>
    public bool OffersResolution => saveResolution is not null;

    /// <summary>How much this preset's textures are shrunk: kept for each preset, used while the settings are at it.</summary>
    public TextureResolution Resolution
    {
        get => _resolution;
        set
        {
            if (value is null || !Set(ref _resolution, value)) return;
            saveResolution?.Invoke(Preset, value);
            RaiseResolution();
        }
    }

    /// <summary>The texture sizes a shrunk resolution gives, under the summary's full-resolution ones; null at Full.</summary>
    public string? ResolutionText
    {
        get
        {
            if (Resolution.IsFull) return null;
            TexturePlan plan = Preset.PlanAt(Resolution);
            return string.Create(CultureInfo.CurrentCulture,
                $"At {Resolution.Label} resolution: {plan.SizeFor(16)} px textures in most areas, {plan.SizeFor(18)} px near airports, from the same imagery.");
        }
    }

    /// <summary>The photo textures' graphics memory at this preset, as flown on this PC (or on the test flight).</summary>
    public string MemoryText
    {
        get
        {
            long bytes = PresetMemory.Estimate.BytesAt(Preset.PlanAt(Resolution));
            string source = PresetMemory.Estimate == Tuning.GraphicsMemory.TestFlight ? "on a test flight" : "on your last long flight";
            return $"Photo textures take about {CacheInventory.FormatBytes(bytes)} of graphics memory at the most ({source}).";
        }
    }

    /// <summary>
    /// A warning when the photo textures and X-Plane's own needs (about 5 GB) would be more than the graphics card has.
    /// </summary>
    public string? MemoryWarning
    {
        get
        {
            if (PresetMemory.Card is not { } card) return null;
            long bytes = PresetMemory.Estimate.BytesAt(Preset.PlanAt(Resolution));
            return bytes + Tuning.GraphicsMemory.XPlaneOwnBytes <= card ? null
                : $"Your graphics card has {CacheInventory.FormatBytes(card)}. With X-Plane's own needs (about 5 GB), {Preset.Name} may run it short: "
                  + "X-Plane then stutters, or lowers the detail of its textures. A smaller preset or a lower resolution is safer.";
        }
    }

    /// <summary>The figures read again (a flight measured them since).</summary>
    public void RefreshMemory()
    {
        Raise(nameof(MemoryText));
        Raise(nameof(MemoryWarning));
        Raise(nameof(IsRecommended));
        Raise(nameof(RecommendedBadge));
        Raise(nameof(RecommendedReason));
        Raise(nameof(FitText));
        Raise(nameof(Suits));
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (Set(ref _isSelected, value) && value) choose(Preset);
        }
    }

    /// <summary>Marks it chosen without choosing it again (when loading).</summary>
    public void Show(bool selected)
    {
        _isSelected = selected;
        Raise(nameof(IsSelected));
    }

    /// <summary>Shows the resolution the settings have, without saving it again (when loading).</summary>
    public void ShowResolution(TextureResolution resolution)
    {
        _resolution = resolution;
        Raise(nameof(Resolution));
        RaiseResolution();
    }

    private void RaisePreset()
    {
        Raise(nameof(Preset));
        Raise(nameof(Base));
        Raise(nameof(Summary));
        Raise(nameof(BestFor));
        RaiseResolution();
        RefreshMemory();
    }

    private void RaiseResolution()
    {
        Raise(nameof(ResolutionText));
        Raise(nameof(Details));
        Raise(nameof(MemoryText));
        Raise(nameof(MemoryWarning));
    }
}
