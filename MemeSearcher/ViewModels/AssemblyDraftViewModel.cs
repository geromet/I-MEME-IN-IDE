using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemeSearcher.Core.Interfaces;
using MemeSearcher.Infrastructure.Ffmpeg;
using MemeSearcher.Services;

namespace MemeSearcher.ViewModels;

/// <summary>
/// #25 exit criterion 3: "drag-and-drop mix-and-match: pick a hit for one span, another for the
/// next, see the query fill up, audition the concatenation." A manual driver for the composite
/// assembly the app can already do mechanically (FFmpegClipExtractor.ExtractCompositeAsync) - what
/// was missing was the user steering which candidate fills which span, not the stitching itself.
///
/// Built from the query's already-grouped spans (ResultGrouping.GroupByCoveredSpan) rather than
/// from scratch - a slot's candidate list is exactly what that span's group already collected.
/// </summary>
public partial class AssemblyDraftViewModel : ObservableObject
{
    private readonly IMediaPlayerLauncher _playerLauncher;
    private readonly FFmpegClipExtractor _clipExtractor;
    private readonly IFilePickerService _filePicker;

    public ObservableCollection<AssemblySlotViewModel> Slots { get; } = [];

    [ObservableProperty]
    private string _status = "";

    public AssemblyDraftViewModel(
        IReadOnlyList<ResultGroupViewModel> groups,
        IMediaPlayerLauncher playerLauncher,
        FFmpegClipExtractor clipExtractor,
        IFilePickerService filePicker)
    {
        _playerLauncher = playerLauncher;
        _clipExtractor = clipExtractor;
        _filePicker = filePicker;

        foreach (var group in groups)
        {
            var sample = group.Members[0];
            var slot = new AssemblySlotViewModel(group.Label, sample.QueryStart, sample.QueryEnd, group.Members);
            // A slot's own choice can flip whether the draft is complete enough to audition/export -
            // CanExecute needs telling explicitly since CommunityToolkit doesn't see through to a
            // nested view model's property changes on its own.
            slot.PropertyChanged += OnSlotPropertyChanged;
            Slots.Add(slot);
        }
    }

    private void OnSlotPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AssemblySlotViewModel.SelectedCandidate))
        {
            return;
        }

        OnPropertyChanged(nameof(IsComplete));
        OnPropertyChanged(nameof(CoverageSummary));
        AuditionCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// At least one slot has a chosen candidate - a slot is a *choice*, not a requirement, so a
    /// user assembling only part of the query (skipping a span they don't want, by leaving its
    /// slot unselected) still has something to audition/export. Requiring every slot would force
    /// stitching every covered span found in the results together regardless of overlap, which for
    /// the moeten case (spans [0,2), [0,3), [2,5), [3,5) all present at once) would concatenate
    /// overlapping candidates into something roughly double the query - not what "pick a hit for
    /// one span, another for the next" means.
    /// </summary>
    public bool IsComplete => Slots.Any(s => s.SelectedCandidate is not null);

    /// <summary>"See the query fill up" (#25's own framing) made literal: how many of the query's covered spans currently have a chosen candidate.</summary>
    public string CoverageSummary => $"{Slots.Count(s => s.SelectedCandidate is not null)} of {Slots.Count} span(s) selected";

    /// <summary>Null when there's nothing playable to render - a chosen candidate still needs a locatable, timed clip to contribute, the same requirement CompositeSearchResultRowViewModel.CanExportClip already enforces for automatic composite results (#32). Skipped (unselected) slots are simply omitted, not treated as failures.</summary>
    private List<(string MediaPath, double StartSeconds, double EndSeconds)>? BuildClipList()
    {
        var clips = new List<(string, double, double)>();
        foreach (var slot in Slots)
        {
            if (slot.SelectedCandidate is not { } candidate)
            {
                continue;
            }

            if (candidate.MediaPath is null || !candidate.HasTiming)
            {
                return null;
            }

            clips.Add((candidate.MediaPath, candidate.StartSeconds!.Value, candidate.EndSeconds!.Value));
        }

        return clips.Count > 0 ? clips : null;
    }

    [RelayCommand(CanExecute = nameof(IsComplete))]
    private async Task AuditionAsync()
    {
        var clips = BuildClipList();
        if (clips is null)
        {
            Status = "Pick at least one slot, and make sure every chosen candidate has a timed, playable clip, before you can audition.";
            return;
        }

        Status = "Building preview...";
        var previewPath = Path.Combine(Path.GetTempPath(), $"memesearcher-assembly-preview-{Guid.NewGuid():N}.mp4");
        var extraction = await _clipExtractor.ExtractCompositeAsync(clips, previewPath);

        if (!extraction.Success)
        {
            Status = $"Couldn't build preview: {extraction.Error}";
            return;
        }

        var launch = await _playerLauncher.OpenAsync(previewPath, 0);
        Status = launch.Success
            ? "Opened the assembled preview."
            : $"Built the preview, but couldn't open a player: {launch.Error}";
    }

    [RelayCommand(CanExecute = nameof(IsComplete))]
    private async Task ExportAsync()
    {
        var clips = BuildClipList();
        if (clips is null)
        {
            Status = "Pick at least one slot, and make sure every chosen candidate has a timed, playable clip, before you can export.";
            return;
        }

        var outputPath = await _filePicker.PickClipExportPathAsync("assembled.mp4");
        if (outputPath is null)
        {
            return;
        }

        Status = "Exporting assembled clip...";
        var result = await _clipExtractor.ExtractCompositeAsync(clips, outputPath);

        Status = result.Success
            ? $"Exported to {Path.GetFileName(outputPath)}."
            : $"Export failed: {result.Error}";
    }
}
