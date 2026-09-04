using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemeSearcher.Core.Interfaces;
using MemeSearcher.Core.Settings;
using MemeSearcher.Infrastructure.Processes;

namespace MemeSearcher.ViewModels;

/// <summary>One registered category and its settings, as rendered.</summary>
public class SettingsCategoryViewModel(ISettingsCategory category, ISettingsStore store) : ViewModelBase
{
    public string Name { get; } = category.Name;

    public string Description { get; } = category.Description;

    public IReadOnlyList<SettingRowViewModel> Settings { get; } =
        category.Settings.Select(d => SettingRowViewModel.Create(d, store)).ToArray();
}

/// <summary>Presentation-only status for one registered external tool.</summary>
public sealed record ExternalToolStatusViewModel(string Name, string Summary, string Details)
{
    public static ExternalToolStatusViewModel Create(
        string toolName,
        ExternalToolStatus status,
        DateOnly today)
    {
        if (!status.IsInstalled)
        {
            return new ExternalToolStatusViewModel(
                toolName,
                "Not installed",
                status.Error ?? "The executable could not be located or run.");
        }

        var version = string.IsNullOrWhiteSpace(status.Version) ? "version unknown" : status.Version.Trim();
        var path = string.IsNullOrWhiteSpace(status.ExecutablePath) ? "Path unavailable" : status.ExecutablePath;

        if (string.Equals(toolName, "yt-dlp", StringComparison.OrdinalIgnoreCase) &&
            YtDlpToolLocator.IsVersionStale(status.Version, today))
        {
            return new ExternalToolStatusViewModel(
                toolName,
                $"Installed ({version}) — update recommended",
                $"{path}. This yt-dlp release is more than 180 days old. Updating is recommended because YouTube changes frequently; the warning does not block use.");
        }

        return new ExternalToolStatusViewModel(toolName, $"Installed ({version})", path);
    }
}

/// <summary>
/// The Settings view (#24). Registered setting categories remain data-driven. External-tool
/// diagnostics reuse #16's <see cref="IToolRegistry"/> rather than creating a parallel list of
/// executables, and #27's yt-dlp age heuristic is presentation-only/non-fatal.
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsRegistry _registry;
    private readonly ISettingsStore _store;
    private readonly IToolRegistry? _toolRegistry;
    private Task? _toolStatusRefreshTask;

    [ObservableProperty]
    private string _validationMessage = "";

    [ObservableProperty]
    private string _toolStatusError = "";

    [ObservableProperty]
    private bool _isRefreshingToolStatuses;

    public SettingsViewModel(
        SettingsRegistry registry,
        ISettingsStore store,
        IToolRegistry? toolRegistry = null)
    {
        _registry = registry;
        _store = store;
        _toolRegistry = toolRegistry;

        Categories = new ObservableCollection<SettingsCategoryViewModel>(
            registry.Categories.Select(c => new SettingsCategoryViewModel(c, store)));

        // Re-validate on every change rather than on a Save button: settings here are applied
        // immediately, so an invalid combination should be visible the moment it is created, not
        // when the next transcription fails.
        store.Changed += (_, _) => Revalidate();
        Revalidate();
    }

    public ObservableCollection<SettingsCategoryViewModel> Categories { get; }

    public ObservableCollection<ExternalToolStatusViewModel> ToolStatuses { get; } = [];

    public bool HasValidationMessage => ValidationMessage.Length > 0;

    public bool HasToolStatusError => ToolStatusError.Length > 0;

    [RelayCommand]
    public Task RefreshToolStatusesAsync()
    {
        if (_toolRegistry is null)
        {
            return Task.CompletedTask;
        }

        if (_toolStatusRefreshTask is { IsCompleted: false })
        {
            return _toolStatusRefreshTask;
        }

        _toolStatusRefreshTask = RefreshToolStatusesCoreAsync();
        return _toolStatusRefreshTask;
    }

    private async Task RefreshToolStatusesCoreAsync()
    {
        IsRefreshingToolStatuses = true;
        ToolStatusError = "";
        OnPropertyChanged(nameof(HasToolStatusError));

        try
        {
            var statuses = await _toolRegistry!.LocateAllAsync();
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            ToolStatuses.Clear();
            foreach (var locator in _toolRegistry.Locators)
            {
                if (statuses.TryGetValue(locator.ToolName, out var status))
                {
                    ToolStatuses.Add(ExternalToolStatusViewModel.Create(locator.ToolName, status, today));
                }
            }
        }
        catch (Exception ex)
        {
            ToolStatusError = $"Could not refresh external-tool status: {ex.Message}";
            OnPropertyChanged(nameof(HasToolStatusError));
        }
        finally
        {
            IsRefreshingToolStatuses = false;
        }
    }

    private void Revalidate()
    {
        ValidationMessage = string.Join("\n", _registry.Validate(_store));
        OnPropertyChanged(nameof(HasValidationMessage));
    }
}
