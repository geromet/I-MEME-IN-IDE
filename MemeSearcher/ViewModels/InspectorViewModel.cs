using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemeSearcher.Core.Interfaces;
using MemeSearcher.Core.Search;

namespace MemeSearcher.ViewModels;

/// <summary>
/// Backs the shared Inspector panel for single-source and composite results. Both modes reuse the
/// same phone timing primitive; composite sections project already-shipped component provenance
/// rather than creating a second alignment model. Waveform remains the later #35 slice.
/// </summary>
public partial class InspectorViewModel(IMediaPlayerLauncher playerLauncher) : ViewModelBase
{
    [ObservableProperty]
    private bool _hasSelection;

    [ObservableProperty]
    private bool _isSingleSelection;

    [ObservableProperty]
    private bool _isCompositeSelection;

    [ObservableProperty]
    private string _sourceTextDisplay = "";

    [ObservableProperty]
    private string _alignmentSummary = "";

    [ObservableProperty]
    private string? _mediaPath;

    [ObservableProperty]
    private string _seekStatus = "";

    [ObservableProperty]
    private bool _hasAlignmentSteps;

    public ObservableCollection<PhoneBlockViewModel> PhoneBlocks { get; } = [];
    public ObservableCollection<CompositeInspectorComponentViewModel> CompositeComponents { get; } = [];

    [ObservableProperty]
    private PhoneCoverageStripViewModel _coverageStrip = new([]);

    [ObservableProperty]
    private bool _hasExtraPhonemes;

    public ObservableCollection<string> ExtraPhonemes { get; } = [];

    public void Show(SearchResultRowViewModel? result)
    {
        ResetPresentation();

        if (result is null)
        {
            return;
        }

        HasSelection = true;
        IsSingleSelection = true;
        SourceTextDisplay = result.SourceText;
        MediaPath = result.MediaPath;

        foreach (var phone in result.MatchedPhoneDetails)
        {
            PhoneBlocks.Add(new PhoneBlockViewModel(phone));
        }

        CoverageStrip = result.CoverageStrip;
        HasAlignmentSteps = result.AlignmentSteps.Count > 0;

        foreach (var step in result.AlignmentSteps)
        {
            if (step.Op == AlignmentOp.CandidateExtra)
            {
                ExtraPhonemes.Add($"+{step.MatchSymbol}");
            }
        }

        HasExtraPhonemes = ExtraPhonemes.Count > 0;
        AlignmentSummary = SummarizeAlignment(PhoneBlocks, "match");
    }

    /// <summary>#35: show every component in assembly order using the provenance already present on the composite row.</summary>
    public void ShowComposite(CompositeSearchResultRowViewModel? result)
    {
        ResetPresentation();

        if (result is null)
        {
            return;
        }

        HasSelection = true;
        IsCompositeSelection = true;

        for (var index = 0; index < result.Components.Count; index++)
        {
            CompositeComponents.Add(new CompositeInspectorComponentViewModel(result.Components[index], index + 1));
        }
    }

    [RelayCommand]
    private async Task SeekAsync(PhoneBlockViewModel block)
    {
        if (MediaPath is null || block.StartSeconds is not { } start)
        {
            return;
        }

        var outcome = await playerLauncher.OpenAsync(MediaPath, start);
        SeekStatus = FormatSeekOutcome(outcome, block.Symbol, start, null);
    }

    [RelayCommand]
    private async Task SeekCompositeAsync(CompositeInspectorPhoneViewModel phone)
    {
        if (phone.MediaPath is null || phone.Block.StartSeconds is not { } start)
        {
            return;
        }

        var outcome = await playerLauncher.OpenAsync(phone.MediaPath, start);
        SeekStatus = FormatSeekOutcome(outcome, phone.Block.Symbol, start, phone.MediaTitle);
    }

    private void ResetPresentation()
    {
        HasSelection = false;
        IsSingleSelection = false;
        IsCompositeSelection = false;
        SourceTextDisplay = "";
        AlignmentSummary = "";
        MediaPath = null;
        SeekStatus = "";
        HasAlignmentSteps = false;
        HasExtraPhonemes = false;
        CoverageStrip = new PhoneCoverageStripViewModel([]);
        PhoneBlocks.Clear();
        ExtraPhonemes.Clear();
        CompositeComponents.Clear();
    }

    private static string SummarizeAlignment(System.Collections.Generic.IEnumerable<PhoneBlockViewModel> phones, string subject)
    {
        var list = phones.ToList();
        return list.Count == 0
            ? $"No phone timing available for this {subject}."
            : list.All(p => p.IsAligned)
                ? "Precisely aligned (real per-phone timing)."
                : list.Any(p => p.IsAligned)
                    ? "Partially aligned - some phones are estimated."
                    : "Estimated timing - no phone-level alignment has run for this source.";
    }

    private static string FormatSeekOutcome(MediaPlayerLaunchResult outcome, string symbol, double start, string? mediaTitle) => outcome switch
    {
        { Success: true, SeekedToTimestamp: true } when mediaTitle is not null => $"Seeked {mediaTitle} to \"{symbol}\" at {FormatTimestamp(start)}.",
        { Success: true, SeekedToTimestamp: true } => $"Seeked to \"{symbol}\" at {FormatTimestamp(start)}.",
        { Success: true, SeekedToTimestamp: false } => "Opened, but no seek-capable player (mpv/vlc) was found.",
        _ => $"Couldn't open media: {outcome.Error}",
    };

    private static string FormatTimestamp(double seconds) => TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss\.ff");
}
