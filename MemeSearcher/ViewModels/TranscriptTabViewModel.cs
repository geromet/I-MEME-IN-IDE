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
    ///
    /// Within a highlighted cue, individual matched words light up instead of the whole line (#26
    /// Part 2) only when every matched word in that cue has real, non-interpolated timing - a cue
    /// with even one matched word whose timing is a character-proportional guess falls back to a
    /// whole-cue highlight rather than pointing confidently at one specific (possibly wrong) word.
    /// Likewise a match with no WordId at all for a cue (shouldn't happen via the normal search
    /// path, but MatchedPhone.WordId is nullable) falls back the same way.
    /// </summary>
    public void HighlightMatches(IReadOnlySet<Guid> segmentIds, IReadOnlySet<Guid> wordIds)
    {
        TranscriptCueViewModel? firstMatch = null;

        foreach (var cue in Cues)
        {
            cue.IsHighlighted = segmentIds.Contains(cue.SegmentId);

            if (!cue.IsHighlighted)
            {
                cue.HasWordHighlights = false;
                foreach (var word in cue.Words)
                {
                    word.IsHighlighted = false;
                }
                continue;
            }

            firstMatch ??= cue;

            var matchedWords = cue.Words.Where(w => wordIds.Contains(w.WordId)).ToList();
            var trustworthy = matchedWords.Count > 0 && matchedWords.All(w => !w.IsTimingInterpolated);
            cue.HasWordHighlights = trustworthy;

            foreach (var word in cue.Words)
            {
                word.IsHighlighted = trustworthy && wordIds.Contains(word.WordId);
            }
        }

        ScrollTarget = firstMatch;
    }

    public void ClearHighlight()
    {
        foreach (var cue in Cues)
        {
            cue.IsHighlighted = false;
            cue.HasWordHighlights = false;
            foreach (var word in cue.Words)
            {
                word.IsHighlighted = false;
            }
        }
    }
}
