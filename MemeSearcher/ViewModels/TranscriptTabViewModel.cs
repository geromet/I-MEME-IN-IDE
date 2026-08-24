using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using MemeSearcher.Infrastructure.Transcription;

namespace MemeSearcher.ViewModels;

/// <summary>One media's transcript, open as a tab in the transcript panel (#26). One tab per media, reused (not rebuilt) across repeated selections of results from the same media - only the highlight moves.</summary>
public partial class TranscriptTabViewModel : ObservableObject
{
    public Guid MediaId { get; }

    public string Title { get; }

    public ObservableCollection<TranscriptCueViewModel> Cues { get; }

    /// <summary>
    /// The view listens for this changing and scrolls the cue into view - a plain property rather
    /// than an event, since Avalonia's ListBox.ScrollIntoView needs to run on the UI thread in
    /// response to a bindable change, and a property survives re-subscription across tab switches
    /// more simply than an event the view would have to attach/detach per tab.
    /// </summary>
    [ObservableProperty]
    private TranscriptCueViewModel? _scrollTarget;

    public TranscriptTabViewModel(Guid mediaId, string title, IEnumerable<TranscriptCue> cues)
    {
        MediaId = mediaId;
        Title = title;
        Cues = new ObservableCollection<TranscriptCueViewModel>(cues.Select(c => new TranscriptCueViewModel(c)));
    }

    /// <summary>
    /// Highlights every cue that contains at least one matched phone, and scrolls to the first of
    /// them. A match can straddle two cues (the matcher aligns across segment boundaries the same
    /// way it does word boundaries) - highlighting only the first would silently drop the second
    /// half of a straddling match from view.
    /// </summary>
    public void HighlightSegments(IReadOnlySet<Guid> segmentIds)
    {
        TranscriptCueViewModel? firstMatch = null;

        foreach (var cue in Cues)
        {
            cue.IsHighlighted = segmentIds.Contains(cue.SegmentId);
            if (cue.IsHighlighted)
            {
                firstMatch ??= cue;
            }
        }

        ScrollTarget = firstMatch;
    }

    public void ClearHighlight()
    {
        foreach (var cue in Cues)
        {
            cue.IsHighlighted = false;
        }
    }
}
