using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using MemeSearcher.Infrastructure.Transcription;

namespace MemeSearcher.ViewModels;

/// <summary>One rendered line in the transcript viewer (#26). IsHighlighted/HasWordHighlights are mutated in place rather than the cue being replaced, so the ListBox doesn't need to re-virtualize the whole list on every selection change.</summary>
public partial class TranscriptCueViewModel : ObservableObject
{
    public Guid SegmentId { get; }

    public string Text { get; }

    public double? StartSeconds { get; }

    public bool HasTiming { get; }

    public string TimeRangeDisplay { get; }

    public ObservableCollection<TranscriptWordViewModel> Words { get; }

    /// <summary>Cue contains at least one phone from the currently-selected result (word- or cue-level). Drives the scroll target and the cue's left accent border regardless of granularity.</summary>
    [ObservableProperty]
    private bool _isHighlighted;

    /// <summary>
    /// True when the match within this cue was granular enough to point at specific words (#26 Part
    /// 2) - every matched word here has real, non-interpolated timing. False means the cue fell back
    /// to a whole-line highlight instead, either because timing was a guess or because the match had
    /// no word-level provenance at all.
    /// </summary>
    [ObservableProperty]
    private bool _hasWordHighlights;

    public TranscriptCueViewModel(TranscriptCue cue)
    {
        SegmentId = cue.SegmentId;
        Text = cue.Text;
        StartSeconds = cue.StartSeconds;
        HasTiming = cue.StartSeconds is not null && cue.EndSeconds is not null;
        TimeRangeDisplay = cue.StartSeconds is { } start
            ? TimeSpan.FromSeconds(start).ToString(@"hh\:mm\:ss")
            : "no timing";
        Words = new ObservableCollection<TranscriptWordViewModel>(cue.Words.Select(w => new TranscriptWordViewModel(w)));
    }
}
