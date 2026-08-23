using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using MemeSearcher.Core.Settings;

namespace MemeSearcher.ViewModels;

/// <summary>
/// One editable setting. Subclassed per <see cref="SettingKind"/> so the view can select a
/// template by type rather than switching on an enum in XAML - which is what keeps the Settings
/// view free of knowledge about any particular setting (#24).
/// </summary>
public abstract partial class SettingRowViewModel(SettingDefinition definition, ISettingsStore store)
    : ViewModelBase
{
    protected ISettingsStore Store { get; } = store;

    public SettingDefinition Definition { get; } = definition;

    public string DisplayName => Definition.DisplayName;

    public string Description => Definition.Description;

    public static SettingRowViewModel Create(SettingDefinition definition, ISettingsStore store) =>
        definition.Kind switch
        {
            SettingKind.Choice => new ChoiceSettingViewModel(definition, store),
            SettingKind.Toggle => new ToggleSettingViewModel(definition, store),
            _ => new TextSettingViewModel(definition, store),
        };
}

public partial class ChoiceSettingViewModel : SettingRowViewModel
{
    [ObservableProperty]
    private SettingChoice _selectedChoice;

    public ChoiceSettingViewModel(SettingDefinition definition, ISettingsStore store)
        : base(definition, store)
    {
        Choices = definition.Choices ?? [];
        var current = store.Get(definition);
        _selectedChoice = Choices.FirstOrDefault(c => c.Value == current) ?? Choices[0];
    }

    public IReadOnlyList<SettingChoice> Choices { get; }

    partial void OnSelectedChoiceChanged(SettingChoice value) => Store.Set(Definition, value.Value);
}

public partial class ToggleSettingViewModel : SettingRowViewModel
{
    [ObservableProperty]
    private bool _isEnabled;

    public ToggleSettingViewModel(SettingDefinition definition, ISettingsStore store)
        : base(definition, store)
    {
        _isEnabled = store.GetBool(definition);
    }

    partial void OnIsEnabledChanged(bool value) => Store.SetBool(Definition, value);
}

public partial class TextSettingViewModel : SettingRowViewModel
{
    [ObservableProperty]
    private string _value;

    public TextSettingViewModel(SettingDefinition definition, ISettingsStore store)
        : base(definition, store)
    {
        _value = store.Get(definition);
    }

    partial void OnValueChanged(string value) => Store.Set(Definition, value);
}
