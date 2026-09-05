using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemeSearcher.Core.Interfaces;
using MemeSearcher.Core.Jobs;
using MemeSearcher.Core.Settings;
using MemeSearcher.Infrastructure.Library;
using MemeSearcher.Infrastructure.Settings;
using MemeSearcher.Infrastructure.YtDlp;
using MemeSearcher.Services;

namespace MemeSearcher.ViewModels;

public partial class LibraryViewModel(
    LibraryService libraryService,
    MediaIngestionService ingestionService,
    IFilePickerService filePicker,
    ISettingsStore settings,
    IPhoneNGramIndexService indexService,
    IJobQueue jobQueue,
    YtDlpImportPlanner? ytDlpImportPlanner = null,
    YtDlpImportOrchestrator? ytDlpImportOrchestrator = null,
    YtDlpSettings? ytDlpSettings = null) : ViewModelBase
{
    // The language new imports are transcribed and phonemized in, chosen in Settings (#24).
    // Shared with SearchViewModel through the same setting, since a search must be phonemized in
    // the language its corpus was ingested with (#23).
    private string Language => settings.Get(WhisperXSettings.Language);

    private static readonly HashSet<string> TranscriptExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".srt", ".vtt", ".txt",
    };

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>
    /// Whether the current status message is a failure. Failures were previously indistinguishable
    /// from routine chatter - same thin grey line - so a realignment that failed with an accurate,
    /// actionable message read as the button doing nothing at all.
    /// </summary>
    [ObservableProperty]
    private bool _isStatusError;

    [ObservableProperty]
    private string _statusMessage = "No media imported yet.";

    /// <summary>
    /// Any plain assignment to StatusMessage is a non-failure, so clearing the flag here means a
    /// success message can never inherit the previous failure's styling. SetError assigns and then
    /// raises the flag.
    /// </summary>
    partial void OnStatusMessageChanged(string value) => IsStatusError = false;

    private void SetError(string message)
    {
        StatusMessage = message;
        IsStatusError = true;
    }

    public ObservableCollection<MediaRowViewModel> Items { get; } = [];

    /// <summary>
    /// #27 URL review/confirm presentation. Existing direct-construction tests do not need yt-dlp
    /// process/database dependencies, so the optional constructor parameters leave this null there;
    /// the production container already registers all three dependencies and therefore supplies it.
    /// </summary>
    public YtDlpImportViewModel? YtDlpImport { get; } =
        ytDlpImportPlanner is not null && ytDlpImportOrchestrator is not null && ytDlpSettings is not null
            ? new YtDlpImportViewModel(ytDlpImportPlanner, ytDlpImportOrchestrator, jobQueue, settings, ytDlpSettings)
            : null;

    /// <summary>Milestone 13: "N of M selected" - the same live scope indicator SearchViewModel shows in the search bar, kept here too since this is where the checkboxes actually live.</summary>
    [ObservableProperty]
    private string _selectionSummary = "";

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;

        try
        {
            var summaries = await libraryService.GetAllAsync();

            foreach (var row in Items)
            {
                row.PropertyChanged -= OnRowPropertyChanged;
            }

            Items.Clear();
            foreach (var summary in summaries)
            {
                var row = new MediaRowViewModel(summary);
                row.PropertyChanged += OnRowPropertyChanged;
                Items.Add(row);
            }

            UpdateSelectionSummary();
            StatusMessage = Items.Count > 0 ? $"{Items.Count} item(s) in the library." : "No media imported yet.";
        }
        catch (Exception ex)
        {
            SetError($"Failed to load the library: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// "All selected" rather than "11 of 11 selected" when nothing is excluded - matches
    /// SearchViewModel.ScopeSummary's "All indexed media" wording for the same state, so the two
    /// panels describing the same scope don't read as two different things.
    /// </summary>
    private void UpdateSelectionSummary()
    {
        if (Items.Count == 0)
        {
            SelectionSummary = "";
            return;
        }

        var selected = Items.Count(i => i.IsSelected);
        SelectionSummary = selected == Items.Count ? "All selected" : $"{selected} of {Items.Count} selected";
    }

    /// <summary>
    /// Persists a checkbox toggle immediately (addendum §13: selection survives restart), then
    /// updates the summary - in that order, not the reverse. MainWindowViewModel reacts to
    /// SelectionSummary changing by having every open search tab re-read the selection from the
    /// database (SearchViewModel.RefreshScopeSummaryAsync); raising that notification before the
    /// write lands would let the re-read race the write and observe stale data. The outer call is
    /// still fire-and-forget (matching how the rest of this codebase treats a UI-triggered write
    /// that doesn't need to block the click that caused it) - only the write-then-notify order
    /// inside it is guaranteed, not its timing relative to the caller.
    /// </summary>
    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MediaRowViewModel.IsSelected) || sender is not MediaRowViewModel row)
        {
            return;
        }

        _ = PersistSelectionThenUpdateSummaryAsync(row);
    }

    private async Task PersistSelectionThenUpdateSummaryAsync(MediaRowViewModel row)
    {
        await libraryService.SetSelectedAsync(row.Id, row.IsSelected);
        UpdateSelectionSummary();
    }

    [RelayCommand]
    private async Task SelectAllAsync()
    {
        await libraryService.SetAllSelectedAsync(true);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task SelectNoneAsync()
    {
        await libraryService.SetAllSelectedAsync(false);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task InvertSelectionAsync()
    {
        await libraryService.InvertSelectionAsync();
        await LoadAsync();
    }

    /// <summary>
    /// Milestone 14 (#14): import is now a queued, cancellable job rather than a direct await - the
    /// command returns as soon as the job is enqueued, so picking several files in a row queues
    /// several imports that then run one at a time (JobQueueService's concurrency limit) instead of
    /// blocking the UI on each in turn. Outcome/failure now show up as that job's row in the Jobs
    /// panel rather than this panel's single StatusMessage, so a failure isn't silently overwritten
    /// by whatever runs next.
    /// </summary>
    [RelayCommand]
    private async Task ImportAsync()
    {
        var files = await filePicker.PickMediaFilesAsync();
        if (files.Count == 0)
        {
            return;
        }

        if (!TryClassify(files, out var transcriptPath, out var mediaPath, out var error))
        {
            SetError(error);
            return;
        }

        var displayName = Path.GetFileName(transcriptPath ?? mediaPath!);
        var request = new MediaIngestionRequest(mediaPath, transcriptPath, Language);

        jobQueue.Enqueue(JobKind.Import, displayName, async (progress, ct) =>
        {
            // Milestone 3: no transcript file means whisperx has to run first, which is much
            // slower than parsing an SRT - say so rather than leaving the job looking stalled.
            progress.Report(transcriptPath is null
                ? $"Transcribing {displayName} (this can take a while)..."
                : $"Importing {displayName}...");

            var result = await ingestionService.ImportAsync(request, ct);

            progress.Report(result.Outcome == MediaIngestionOutcome.Imported
                ? $"Imported {displayName}."
                : $"{displayName} was already indexed.");

            await LoadAsync();
        });

        StatusMessage = $"Queued: {displayName}";
    }

    /// <summary>
    /// Sorts picked files into "the transcript" and "the optional companion media file" by
    /// extension (addendum §7: transcript and media filenames don't have to match, so this can't
    /// be done by name). A media file with no transcript is valid as of Milestone 3 - it gets
    /// transcribed directly instead of parsed from a file.
    /// </summary>
    private static bool TryClassify(
        IReadOnlyList<string> files, out string? transcriptPath, out string? mediaPath, out string error)
    {
        var transcripts = files.Where(f => TranscriptExtensions.Contains(Path.GetExtension(f))).ToList();
        var mediaFiles = files.Where(f => !TranscriptExtensions.Contains(Path.GetExtension(f))).ToList();

        transcriptPath = null;
        mediaPath = null;

        // No "both empty" check here: the caller already returns early when no files were picked,
        // and every picked file falls into exactly one of the two buckets above.
        if (transcripts.Count > 1)
        {
            error = "Select only one transcript file at a time.";
            return false;
        }

        if (mediaFiles.Count > 1)
        {
            error = "Select only one audio/video file at a time.";
            return false;
        }

        transcriptPath = transcripts.Count == 1 ? transcripts[0] : null;
        mediaPath = mediaFiles.Count == 1 ? mediaFiles[0] : null;
        error = "";
        return true;
    }

    [RelayCommand]
    private async Task RemoveFromLibraryAsync(MediaRowViewModel row)
    {
        await libraryService.RemoveAsync(row.Id, deleteSourceFile: false);
        Items.Remove(row);
        StatusMessage = $"Removed \"{row.Title}\" from the library. The source file was kept.";
    }

    /// <summary>First click arms the confirmation; a second click on the same row deletes for real.</summary>
    [RelayCommand]
    private async Task DeleteSourceFileAsync(MediaRowViewModel row)
    {
        if (!row.IsPendingDelete)
        {
            row.IsPendingDelete = true;
            return;
        }

        await libraryService.RemoveAsync(row.Id, deleteSourceFile: true);
        Items.Remove(row);
        StatusMessage = $"Deleted \"{row.Title}\" and its source file.";
    }

    [RelayCommand]
    private void CancelDelete(MediaRowViewModel row) => row.IsPendingDelete = false;

    /// <summary>
    /// Milestone 12: the shell's toolbar exposes the #9 index as something the user can trigger,
    /// not just something that runs silently during import. A full rebuild, not incremental - the
    /// same operation ReindexAllAsync already performs when repairing an index, just reachable now.
    /// Milestone 14: queued like import/realign, so it shows up (and can be cancelled) in the Jobs
    /// panel instead of blocking the toolbar for however long the rebuild takes.
    /// </summary>
    [RelayCommand]
    private void Reindex()
    {
        jobQueue.Enqueue(JobKind.Reindex, "Rebuild phonetic index", async (progress, ct) =>
        {
            progress.Report("Rebuilding phonetic index...");
            var summary = await indexService.ReindexAllAsync(ct);
            progress.Report($"Rebuilt index: {summary.PostingCount} posting(s) across {summary.MediaCount} media item(s).");
        });
    }

    /// <summary>
    /// Addendum §30: reprocess a media item's word/phone timing via the configured
    /// IAlignmentProvider (MFA by default - see App.axaml.cs) without retranscribing. Only
    /// available for items imported with a playable media file (RealignAsync requires it).
    /// Milestone 14: queued and cancellable, like import/reindex - row.IsRealigning still disables
    /// this row's button for the duration, now driven by the job's lifetime rather than a direct await.
    /// </summary>
    [RelayCommand]
    private void Realign(MediaRowViewModel row)
    {
        if (row.IsRealigning || !row.HasPlayableMedia)
        {
            return;
        }

        row.IsRealigning = true;

        jobQueue.Enqueue(JobKind.Realign, $"Realign \"{row.Title}\"", async (progress, ct) =>
        {
            try
            {
                progress.Report($"Realigning \"{row.Title}\"...");
                var result = await ingestionService.RealignAsync(row.Id, ct);

                // Coverage, not just a count: an aligner routinely fails to place some words, and
                // "1545 words" is meaningless without the denominator (#30).
                // Two coverage numbers, because they fail independently: word coverage says how
                // much of the transcript the aligner placed, phoneme coverage says how much of the
                // result the matcher can actually reason about. A corpus can be perfectly aligned
                // and still search badly because its phones are not modelled (#31), and that used
                // to be invisible.
                var coverage = result.PhonemeCoverage;
                var phonemeNote = coverage.KnownPercent >= 99.5
                    ? ""
                    : $" {coverage.UnknownPhones} of {coverage.TotalPhones} phone(s) "
                      + $"({100 - coverage.KnownPercent:F0}%) are not modelled by the phonetic matcher"
                      + $" ({string.Join(" ", coverage.UnknownSymbols.Take(8))}) - search quality will suffer.";

                progress.Report(
                    $"Realigned \"{row.Title}\": {result.UpdatedWordCount} of {result.TotalWordCount} word(s) "
                    + $"({result.CoveragePercent:F0}%), {result.UpdatedPhoneCount} phone(s).{phonemeNote}");
            }
            finally
            {
                row.IsRealigning = false;
            }

            await LoadAsync();
        });
    }
}
