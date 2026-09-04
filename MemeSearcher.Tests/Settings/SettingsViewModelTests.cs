using MemeSearcher.Core.Interfaces;
using MemeSearcher.Core.Settings;
using MemeSearcher.Infrastructure.Processes;
using MemeSearcher.Infrastructure.Settings;
using MemeSearcher.Tests.TestDoubles;
using MemeSearcher.ViewModels;

namespace MemeSearcher.Tests.Settings;

public class SettingsViewModelTests
{
    private static (SettingsViewModel ViewModel, ISettingsStore Store) Create()
    {
        var store = new InMemorySettingsStore();
        var registry = new SettingsRegistry([new WhisperXSettings(new CudaAvailabilityProbe())]);
        return (new SettingsViewModel(registry, store), store);
    }

    [Fact]
    public void Categories_AreBuiltFromTheRegistryAlone()
    {
        var (viewModel, _) = Create();

        var category = Assert.Single(viewModel.Categories);
        Assert.Equal(WhisperXSettings.CategoryName, category.Name);
        Assert.Equal(4, category.Settings.Count);
    }

    [Fact]
    public void ChoiceSettingsRenderAsChoiceRows()
    {
        var (viewModel, _) = Create();

        // Every WhisperX setting is a closed choice; if one is ever not, the UI needs a template
        // for its kind rather than silently falling back to a text box.
        Assert.All(viewModel.Categories[0].Settings, row => Assert.IsType<ChoiceSettingViewModel>(row));
    }

    [Fact]
    public void ChangingAChoiceWritesThroughToTheStore()
    {
        var (viewModel, store) = Create();
        var row = (ChoiceSettingViewModel)viewModel.Categories[0].Settings
            .Single(s => s.Definition.Key == WhisperXSettings.Language.Key);

        row.SelectedChoice = row.Choices.Single(c => c.Value == "nl");

        Assert.Equal("nl", store.Get(WhisperXSettings.Language));
    }

    [Fact]
    public void ValidationMessageAppearsWhenAnInvalidCombinationIsSelected()
    {
        var (viewModel, store) = Create();
        Assert.False(viewModel.HasValidationMessage);

        store.Set(WhisperXSettings.Device, WhisperXSettings.Cpu);
        store.Set(WhisperXSettings.ComputeType, WhisperXSettings.Float16);

        Assert.True(viewModel.HasValidationMessage);
        Assert.Contains("float16", viewModel.ValidationMessage);
    }

    [Fact]
    public void ToolStatus_MissingToolPreservesActionableLocatorError()
    {
        var status = ExternalToolStatusViewModel.Create(
            "yt-dlp",
            new ExternalToolStatus(
                IsInstalled: false,
                ExecutablePath: null,
                Version: null,
                Error: "Install yt-dlp: https://github.com/yt-dlp/yt-dlp#installation"),
            new DateOnly(2026, 9, 4));

        Assert.Equal("Not installed", status.Summary);
        Assert.Contains("Install yt-dlp", status.Details);
    }

    [Fact]
    public void ToolStatus_StaleYtDlpIsANonBlockingUpdateWarning()
    {
        var status = ExternalToolStatusViewModel.Create(
            "yt-dlp",
            new ExternalToolStatus(
                IsInstalled: true,
                ExecutablePath: "/usr/bin/yt-dlp",
                Version: "2026.01.01",
                Error: null),
            new DateOnly(2026, 9, 4));

        Assert.Contains("update recommended", status.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does not block use", status.Details, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/usr/bin/yt-dlp", status.Details);
    }

    [Fact]
    public void ToolStatus_RecentYtDlpRemainsOrdinaryInstalledStatus()
    {
        var status = ExternalToolStatusViewModel.Create(
            "yt-dlp",
            new ExternalToolStatus(
                IsInstalled: true,
                ExecutablePath: "/usr/bin/yt-dlp",
                Version: "2026.08.19",
                Error: null),
            new DateOnly(2026, 9, 4));

        Assert.Equal("Installed (2026.08.19)", status.Summary);
        Assert.DoesNotContain("recommended", status.Details, StringComparison.OrdinalIgnoreCase);
    }
}
