using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemeSearcher.Core.Interfaces;
using MemeSearcher.Core.Search;
using MemeSearcher.Infrastructure.Library;
using MemeSearcher.Services;

namespace MemeSearcher.ViewModels;

public partial class SearchViewModel(
    IPhoneticSearchService searchService,
    IPhonemizer phonemizer,
    LibraryService libraryService,
    IMediaPlayerLauncher playerLauncher,
    IClipboardService clipboard) : ViewModelBase
{
    // No language selector yet (handoff §34 leaves room for one) - en-US is the only phonemizer
    // language exercised so far.
    private const string Language = "en-US";

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

    public ObservableCollection<SearchResultRowViewModel> Results { get; } = [];

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        IsBusy = true;
        StatusMessage = "Searching...";

        try
        {
            var phonemized = await phonemizer.PhonemizeAsync(QueryText, Language);
            QueryIpa = phonemized.Ipa;

            var results = await searchService.SearchAsync(QueryText, Language, new SearchScope.AllIndexedMedia());
            var mediaPaths = await libraryService.GetPathsAsync(results.Select(r => r.MediaId));

            Results.Clear();
            foreach (var result in results)
            {
                var row = new SearchResultRowViewModel(result, playerLauncher, clipboard)
                {
                    MediaPath = mediaPaths.GetValueOrDefault(result.MediaId),
                };
                Results.Add(row);
            }

            StatusMessage = Results.Count > 0
                ? $"{Results.Count} result(s)."
                : "No matches found.";
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

    private bool CanSearch() => !IsBusy && !string.IsNullOrWhiteSpace(QueryText);
}
