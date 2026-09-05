using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemeSearcher.Core.Interfaces;
using MemeSearcher.Core.Search;
using MemeSearcher.Infrastructure.Ffmpeg;
using Microsoft.Extensions.DependencyInjection;

namespace MemeSearcher.ViewModels;

/// <summary>
/// Backs the shared Inspector panel for single-source and composite results. Both modes reuse the
/// same phone timing primitive; composite sections project already-shipped component provenance.
/// #35 waveform context is decoded only for the current selection and is cancelled/discarded when
/// the selection changes.
/// </summary>
public partial class InspectorViewModel : ViewModelBase
{
    private readonly IMediaPlayerLauncher _playerLauncher;
    private readonly Func<string, double, double, CancellationToken, Task<WaveformSampleResult>>? _sampleWaveformAsync;
    private CancellationTokenSource? _waveformCancellation;
    private long _selectionGeneration;

    public InspectorViewModel(IMediaPlayerLauncher playerLauncher)
        : this(playerLauncher, (Func<string, double, double, CancellationToken, Task<WaveformSampleResult>>?)null)
    {
    }

    public InspectorViewModel(
        IMediaPlayerLauncher playerLauncher,
        [FromKeyedServices("ffmpeg")] IExternalToolLocator ffmpegLocator)
        : this(playerLauncher, new WaveformSampler(ffmpegLocator).SampleAsync)
    {
    }

    public InspectorViewModel(
        IMediaPlayerLauncher playerLauncher,
        Func<string, double, double, CancellationToken, Task<WaveformSampleResult>>? sampleWaveformAsync)
    {
        _playerLauncher = playerLauncher;
        _sampleWaveformAsync = sampleWaveformAsync;
    }

    [ObservableProperty]
    private bool _hasSelection;

    [ObservableProperty]
    private bool _isSingleSelection;

    [ObservableProperty]
    private bool _isCompositeSelection;

    [ObservableProperty]
    private string _sourceTextDisplay = "";

    [ObservableProperty]
    private string _alignmentSummary = "";

    [ObservableProperty]
    private string? _mediaPath;

    [ObservableProperty]
    private string _seekStatus = "";

    [ObservableProperty]
    private bool _hasAlignmentSteps;

    public ObservableCollection<PhoneBlockViewModel> PhoneBlocks { get; } = [];
    public ObservableCollection<CompositeInspectorComponentViewModel> CompositeComponents { get; } = [];
    public WaveformStripViewModel Waveform { get; } = new();

    [ObservableProperty]
    private PhoneCoverageStripViewModel _coverageStrip = new([]);

    [ObservableProperty]
    private bool _hasExtraPhonemes;

    public ObservableCollection<string> ExtraPhonemes { get; } = [];

    public void Show(SearchResultRowViewModel? result)
    {
        ResetPresentation();

        if (result is null)
        {
            return;
        }

        HasSelection = true;
        IsSingleSelection = true;
        SourceTextDisplay = result.SourceText;
        MediaPath = result.MediaPath;

        foreach (var phone in result.MatchedPhoneDetails)
        {
            PhoneBlocks.Add(new PhoneBlockViewModel(phone));
        }

        CoverageStrip = result.CoverageStrip;
        HasAlignmentSteps = result.AlignmentSteps.Count > 0;

        foreach (var step in result.AlignmentSteps)
        {
            if (step.Op == AlignmentOp.CandidateExtra)
            {
                ExtraPhonemes.Add($"+{step.MatchSymbol}");
            }
        }

        HasExtraPhonemes = ExtraPhonemes.Count > 0;
        AlignmentSummary = SummarizeAlignment(PhoneBlocks, "match");
        StartWaveformLoad(Waveform, result.MediaPath, result.StartSeconds, result.EndSeconds, _selectionGeneration);
    }

    /// <summary>#35: show every component in assembly order using the provenance already present on the composite row.</summary>
    public void ShowComposite(CompositeSearchResultRowViewModel? result)
    {
        ResetPresentation();

        if (result is null)
        {
            return;
        }

        HasSelection = true;
        IsCompositeSelection = true;

        for (var index = 0; index < result.Components.Count; index++)
        {
            var component = new CompositeInspectorComponentViewModel(result.Components[index], index + 1);
            CompositeComponents.Add(component);
            StartWaveformLoad(
                component.Waveform,
                component.MediaPath,
                component.StartSeconds,
                component.EndSeconds,
                _selectionGeneration);
        }
    }

