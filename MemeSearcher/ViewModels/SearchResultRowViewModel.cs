using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemeSearcher.Core.Interfaces;
using MemeSearcher.Core.Search;
using MemeSearcher.Infrastructure.Ffmpeg;
using MemeSearcher.Services;

namespace MemeSearcher.ViewModels;

/// <summary>
/// Display-formatted projection of a Core SearchResult (handoff §45: raw distance is never the
/// user-facing number) plus result interaction (handoff §21): play/seek, copy, and (Milestone 5)
/// clip export actions.
/// </summary>
public partial class SearchResultRowViewModel : ObservableObject
{
    private readonly IMediaPlayerLauncher _playerLauncher;
    private readonly IClipboardService _clipboard;
    private readonly FFmpegClipExtractor _clipExtractor;
    private readonly IFilePickerService _filePicker;

    public Guid MediaId { get; }

    /// <summary>Null when this result's transcript carried no timing at all (#32).</summary>
    public double? StartSeconds { get; }

    public double? EndSeconds { get; }

    /// <summary>
    /// Whether this result can be located in time. Gates play, clip export and copy-timestamp -
    /// all three previously operated on a stand-in zero and silently did the wrong thing.
    /// </summary>
    public bool HasTiming => StartSeconds is not null && EndSeconds is not null;

    /// <summary>#25: raw score, alongside ScoreDisplay's formatted text - needed so the results list can sort by score as one of two axes rather than only ever trusting the server's default order.</summary>
    public double Score { get; }

    public string ScoreDisplay { get; }
    public string TimeRangeDisplay { get; }
    public string SourceText { get; }
    public string Ipa { get; }
    public string PhonemesDisplay { get; }

    /// <summary>Milestone 15 (#15): per-phone timing/provenance for the inspector's phone timeline.</summary>
    public IReadOnlyList<MatchedPhone> MatchedPhoneDetails { get; }

    /// <summary>Milestone 15 (#15): the query-to-match alignment, for the inspector's correspondence display.</summary>
    public IReadOnlyList<QueryAlignmentStep> AlignmentSteps { get; }

    /// <summary>
    /// #25: one cell per query phoneme, shared between this row's compact strip and the Inspector's
    /// larger one - built once here so both render identical coverage for the same result.
    /// </summary>
    public PhoneCoverageStripViewModel CoverageStrip { get; }

    /// <summary>
    /// #25: the fraction of the query this match genuinely covers - positions actually aligned
    /// (Match or Substitute), not the width of [QueryStart, QueryEnd), since a covered span can
    /// contain interior gaps (QueryExtra) that this fraction is meant to count against, not include.
    /// A separate axis from Score: "matched a little, well" and "matched a lot, roughly" can carry
    /// similar scores while covering very different amounts of the query.
    /// </summary>
    public double CoverageFraction { get; }

    public string CoverageDisplay => $"{CoverageFraction:P0} covered";

    // Resolved after construction (batched across all results by SearchViewModel), so the
    // Play/Export buttons' enabled state has to react to it arriving.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportClipCommand))]
    private string? _mediaPath;

    [ObservableProperty]
    private string _playbackStatus = "";

    public SearchResultRowViewModel(
        SearchResult result,
        IMediaPlayerLauncher playerLauncher,
        IClipboardService clipboard,
        FFmpegClipExtractor clipExtractor,
        IFilePickerService filePicker)
    {
        _playerLauncher = playerLauncher;
        _clipboard = clipboard;
        _clipExtractor = clipExtractor;
        _filePicker = filePicker;

        MediaId = result.MediaId;
        StartSeconds = result.StartSeconds;
        EndSeconds = result.EndSeconds;
        Score = result.Score;
        ScoreDisplay = $"{result.Score:P0}";
        // Say "no timing" rather than printing 00:00 - a result from an untimed transcript is not
        // a result at the start of the file, and rendering them identically is the bug (#32).
        TimeRangeDisplay = result.StartSeconds is { } start && result.EndSeconds is { } end
            ? $"{FormatTimestamp(start)} - {FormatTimestamp(end)}"
            : "no timing";
        SourceText = result.SourceText;
        Ipa = result.Ipa;
        PhonemesDisplay = string.Join(' ', result.Phonemes);
        MatchedPhoneDetails = result.MatchedPhoneDetails;
        AlignmentSteps = result.AlignmentSteps;

        var coverageCells = PhoneCoverageStripBuilder.Build(result.QueryPhonemes, result.AlignmentSteps, result.QueryStart, result.QueryEnd);
        CoverageStrip = new PhoneCoverageStripViewModel(coverageCells);
        CoverageFraction = result.QueryPhonemes.Count > 0
            ? coverageCells.Count(c => c.IsMatch || c.IsSubstitute) / (double)result.QueryPhonemes.Count
            : 0;
    }

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private async Task PlayAsync()
    {
        if (MediaPath is null)
        {
            return;
        }

        var result = await _playerLauncher.OpenAsync(MediaPath, StartSeconds!.Value);

        PlaybackStatus = result switch
        {
            { Success: true, SeekedToTimestamp: true } => "Opened at timestamp.",
            { Success: true, SeekedToTimestamp: false } =>
                "Opened, but no seek-capable player (mpv/vlc) was found - jump to the timestamp manually.",
            _ => $"Couldn't open media: {result.Error}",
        };
    }

    private bool CanPlay() => MediaPath is not null && HasTiming;

    [RelayCommand(CanExecute = nameof(CanExportClip))]
    private async Task ExportClipAsync()
    {
        if (MediaPath is null)
        {
            return;
        }

        var extension = Path.GetExtension(MediaPath) is { Length: > 0 } ext ? ext : ".mp4";
        var suggestedName = $"{SourceText.Replace(' ', '_')}{extension}";

        var outputPath = await _filePicker.PickClipExportPathAsync(suggestedName);
        if (outputPath is null)
        {
            return;
        }

        PlaybackStatus = "Exporting clip...";
        var result = await _clipExtractor.ExtractAsync(MediaPath, StartSeconds!.Value, EndSeconds!.Value, outputPath);

        PlaybackStatus = result.Success
            ? $"Exported to {Path.GetFileName(outputPath)}."
            : $"Export failed: {result.Error}";
    }

    private bool CanExportClip() => MediaPath is not null && HasTiming;

    [RelayCommand(CanExecute = nameof(HasTiming))]
    private Task CopyTimestampAsync() => _clipboard.SetTextAsync(FormatTimestamp(StartSeconds!.Value));

    [RelayCommand]
    private Task CopyTextAsync() => _clipboard.SetTextAsync(SourceText);

    [RelayCommand]
    private Task CopyIpaAsync() => _clipboard.SetTextAsync(Ipa);

    [RelayCommand]
    private Task CopyPhonemesAsync() => _clipboard.SetTextAsync(PhonemesDisplay);

    private static string FormatTimestamp(double seconds) => TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss\.ff");
}
