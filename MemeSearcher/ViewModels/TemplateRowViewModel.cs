using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using MemeSearcher.Core.Search;
using MemeSearcher.Infrastructure.Templates;

namespace MemeSearcher.ViewModels;

public partial class TemplateRowViewModel : ObservableObject
{
    private PhoneticSearchOptions _baseSearchOptions;

    public TemplateRowViewModel(TemplateSummary summary)
    {
        Id = summary.Id;
        Name = summary.Name;
        Description = summary.Description;
        HasDescription = !string.IsNullOrWhiteSpace(summary.Description);
        Mode = summary.Mode;
        TargetCatalogId = summary.TargetCatalogId;
        HasCustomSearchOptions = !string.IsNullOrWhiteSpace(summary.SearchOptionsJson);
        VariantCountDisplay = summary.Variants.Count == 1 ? "1 variant" : $"{summary.Variants.Count} variants";

        _baseSearchOptions = ParseOptions(summary.SearchOptionsJson, summary.Mode);
        LoadSearchOptionFields(_baseSearchOptions);

        _editName = summary.Name;
        _editDescription = summary.Description ?? "";
    }

    public Guid Id { get; }
    public string Name { get; }
    public string? Description { get; }
    public bool HasDescription { get; }
    public SearchMode Mode { get; }
    public Guid? TargetCatalogId { get; }
    public string VariantCountDisplay { get; }

    [ObservableProperty]
    private bool _hasCustomSearchOptions;

    [ObservableProperty]
    private bool _isPendingDelete;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _editName;

    [ObservableProperty]
    private string _editDescription;

    [ObservableProperty]
    private string _editInsertionCost = "";

    [ObservableProperty]
    private string _editDeletionCost = "";

    [ObservableProperty]
    private string _editSubstitutionMaxCost = "";

    [ObservableProperty]
    private string _editWordBoundaryCost = "";

    [ObservableProperty]
    private string _editMinimumScore = "";

    [ObservableProperty]
    private string _editMaxResults = "";

    public void ResetSearchOptionFields()
    {
        _baseSearchOptions = PhoneticSearchOptions.ForMode(Mode);
        LoadSearchOptionFields(_baseSearchOptions);
        HasCustomSearchOptions = false;
    }

    public bool TryBuildSearchOptions(out PhoneticSearchOptions options, out string error)
    {
        options = _baseSearchOptions;
        error = "";

        if (!TryParseNonNegative(EditInsertionCost, "Insertion cost", out var insertion, out error)
            || !TryParseNonNegative(EditDeletionCost, "Deletion cost", out var deletion, out error)
            || !TryParseNonNegative(EditSubstitutionMaxCost, "Substitution max cost", out var substitution, out error)
            || !TryParseNonNegative(EditWordBoundaryCost, "Word-boundary cost", out var wordBoundary, out error))
        {
            return false;
        }

        if (!double.TryParse(EditMinimumScore, NumberStyles.Float, CultureInfo.InvariantCulture, out var minimumScore)
            || !double.IsFinite(minimumScore)
            || minimumScore is < 0 or > 1)
        {
            error = "Minimum score must be a finite number from 0 through 1.";
            return false;
        }

        if (!int.TryParse(EditMaxResults, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxResults)
            || maxResults <= 0)
        {
            error = "Max results must be a positive whole number.";
            return false;
        }

        options = _baseSearchOptions with
        {
            InsertionCost = insertion,
            DeletionCost = deletion,
            SubstitutionMaxCost = substitution,
            WordBoundaryCost = wordBoundary,
            MinimumScore = minimumScore,
            MaxResults = maxResults,
        };
        return true;
    }

    public void AcceptSavedSearchOptions(PhoneticSearchOptions options)
    {
        _baseSearchOptions = options;
        LoadSearchOptionFields(options);
        HasCustomSearchOptions = true;
    }

    private void LoadSearchOptionFields(PhoneticSearchOptions options)
    {
        EditInsertionCost = Format(options.InsertionCost);
        EditDeletionCost = Format(options.DeletionCost);
        EditSubstitutionMaxCost = Format(options.SubstitutionMaxCost);
        EditWordBoundaryCost = Format(options.WordBoundaryCost);
        EditMinimumScore = Format(options.MinimumScore);
        EditMaxResults = options.MaxResults.ToString(CultureInfo.InvariantCulture);
    }

    private static PhoneticSearchOptions ParseOptions(string? json, SearchMode mode)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return PhoneticSearchOptions.ForMode(mode);
        }

        try
        {
            return JsonSerializer.Deserialize<PhoneticSearchOptions>(json) ?? PhoneticSearchOptions.ForMode(mode);
        }
        catch (JsonException)
        {
            return PhoneticSearchOptions.ForMode(mode);
        }
    }

    private static bool TryParseNonNegative(string text, string label, out double value, out string error)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            || !double.IsFinite(value)
            || value < 0)
        {
            error = $"{label} must be a finite number greater than or equal to zero.";
            return false;
        }

        error = "";
        return true;
    }

    private static string Format(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
}
