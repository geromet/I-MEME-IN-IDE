using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemeSearcher.Infrastructure.Library;
using MemeSearcher.Services;

namespace MemeSearcher.ViewModels;

public partial class LibraryViewModel(
    LibraryService libraryService,
    MediaIngestionService ingestionService,
    IFilePickerService filePicker) : ViewModelBase
{
    private const string Language = "en-US";

    private static readonly HashSet<string> TranscriptExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".srt", ".vtt", ".txt",
    };

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "No media imported yet.";

    public ObservableCollection<MediaRowViewModel> Items { get; } = [];

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;

        try
        {
            var summaries = await libraryService.GetAllAsync();

            Items.Clear();
            foreach (var summary in summaries)
            {
                Items.Add(new MediaRowViewModel(summary));
            }

            StatusMessage = Items.Count > 0 ? $"{Items.Count} item(s) in the library." : "No media imported yet.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load the library: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportAsync()
    {
        var files = await filePicker.PickMediaFilesAsync();
        if (files.Count == 0)
        {
            return;
        }

        if (!TryClassify(files, out var transcriptPath, out var mediaPath, out var error))
        {
            StatusMessage = error;
            return;
        }

        var displayName = Path.GetFileName(transcriptPath ?? mediaPath!);

        IsBusy = true;
        // Milestone 3: no transcript file means whisperx has to run first, which is much slower
        // than parsing an SRT - say so rather than leaving the user staring at a generic message.
        StatusMessage = transcriptPath is null
            ? $"Transcribing {displayName} (this can take a while)..."
            : $"Importing {displayName}...";

        try
        {
            var result = await ingestionService.ImportAsync(new MediaIngestionRequest(mediaPath, transcriptPath, Language));

            StatusMessage = result.Outcome == MediaIngestionOutcome.Imported
                ? $"Imported {displayName}."
                : $"{displayName} was already indexed.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Import failed: {ex.Message}";
            return;
        }
        finally
        {
            IsBusy = false;
        }

        await LoadAsync();
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

    private bool CanImport() => !IsBusy;

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
}
