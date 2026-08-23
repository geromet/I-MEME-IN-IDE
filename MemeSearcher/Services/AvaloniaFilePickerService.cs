using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace MemeSearcher.Services;

public class AvaloniaFilePickerService : IFilePickerService
{
    public async Task<IReadOnlyList<string>> PickMediaFilesAsync()
    {
        var window = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (window is null)
        {
            return [];
        }

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add media (a transcript, an audio/video file, or both)",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Transcript or media files")
                {
                    Patterns =
                    [
                        "*.srt", "*.vtt", "*.txt",
                        "*.mp4", "*.mkv", "*.mov", "*.webm", "*.avi",
                        "*.mp3", "*.wav", "*.m4a", "*.flac", "*.ogg",
                    ],
                },
            ],
        });

        return files.Select(f => f.TryGetLocalPath()).Where(p => p is not null).Cast<string>().ToList();
    }

    public async Task<string?> PickClipExportPathAsync(string suggestedFileName)
    {
        var window = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (window is null)
        {
            return null;
        }

        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export clip",
            SuggestedFileName = suggestedFileName,
        });

        return file?.TryGetLocalPath();
    }
}
