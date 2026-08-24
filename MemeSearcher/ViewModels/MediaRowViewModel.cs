using System;
using CommunityToolkit.Mvvm.ComponentModel;
using MemeSearcher.Infrastructure.Library;

namespace MemeSearcher.ViewModels;

public partial class MediaRowViewModel(MediaSummary summary) : ObservableObject
{
    public Guid Id { get; } = summary.Id;
    public string Title { get; } = summary.Title;
    public string Path { get; } = summary.Path;
    public string Language { get; } = summary.Language;
    public string ImportedAtDisplay { get; } = summary.CreatedAt.ToLocalTime().ToString("g");
    public string SegmentsDisplay { get; } = $"{summary.SegmentCount} segment(s), {summary.WordCount} word(s)";

    // handoff §21-22: playback only works when a media file was actually attached at import -
    // shown here so it's not a surprise when Play is unavailable on a search result later.
    public string PlayableMediaDisplay { get; } = summary.HasPlayableMedia ? "🎬 playable" : "transcript only";

    // Milestone 6: realignment (RealignAsync) needs the actual media file to align against -
    // transcript-only imports can't use it, same restriction as playback.
    public bool HasPlayableMedia { get; } = summary.HasPlayableMedia;

    [ObservableProperty]
    private bool _isRealigning;

    // Milestone 13: addendum §13's manual search scope. Two-way bound to a CheckBox; LibraryViewModel
    // subscribes to this property's changes to persist and to keep the "N of M selected" summary
    // current, rather than routing every checkbox click through an explicit command.
    [ObservableProperty]
    private bool _isSelected = summary.IsSelectedForSearch;

    // Milestone 3: duration is now actually populated (via ffprobe) instead of always zero.
    public string DurationDisplay { get; } = summary.Duration > TimeSpan.Zero
        ? summary.Duration.ToString(@"hh\:mm\:ss")
        : "";

    public string PhonemeCoverageDisplay { get; } = summary.WordCount > 0
        ? $"{summary.PhonemizedWordCount}/{summary.WordCount} phonemized"
        : "no words";

    // Two-step confirmation for the destructive delete-source-file action (addendum §29): the
    // first click arms it, a second click on the same row actually deletes.
    [ObservableProperty]
    private bool _isPendingDelete;
}
