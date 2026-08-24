namespace MemeSearcher.Shell;

/// <summary>Fixed dock zones (#19). Full floating/tear-out docking is explicitly out of scope.</summary>
public enum DockZone
{
    Left,
    Right,
    Bottom,
}

/// <summary>
/// A panel the shell can host (#19). Registering one of these - via DI, as an <see cref="IViewPanel"/>
/// - is the entire mechanism: no shell XAML edit is required to add, remove, or relocate a panel.
/// </summary>
public interface IViewPanel
{
    /// <summary>Stable identity, used as the key for persisted layout state - must not be renamed once shipped.</summary>
    string Id { get; }

    string DisplayName { get; }

    DockZone PreferredDock { get; }

    bool VisibleByDefault { get; }

    /// <summary>The panel's own view model; the shell resolves its View via DataTemplate on this object's type.</summary>
    object ViewModel { get; }
}

/// <summary>Stable panel ids, shared between DI registration (App.axaml.cs) and shell code that needs to find a specific panel (e.g. the toolbar's dedicated Jobs toggle).</summary>
public static class PanelIds
{
    public const string Library = "library";
    public const string Inspector = "inspector";
    public const string Jobs = "jobs";
    public const string Settings = "settings";
    public const string Catalogs = "catalogs";
    public const string Templates = "templates";
    public const string Transcript = "transcript";
}

/// <summary>Plain <see cref="IViewPanel"/> implementation for DI registration - see App.axaml.cs.</summary>
public sealed class ViewPanelDescriptor(
    string id, string displayName, DockZone preferredDock, object viewModel, bool visibleByDefault = true)
    : IViewPanel
{
    public string Id { get; } = id;

    public string DisplayName { get; } = displayName;

    public DockZone PreferredDock { get; } = preferredDock;

    public bool VisibleByDefault { get; } = visibleByDefault;

    public object ViewModel { get; } = viewModel;
}
