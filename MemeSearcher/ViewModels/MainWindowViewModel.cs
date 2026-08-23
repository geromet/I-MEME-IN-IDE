namespace MemeSearcher.ViewModels;

/// <summary>Shell hosting the top-level views (handoff §43: Search + Library, plus Settings from #24).</summary>
public class MainWindowViewModel(SearchViewModel search, LibraryViewModel library, SettingsViewModel settings)
    : ViewModelBase
{
    public SearchViewModel Search { get; } = search;
    public LibraryViewModel Library { get; } = library;

    // A tab for now. The shell layout work (#12) replaces this TabControl, and the panel framework
    // (#19) makes Settings a registered Tool rather than a hardcoded tab.
    public SettingsViewModel Settings { get; } = settings;
}