    [RelayCommand]
    private async Task SeekAsync(PhoneBlockViewModel block)
    {
        if (MediaPath is null || block.StartSeconds is not { } start)
        {
            return;
        }

        var outcome = await _playerLauncher.OpenAsync(MediaPath, start);
        SeekStatus = FormatSeekOutcome(outcome, block.Symbol, start, null);
    }

    [RelayCommand]
    private async Task SeekCompositeAsync(CompositeInspectorPhoneViewModel phone)
    {
        if (phone.MediaPath is null || phone.Block.StartSeconds is not { } start)
        {
            return;
        }

        var outcome = await _playerLauncher.OpenAsync(phone.MediaPath, start);
        SeekStatus = FormatSeekOutcome(outcome, phone.Block.Symbol, start, phone.MediaTitle);
    }

    private void StartWaveformLoad(
        WaveformStripViewModel target,
        string? mediaPath,
        double? startSeconds,
        double? endSeconds,
        long generation)
    {
        if (mediaPath is null || startSeconds is not { } start || endSeconds is not { } end)
        {
            target.SetUnavailable("Waveform unavailable: media file or timing is missing.");
            return;
        }

        if (_sampleWaveformAsync is null)
        {
            target.SetUnavailable("Waveform unavailable: waveform sampler is not configured.");
            return;
        }

        _waveformCancellation ??= new CancellationTokenSource();
        var token = _waveformCancellation.Token;
        target.Begin();
        _ = LoadWaveformAsync(target, mediaPath, start, end, generation, token);
    }

    private async Task LoadWaveformAsync(
        WaveformStripViewModel target,
        string mediaPath,
        double startSeconds,
        double endSeconds,
        long generation,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _sampleWaveformAsync!(mediaPath, startSeconds, endSeconds, cancellationToken);
            if (generation == _selectionGeneration && !cancellationToken.IsCancellationRequested)
            {
                target.Apply(result);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A newer selection owns the Inspector now. The stale completion publishes nothing.
        }
        catch (Exception ex)
        {
            if (generation == _selectionGeneration && !cancellationToken.IsCancellationRequested)
            {
                target.SetUnavailable($"Waveform unavailable: {ex.Message}");
            }
        }
    }

    private void ResetPresentation()
    {
        _waveformCancellation?.Cancel();
        _waveformCancellation?.Dispose();
        _waveformCancellation = null;
        _selectionGeneration++;
        Waveform.Reset();

        HasSelection = false;
        IsSingleSelection = false;
        IsCompositeSelection = false;
        SourceTextDisplay = "";
        AlignmentSummary = "";
        MediaPath = null;
        SeekStatus = "";
        HasAlignmentSteps = false;
        HasExtraPhonemes = false;
        CoverageStrip = new PhoneCoverageStripViewModel([]);
        PhoneBlocks.Clear();
        ExtraPhonemes.Clear();
        CompositeComponents.Clear();
    }

    private static string SummarizeAlignment(System.Collections.Generic.IEnumerable<PhoneBlockViewModel> phones, string subject)
    {
        var list = phones.ToList();
        return list.Count == 0
            ? $"No phone timing available for this {subject}."
            : list.All(p => p.IsAligned)
                ? "Precisely aligned (real per-phone timing)."
                : list.Any(p => p.IsAligned)
                    ? "Partially aligned - some phones are estimated."
                    : "Estimated timing - no phone-level alignment has run for this source.";
    }

    private static string FormatSeekOutcome(MediaLaunchResult outcome, string symbol, double start, string? mediaTitle) => outcome switch
    {
        { Success: true, SeekedToTimestamp: true } when mediaTitle is not null => $"Seeked {mediaTitle} to \"{symbol}\" at {FormatTimestamp(start)}.",
        { Success: true, SeekedToTimestamp: true } => $"Seeked to \"{symbol}\" at {FormatTimestamp(start)}.",
        { Success: true, SeekedToTimestamp: false } => "Opened, but no seek-capable player (mpv/vlc) was found.",
        _ => $"Couldn't open media: {outcome.Error}",
    };

    private static string FormatTimestamp(double seconds) => TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss\.ff");
}
