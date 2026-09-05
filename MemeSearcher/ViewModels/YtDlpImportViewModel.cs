using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemeSearcher.Core.Jobs;
using MemeSearcher.Core.Models;
using MemeSearcher.Core.Settings;
using MemeSearcher.Infrastructure.Settings;
using MemeSearcher.Infrastructure.YtDlp;

namespace MemeSearcher.ViewModels;

/// <summary>
/// Presentation state for #27's URL -> review -> confirm flow. Planning is deliberately separate
/// from execution: ReviewCommand may enumerate and classify a URL, but the only path that can call
/// YtDlpImportOrchestrator is the work item created by ConfirmImportCommand.
/// </summary>
public partial class YtDlpImportViewModel : ViewModelBase
{
    private readonly Func<string, CancellationToken, Task<YtDlpImportPlan>> _planAsync;
    private readonly Func<YtDlpImportPlan, string, IProgress<string>, CancellationToken, Task<YtDlpImportSummary>> _importAsync;
    private readonly IJobQueue _jobQueue;
    private readonly Func<string> _language;
    private readonly Func<YtDlpMediaKind> _mediaKind;
    private readonly Func<string> _downloadLocation;

    private YtDlpImportPlan? _plan;
    private CancellationTokenSource? _reviewCancellation;

    public YtDlpImportViewModel(
        YtDlpImportPlanner planner,
        YtDlpImportOrchestrator orchestrator,
        IJobQueue jobQueue,
        ISettingsStore settings,
        YtDlpSettings ytDlpSettings)
        : this(
            planner.PlanAsync,
            orchestrator.ImportAsync,
            jobQueue,
            () => settings.Get(WhisperXSettings.Language),
            () => ytDlpSettings.ResolveMediaKind(settings),
            () => ytDlpSettings.ResolveDownloadDirectory(settings))
    {
    }

    /// <summary>
    /// Delegate seam used by presentation tests so the review/confirm contract stays deterministic
    /// and network-free. Production still delegates directly to YtDlpImportPlanner.PlanAsync and
    /// YtDlpImportOrchestrator.ImportAsync through the constructor above.
    /// </summary>
    public YtDlpImportViewModel(
        Func<string, CancellationToken, Task<YtDlpImportPlan>> planAsync,
        Func<YtDlpImportPlan, string, IProgress<string>, CancellationToken, Task<YtDlpImportSummary>> importAsync,
        IJobQueue jobQueue,
        Func<string> language,
        Func<YtDlpMediaKind> mediaKind,
        Func<string> downloadLocation)
    {
        _planAsync = planAsync;
        _importAsync = importAsync;
        _jobQueue = jobQueue;
        _language = language;
        _mediaKind = mediaKind;
        _downloadLocation = downloadLocation;
    }

    [ObservableProperty]
    private string _sourceUrl = "";

    [ObservableProperty]
    private bool _isPlanning;

    [ObservableProperty]
    private bool _hasPlan;

    [ObservableProperty]
    private string _reviewError = "";

    [ObservableProperty]
    private string _statusMessage = "Paste a YouTube channel or playlist URL to review it before downloading.";

    [ObservableProperty]
    private string _mediaKindDisplay = "";

    [ObservableProperty]
    private string _downloadLocationDisplay = "";

    public bool HasReviewError => ReviewError.Length > 0;

    public int TotalCount => _plan?.TotalCount ?? 0;

    public int NewCount => _plan?.NewCount ?? 0;

    public int AlreadyImportedCount => _plan?.AlreadyImportedCount ?? 0;

    public int PreviouslyFailedCount => _plan?.PreviouslyFailedCount ?? 0;

    public bool HasNewItems => NewCount > 0;

    /// <summary>
    /// Deferred projection over the planner's existing Items list. Large plans are not copied into
    /// a second DTO/list merely for display; Avalonia can enumerate the New subset directly.
    /// </summary>
    public IEnumerable<YtDlpImportPlanItem> NewItems =>
        _plan?.Items.Where(item => item.Status == YtDlpImportPlanStatus.New)
        ?? Enumerable.Empty<YtDlpImportPlanItem>();

    partial void OnReviewErrorChanged(string value) => OnPropertyChanged(nameof(HasReviewError));

    [RelayCommand]
    private async Task ReviewAsync()
    {
        var url = SourceUrl.Trim();
        if (url.Length == 0)
        {
            ReviewError = "Enter a YouTube channel or playlist URL first.";
            return;
        }

        _reviewCancellation?.Cancel();
        ClearPlan();
        ReviewError = "";
        StatusMessage = "Enumerating and checking the library...";

        var cancellation = new CancellationTokenSource();
        _reviewCancellation = cancellation;
        IsPlanning = true;

        try
        {
            var plan = await _planAsync(url, cancellation.Token);
            if (!ReferenceEquals(_reviewCancellation, cancellation))
            {
                return;
            }

            _plan = plan;
            MediaKindDisplay = _mediaKind() == YtDlpMediaKind.Video ? "Video" : "Audio only";
            DownloadLocationDisplay = _downloadLocation();
            HasPlan = true;
            NotifyPlanChanged();
            StatusMessage = plan.NewCount == 0
                ? "Nothing new to import from this URL."
                : $"Review {plan.NewCount} new item(s), then confirm to start the queued import.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (ReferenceEquals(_reviewCancellation, cancellation))
            {
                StatusMessage = "Review cancelled. No download was started.";
            }
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(_reviewCancellation, cancellation))
            {
                ReviewError = $"Could not review this URL: {ex.Message}";
                StatusMessage = "Review failed. No download was started.";
            }
        }
        finally
        {
            if (ReferenceEquals(_reviewCancellation, cancellation))
            {
                _reviewCancellation = null;
                IsPlanning = false;
            }

            cancellation.Dispose();
        }
    }

    [RelayCommand]
    private void ConfirmImport()
    {
        if (_plan is not { NewCount: > 0 } plan)
        {
            return;
        }

        var language = _language();
        _jobQueue.Enqueue(JobKind.YtDlpImport, $"YouTube import ({plan.NewCount} new item(s))", async (progress, ct) =>
        {
            await _importAsync(plan, language, progress, ct);
        });

        var queuedCount = plan.NewCount;
        ClearPlan();
        StatusMessage = $"Queued {queuedCount} new item(s). Progress and cancellation are available in Jobs / Errors.";
    }

    [RelayCommand]
    private void CancelReview()
    {
        _reviewCancellation?.Cancel();
        ClearPlan();
        ReviewError = "";
        StatusMessage = "Review cancelled. No download was started.";
    }

    private void ClearPlan()
    {
        _plan = null;
        HasPlan = false;
        MediaKindDisplay = "";
        DownloadLocationDisplay = "";
        NotifyPlanChanged();
    }

    private void NotifyPlanChanged()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(NewCount));
        OnPropertyChanged(nameof(AlreadyImportedCount));
        OnPropertyChanged(nameof(PreviouslyFailedCount));
        OnPropertyChanged(nameof(HasNewItems));
        OnPropertyChanged(nameof(NewItems));
    }
}
