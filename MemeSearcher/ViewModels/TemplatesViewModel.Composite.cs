using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;

namespace MemeSearcher.ViewModels;

public partial class TemplatesViewModel
{
    public ObservableCollection<CompositeSearchResultRowViewModel> CompositeResults { get; } = [];

    private bool _singleResultClearHookInstalled;

    [RelayCommand]
    private async Task RunCompositeAsync(TemplateRowViewModel template)
    {
        EnsureSingleResultClearHook();
        Results.Clear();
        CompositeResults.Clear();
        StatusMessage = "Searching across multiple sources...";

        try
        {
            var outcome = await templateSearchService.SearchCompositeAsync(template.Id);
            var mediaIds = outcome.Results
                .SelectMany(result => result.Components)
                .Select(component => component.MediaId)
                .Distinct()
                .ToList();
            var mediaTitles = await libraryService.GetTitlesAsync(mediaIds);
            var mediaPaths = await libraryService.GetPathsAsync(mediaIds);

            foreach (var result in outcome.Results)
            {
                CompositeResults.Add(new CompositeSearchResultRowViewModel(
                    result, mediaTitles, mediaPaths, clipExtractor, filePicker));
            }

            StatusMessage = CompositeResults.Count > 0
                ? $"{CompositeResults.Count} composite result(s) across {outcome.ScopeDescription}."
                : "No composite matches found.";
        }
        catch (Exception ex)
        {
            SetError($"Composite search failed: {ex.Message}");
        }
    }

    /// <summary>
    /// The existing single-source RunAsync predates composite template results. Hook its existing
    /// Results collection once so any subsequent single-source run clears stale composite output
    /// without duplicating or rewriting the original run command.
    /// </summary>
    private void EnsureSingleResultClearHook()
    {
        if (_singleResultClearHookInstalled)
        {
            return;
        }

        Results.CollectionChanged += (_, _) => CompositeResults.Clear();
        _singleResultClearHookInstalled = true;
    }
}
