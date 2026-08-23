using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;

namespace MemeSearcher.Services;

public class AvaloniaClipboardService : IClipboardService
{
    public Task SetTextAsync(string text)
    {
        var window = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        return window?.Clipboard?.SetTextAsync(text) ?? Task.CompletedTask;
    }
}
