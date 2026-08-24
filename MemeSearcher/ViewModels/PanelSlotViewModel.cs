using CommunityToolkit.Mvvm.ComponentModel;
using MemeSearcher.Core.Settings;
using MemeSearcher.Shell;

namespace MemeSearcher.ViewModels;

/// <summary>
/// Wraps a registered <see cref="IViewPanel"/> with its persisted visibility (#19: "layout survives
/// restart"). Reuses <see cref="ISettingsStore"/> - the app's one JSON-backed key/value store - for
/// this rather than inventing a parallel persistence mechanism. The definition is built locally and
/// never registered as an <see cref="ISettingsCategory"/>, so it never appears in the Settings UI;
/// it exists only to get the store's Get/Set/atomic-write behavior for free.
/// </summary>
public partial class PanelSlotViewModel : ViewModelBase
{
    private readonly ISettingsStore _store;
    private readonly SettingDefinition _visibilityDefinition;

    public IViewPanel Panel { get; }

    public string Id => Panel.Id;

    public string DisplayName => Panel.DisplayName;

    public DockZone Dock => Panel.PreferredDock;

    public object Content => Panel.ViewModel;

    [ObservableProperty]
    private bool _isVisible;

    public PanelSlotViewModel(IViewPanel panel, ISettingsStore store)
    {
        Panel = panel;
        _store = store;
        _visibilityDefinition = new SettingDefinition(
            Key: $"shell.panel.{panel.Id}.visible",
            Category: "Shell",
            DisplayName: panel.DisplayName,
            Description: "Internal shell layout state - not shown in the Settings panel.",
            Kind: SettingKind.Toggle,
            DefaultValue: panel.VisibleByDefault ? "true" : "false");
        _isVisible = _store.GetBool(_visibilityDefinition);
    }

    partial void OnIsVisibleChanged(bool value) => _store.SetBool(_visibilityDefinition, value);
}
