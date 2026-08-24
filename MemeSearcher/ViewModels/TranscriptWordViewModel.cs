using System;
using CommunityToolkit.Mvvm.ComponentModel;
using MemeSearcher.Infrastructure.Transcription;

namespace MemeSearcher.ViewModels;

/// <summary>One word within a transcript cue (#26 Part 2). IsHighlighted is set only when the word was actually matched *and* its timing is trustworthy - see TranscriptTabViewModel.HighlightMatches for the degrade-to-cue-level rule.</summary>
public partial class TranscriptWordViewModel(TranscriptWord word) : ObservableObject
{
    public Guid WordId { get; } = word.WordId;

    public string Text { get; } = word.Text;

    public bool IsTimingInterpolated { get; } = word.IsTimingInterpolated;

    [ObservableProperty]
    private bool _isHighlighted;
}
