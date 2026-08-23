using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemeSearcher.Core.Interfaces;
using MemeSearcher.Core.Languages;
using MemeSearcher.Core.Models;
using MemeSearcher.Core.Search;
using MemeSearcher.Infrastructure.Ffmpeg;
using MemeSearcher.Infrastructure.Library;
using MemeSearcher.Infrastructure.Search;
using MemeSearcher.Services;

namespace MemeSearcher.ViewModels;

public partial class SearchViewModel(
    IPhoneticSearchService searchService,
    ICompositeSearchService compositeSearchService,
    IPhonemizer phonemizer,
    IQueryPhonemizationCache queryCache,
    SearchHistoryService searchHistoryService,
    LibraryService libraryService,
    IMediaPlayerLauncher playerLauncher,
    IClipboardService clipboard,
    FFmpegClipExtractor clipExtractor,
    IFilePickerService filePicker) : ViewModelBase
{
    // No language selector yet - that arrives with the settings surface (#24). Until then this
    // reads the shared catalog default rather than holding its own literal, so it cannot drift
    // from LibraryViewModel's the way two separate "en-US" constants did (#23).
    private static string Language => LanguageCatalog.Default.Id;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private string _queryText = "";

    [ObservableProperty]
    private string _queryIpa = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Import media in the Library tab, then search.";

    // Milestone 4: single-source is the default per addendum §17 - composite must be opt-in.
    [ObservableProperty]
    private bool _isCompositeMode;

    public ObservableCollection<SearchResultRowViewModel> Results { get; } = [];

    public ObservableCollection<CompositeSearchResultRowViewModel> CompositeResults { get; } = [];

    public ObservableCollection<SearchHistoryEntry> RecentSearches { get; } = [];

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        IsBusy = true;
        StatusMessage = "Searching...";

        try
        {
            // Milestone 7: goes through the same query-representation cache the search services
            // use, so this call and the one inside SearchAsync/SearchCompositeAsync below (same
            // query text/language, one request apart) don't phonemize the query twice.
            var phonemized = await queryCache.GetOrAddAsync(
                QueryText, Language, ct => phonemizer.PhonemizeAsync(QueryText, Language, ct));
            QueryIpa = phonemized.Ipa;

            var resultCount = IsCompositeMode
                ? await SearchCompositeAsync()
                : await SearchSingleSourceAsync();

            await searchHistoryService.RecordAsync(
                QueryText, Language, IsCompositeMode, "All indexed media", resultCount);
            await LoadRecentSearchesAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Search failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RerunSearchAsync(SearchHistoryEntry entry)
    {
        QueryText = entry.QueryText;
        IsCompositeMode = entry.IsComposite;
        await SearchAsync();
    }

    [RelayCommand]
    public async Task LoadRecentSearchesAsync()
    {
        var recent = await searchHistoryService.GetRecentAsync();

        RecentSearches.Clear();
        foreach (var entry in recent)
        {
            RecentSearches.Add(entry);
        }
    }

    private async Task<int> SearchSingleSourceAsync()
    {
        CompositeResults.Clear();

        var results = await searchService.SearchAsync(QueryText, Language, new SearchScope.AllIndexedMedia());
        var mediaPaths = await libraryService.GetPathsAsync(results.Select(r => r.MediaId));

        Results.Clear();
        foreach (var result in results)
        {
            var row = new SearchResultRowViewModel(result, playerLauncher, clipboard, clipExtractor, filePicker)
            {
                MediaPath = mediaPaths.GetValueOrDefault(result.MediaId),
            };
            Results.Add(row);
        }

        StatusMessage = Results.Count > 0
            ? $"{Results.Count} result(s)."
            : "No matches found.";

        return Results.Count;
    }

    private async Task<int> SearchCompositeAsync()
    {
        Results.Clear();

        var results = await compositeSearchService.SearchAsync(QueryText, Language, new SearchScope.AllIndexedMedia());
        var allMediaIds = results.SelectMany(r => r.Components.Select(c => c.MediaId)).Distinct().ToList();
        var mediaTitles = await libraryService.GetTitlesAsync(allMediaIds);
        var mediaPaths = await libraryService.GetPathsAsync(allMediaIds);

        CompositeResults.Clear();
        foreach (var result in results)
        {
            CompositeResults.Add(new CompositeSearchResultRowViewModel(result, mediaTitles, mediaPaths, clipExtractor, filePicker));
        }

        StatusMessage = CompositeResults.Count > 0
            ? $"{CompositeResults.Count} composite result(s)."
            : "No composite matches found.";

        return CompositeResults.Count;
    }

    private bool CanSearch() => !IsBusy && !string.IsNullOrWhiteSpace(QueryText);
}
