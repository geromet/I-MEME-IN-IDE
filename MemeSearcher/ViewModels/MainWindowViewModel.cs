namespace MemeSearcher.ViewModels;

/// <summary>Shell hosting the two top-level views (handoff §43: Search + Library).</summary>
public class MainWindowViewModel(SearchViewModel search, LibraryViewModel library) : ViewModelBase
{
    public SearchViewModel Search { get; } = search;
    public LibraryViewModel Library { get; } = library;
}
