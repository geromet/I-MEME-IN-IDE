using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemeSearcher.Core.Models;
using MemeSearcher.Core.Search;

namespace MemeSearcher.ViewModels;

/// <summary>
/// Thin presentation input for #43. The domain predicate remains MediaSearchFacets; this type only
/// converts compact text/toggle controls into that single existing contract.
/// </summary>
public sealed record SearchFacetInput(
    string ChannelsText,
    string LanguagesText,
    bool IncludeUnknownChannel,
    bool IncludeAudio,
    bool IncludeVideo,
    bool IncludeLocalMedia,
    string UploadedOnOrAfterText,
    string UploadedOnOrBeforeText)
{
    public bool TryBuild(out MediaSearchFacets facets, out string? validationError)
    {
        facets = MediaSearchFacets.Empty;
        validationError = null;

        if (!TryParseDate(UploadedOnOrAfterText, "Upload date from", out var uploadedOnOrAfter, out validationError) ||
            !TryParseDate(UploadedOnOrBeforeText, "Upload date to", out var uploadedOnOrBefore, out validationError))
        {
            return false;
        }

        if (uploadedOnOrAfter is not null && uploadedOnOrBefore is not null && uploadedOnOrAfter > uploadedOnOrBefore)
        {
            validationError = "Upload date from must not be after upload date to.";
            return false;
        }

        var kinds = new List<YtDlpMediaKind>(2);
        if (IncludeAudio)
        {
            kinds.Add(YtDlpMediaKind.Audio);
        }
        if (IncludeVideo)
        {
            kinds.Add(YtDlpMediaKind.Video);
        }

        facets = new MediaSearchFacets
        {
            Channels = ParseTerms(ChannelsText),
            IncludeUnknownChannel = IncludeUnknownChannel,
            Languages = ParseTerms(LanguagesText),
            MediaKinds = kinds,
            IncludeNonYtDlpMedia = IncludeLocalMedia,
            UploadedOnOrAfter = uploadedOnOrAfter,
            UploadedOnOrBefore = uploadedOnOrBefore,
        };
        return true;
    }

    private static IReadOnlyCollection<string> ParseTerms(string value) =>
        value.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(term => term.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool TryParseDate(string value, string label, out DateOnly? date, out string? validationError)
    {
        value = value.Trim();
        if (value.Length == 0)
        {
            date = null;
            validationError = null;
            return true;
        }

        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            date = parsed;
            validationError = null;
            return true;
        }

        date = null;
        validationError = $"{label} must use YYYY-MM-DD.";
        return false;
    }
}

public partial class SearchViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFacets))]
    private string _facetChannels = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFacets))]
    private string _facetLanguages = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFacets))]
    private bool _facetIncludeUnknownChannel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFacets))]
    private bool _facetIncludeAudio;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFacets))]
    private bool _facetIncludeVideo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFacets))]
    private bool _facetIncludeLocalMedia;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFacets))]
    private string _facetUploadedOnOrAfter = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFacets))]
    private string _facetUploadedOnOrBefore = "";

    [ObservableProperty]
    private string _facetValidationMessage = "";

    public bool HasActiveFacets => TryBuildFacets(out var facets, out _) && !facets.IsEmpty;

    private bool TryBuildFacets(out MediaSearchFacets facets, out string? validationError) =>
        new SearchFacetInput(
            FacetChannels,
            FacetLanguages,
            FacetIncludeUnknownChannel,
            FacetIncludeAudio,
            FacetIncludeVideo,
            FacetIncludeLocalMedia,
            FacetUploadedOnOrAfter,
            FacetUploadedOnOrBefore)
        .TryBuild(out facets, out validationError);

    [RelayCommand]
    private async Task ClearFacetsAsync()
    {
        FacetChannels = "";
        FacetLanguages = "";
        FacetIncludeUnknownChannel = false;
        FacetIncludeAudio = false;
        FacetIncludeVideo = false;
        FacetIncludeLocalMedia = false;
        FacetUploadedOnOrAfter = "";
        FacetUploadedOnOrBefore = "";
        FacetValidationMessage = "";
        await RefreshScopeSummaryAsync();
    }
}
