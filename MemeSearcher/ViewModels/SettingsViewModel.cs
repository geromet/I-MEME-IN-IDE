using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using MemeSearcher.Core.Settings;

namespace MemeSearcher.ViewModels;

/// <summary>One registered category and its settings, as rendered.</summary>
public class SettingsCategoryViewModel(ISettingsCategory category, ISettingsStore store) : ViewModelBase
{
    public string Name { get; } = category.Name;

    public string Description { get; } = category.Description;

    public IReadOnlyList<SettingRowViewModel> Settings { get; } =
        category.Settings.Select(d => SettingRowViewModel.Create(d, store)).ToArray();
}

/// <summary>
/// The Settings view (#24). Note what it does not contain: any mention of a language, a device or
/// a compute type. It renders whatever categories were registered, so adding a setting is a
/// registration rather than an edit here.
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsRegistry _registry;
    private readonly ISettingsStore _store;

    [ObservableProperty]
    private string _validationMessage = "";

    public SettingsViewModel(SettingsRegistry registry, ISettingsStore store)
    {
        _registry = registry;
        _store = store;

        Categories = new ObservableCollection<SettingsCategoryViewModel>(
            registry.Categories.Select(c => new SettingsCategoryViewModel(c, store)));

        // Re-validate on every change rather than on a Save button: settings here are applied
        // immediately, so an invalid combination should be visible the moment it is created, not
        // when the next transcription fails.
        store.Changed += (_, _) => Revalidate();
        Revalidate();
    }

    public ObservableCollection<SettingsCategoryViewModel> Categories { get; }

    public bool HasValidationMessage => ValidationMessage.Length > 0;

    private void Revalidate()
    {
        ValidationMessage = string.Join("\n", _registry.Validate(_store));
        OnPropertyChanged(nameof(HasValidationMessage));
    }
}
