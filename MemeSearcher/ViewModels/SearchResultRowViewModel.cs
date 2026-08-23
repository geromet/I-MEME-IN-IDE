using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemeSearcher.Core.Interfaces;
using MemeSearcher.Core.Search;
using MemeSearcher.Services;

namespace MemeSearcher.ViewModels;

/// <summary>
/// Display-formatted projection of a Core SearchResult (handoff §45: raw distance is never the
/// user-facing number) plus result interaction (handoff §21): play/seek and copy actions.
/// </summary>
public partial class SearchResultRowViewModel : ObservableObject
{
    private readonly IMediaPlayerLauncher _playerLauncher;
    private readonly IClipboardService _clipboard;

    public Guid MediaId { get; }
    public double StartSeconds { get; }
    public string ScoreDisplay { get; }
    public string TimeRangeDisplay { get; }
    public string SourceText { get; }
    public string Ipa { get; }
    public string PhonemesDisplay { get; }

    // Resolved after construction (batched across all results by SearchViewModel), so the Play
    // button's enabled state has to react to it arriving.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    private string? _mediaPath;

    [ObservableProperty]
    private string _playbackStatus = "";

    public SearchResultRowViewModel(SearchResult result, IMediaPlayerLauncher playerLauncher, IClipboardService clipboard)
    {
        _playerLauncher = playerLauncher;
        _clipboard = clipboard;

        MediaId = result.MediaId;
        StartSeconds = result.StartSeconds;
        ScoreDisplay = $"{result.Score:P0}";
        TimeRangeDisplay = $"{FormatTimestamp(result.StartSeconds)} - {FormatTimestamp(result.EndSeconds)}";
        SourceText = result.SourceText;
        Ipa = result.Ipa;
        PhonemesDisplay = string.Join(' ', result.MatchPhonemes);
    }

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private async Task PlayAsync()
    {
        if (MediaPath is null)
        {
            return;
        }

        var result = await _playerLauncher.OpenAsync(MediaPath, StartSeconds);

        PlaybackStatus = result switch
        {
            { Success: true, SeekedToTimestamp: true } => "Opened at timestamp.",
            { Success: true, SeekedToTimestamp: false } =>
                "Opened, but no seek-capable player (mpv/vlc) was found - jump to the timestamp manually.",
            _ => $"Couldn't open media: {result.Error}",
        };
    }

    private bool CanPlay() => MediaPath is not null;

    [RelayCommand]
    private Task CopyTimestampAsync() => _clipboard.SetTextAsync(FormatTimestamp(StartSeconds));

    [RelayCommand]
    private Task CopyTextAsync() => _clipboard.SetTextAsync(SourceText);

    [RelayCommand]
    private Task CopyIpaAsync() => _clipboard.SetTextAsync(Ipa);

    [RelayCommand]
    private Task CopyPhonemesAsync() => _clipboard.SetTextAsync(PhonemesDisplay);

    private static string FormatTimestamp(double seconds) => TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss\.ff");
}
