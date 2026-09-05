using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MemeSearcher.Infrastructure.Ffmpeg;

namespace MemeSearcher.ViewModels;

public sealed class WaveformBarViewModel(double amplitude)
{
    public double Height { get; } = 4 + Math.Clamp(amplitude, 0, 1) * 36;
}

/// <summary>
/// Ephemeral Inspector waveform state. Samples are discarded on selection changes; this is not a
/// cache or media-derived persistence model.
/// </summary>
public partial class WaveformStripViewModel : ObservableObject
{
    public ObservableCollection<WaveformBarViewModel> Bars { get; } = [];

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private bool _isLoading;

    public bool HasBars => Bars.Count > 0;
    public bool HasStatus => Status.Length > 0;

    partial void OnStatusChanged(string value) => OnPropertyChanged(nameof(HasStatus));

    public void Begin()
    {
        Bars.Clear();
        OnPropertyChanged(nameof(HasBars));
        Status = "Loading waveform context...";
        IsLoading = true;
    }

    public void Apply(WaveformSampleResult result)
    {
        Bars.Clear();
        if (result.Success)
        {
            foreach (var amplitude in result.Amplitudes)
            {
                Bars.Add(new WaveformBarViewModel(amplitude));
            }

            Status = Bars.Count == 0
                ? "Waveform unavailable: no audio samples in this interval."
                : $"Waveform context: {FormatSeconds(result.DecodeStartSeconds)}–{FormatSeconds(result.DecodeEndSeconds)} (match {FormatSeconds(result.MatchStartSeconds)}–{FormatSeconds(result.MatchEndSeconds)}).";
        }
        else
        {
            Status = result.Error ?? "Waveform unavailable.";
        }

        IsLoading = false;
        OnPropertyChanged(nameof(HasBars));
    }

    public void SetUnavailable(string message)
    {
        Bars.Clear();
        OnPropertyChanged(nameof(HasBars));
        Status = message;
        IsLoading = false;
    }

    public void Reset()
    {
        Bars.Clear();
        OnPropertyChanged(nameof(HasBars));
        Status = "";
        IsLoading = false;
    }

    private static string FormatSeconds(double seconds) => TimeSpan.FromSeconds(seconds).ToString(@"mm\:ss\.ff");
}
