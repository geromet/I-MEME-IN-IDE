using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemeSearcher.Core.Interfaces;

namespace MemeSearcher.ViewModels;

/// <summary>
/// Backs the shell's Inspector panel (#15, Milestone 12's placeholder): the aligned phone timeline,
/// the query-to-match correspondence, and click-to-seek for a selected search result. Waveform
/// rendering is deliberately out of scope here - see the issue's own "treat as stretch goal" note
/// and the follow-up filed for it.
///
/// Owned by MainWindowViewModel and driven by whichever SearchViewModel is the active tab
/// (SearchResultRowViewModel.SelectedResult); MainWindowViewModel calls Show() whenever the active
/// tab or its selection changes.
/// </summary>
public partial class InspectorViewModel(IMediaPlayerLauncher playerLauncher) : ViewModelBase
{
    [ObservableProperty]
    private bool _hasSelection;

    [ObservableProperty]
    private string _sourceTextDisplay = "";

    /// <summary>Human summary of whether every, some, or none of this match's phones came from real per-phone alignment (handoff §49's predicted-vs-actual distinction, made visible).</summary>
    [ObservableProperty]
    private string _alignmentSummary = "";

    [ObservableProperty]
    private string? _mediaPath;

    [ObservableProperty]
    private string _seekStatus = "";

    [ObservableProperty]
    private bool _hasAlignmentSteps;

    public ObservableCollection<PhoneBlockViewModel> PhoneBlocks { get; } = [];

    public ObservableCollection<AlignmentStepDisplayViewModel> AlignmentSteps { get; } = [];

    public void Show(SearchResultRowViewModel? result)
    {
        PhoneBlocks.Clear();
        AlignmentSteps.Clear();
        SeekStatus = "";

        if (result is null)
        {
            HasSelection = false;
            SourceTextDisplay = "";
            AlignmentSummary = "";
            MediaPath = null;
            HasAlignmentSteps = false;
            return;
        }

        HasSelection = true;
        SourceTextDisplay = result.SourceText;
        MediaPath = result.MediaPath;

        foreach (var phone in result.MatchedPhoneDetails)
        {
            PhoneBlocks.Add(new PhoneBlockViewModel(phone));
        }

        foreach (var step in result.AlignmentSteps)
        {
            AlignmentSteps.Add(new AlignmentStepDisplayViewModel(step));
        }

        HasAlignmentSteps = AlignmentSteps.Count > 0;

        AlignmentSummary = PhoneBlocks.Count == 0
            ? "No phone timing available for this match."
            : PhoneBlocks.All(p => p.IsAligned)
                ? "Precisely aligned (real per-phone timing)."
                : PhoneBlocks.Any(p => p.IsAligned)
                    ? "Partially aligned - some phones are estimated."
                    : "Estimated timing - no phone-level alignment has run for this source.";
    }

    [RelayCommand]
    private async Task SeekAsync(PhoneBlockViewModel block)
    {
        if (MediaPath is null || block.StartSeconds is not { } start)
        {
            return;
        }

        var outcome = await playerLauncher.OpenAsync(MediaPath, start);

        SeekStatus = outcome switch
        {
            { Success: true, SeekedToTimestamp: true } => $"Seeked to \"{block.Symbol}\" at {FormatTimestamp(start)}.",
            { Success: true, SeekedToTimestamp: false } => "Opened, but no seek-capable player (mpv/vlc) was found.",
            _ => $"Couldn't open media: {outcome.Error}",
        };
    }

    private static string FormatTimestamp(double seconds) => TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss\.ff");
}
