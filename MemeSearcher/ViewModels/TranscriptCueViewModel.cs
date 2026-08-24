using System;
using CommunityToolkit.Mvvm.ComponentModel;
using MemeSearcher.Infrastructure.Transcription;

namespace MemeSearcher.ViewModels;

/// <summary>One rendered line in the transcript viewer (#26). IsHighlighted is mutated in place rather than the cue being replaced, so the ListBox doesn't need to re-virtualize the whole list on every selection change.</summary>
public partial class TranscriptCueViewModel(TranscriptCue cue) : ObservableObject
{
    public Guid SegmentId { get; } = cue.SegmentId;

    public string Text { get; } = cue.Text;

    public double? StartSeconds { get; } = cue.StartSeconds;

    public bool HasTiming { get; } = cue.StartSeconds is not null && cue.EndSeconds is not null;

    public string TimeRangeDisplay { get; } = cue.StartSeconds is { } start
        ? TimeSpan.FromSeconds(start).ToString(@"hh\:mm\:ss")
        : "no timing";

    /// <summary>Cue-level highlight (#26 Part 1) - true when this cue contains at least one phone from the currently-selected result. Word-level highlighting within a cue is Part 2.</summary>
    [ObservableProperty]
    private bool _isHighlighted;
}
