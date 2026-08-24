using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemeSearcher.Core.Interfaces;
using MemeSearcher.Core.Phonetics;
using MemeSearcher.Core.Search;
using MemeSearcher.Infrastructure.Catalogs;
using MemeSearcher.Infrastructure.Ffmpeg;
using MemeSearcher.Infrastructure.Library;
using MemeSearcher.Infrastructure.Templates;
using MemeSearcher.Services;

namespace MemeSearcher.ViewModels;

/// <summary>One dropdown entry for a template's target catalog. Id null = "All indexed media".</summary>
public record CatalogOption(Guid? Id, string Name);

/// <summary>
/// Milestone 18 (#21): named, saved queries defined as hand-authored phone sequences rather than
/// text - the Templates view (browse/run/edit/delete) with the phone-sequence editor folded in as
/// an inline form, rather than a separate Tool. #19's own close-out left "Tools" as plain MenuItems
/// with no registrable abstraction to plug a real editor into; building one is out of scope for
/// this milestone, so the editor lives here instead, the same way #20's member editor lives inside
/// CatalogsViewModel rather than as its own panel.
/// </summary>
public partial class TemplatesViewModel(
    TemplateService templateService,
    TemplateSearchService templateSearchService,
    CatalogService catalogService,
    LibraryService libraryService,
    IMediaPlayerLauncher playerLauncher,
    IClipboardService clipboard,
    FFmpegClipExtractor clipExtractor,
    IFilePickerService filePicker) : ViewModelBase
{
    [ObservableProperty]
    private bool _isStatusError;

    [ObservableProperty]
    private string _statusMessage = "No templates yet.";

    partial void OnStatusMessageChanged(string value) => IsStatusError = false;

    private void SetError(string message)
    {
        StatusMessage = message;
        IsStatusError = true;
    }

    public ObservableCollection<TemplateRowViewModel> Templates { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedTemplate))]
    private TemplateRowViewModel? _selectedTemplate;

    public bool HasSelectedTemplate => SelectedTemplate is not null;

    public ObservableCollection<TemplateVariantRowViewModel> Variants { get; } = [];

    public IReadOnlyList<SearchMode> Modes { get; } = Enum.GetValues<SearchMode>();

    private bool _suppressModeChangePersist;

    [ObservableProperty]
    private SearchMode _selectedMode;

    partial void OnSelectedModeChanged(SearchMode value)
    {
        if (!_suppressModeChangePersist && SelectedTemplate is not null)
        {
            _ = SetModeAsync(value);
        }
    }

    public ObservableCollection<CatalogOption> CatalogOptions { get; } = [new CatalogOption(null, "All indexed media")];

    [ObservableProperty]
    private CatalogOption? _selectedCatalogOption;

    partial void OnSelectedCatalogOptionChanged(CatalogOption? value)
    {
        if (SelectedTemplate is not null && value is not null)
        {
            _ = templateService.SetTargetCatalogAsync(SelectedTemplate.Id, value.Id);
        }
    }

    [ObservableProperty]
    private string _newTemplateName = "";

    [ObservableProperty]
    private string _newTemplateDescription = "";

    [ObservableProperty]
    private string _newVariantLabel = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddVariantCommand))]
    private string _newVariantPhonesRaw = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetectionMessage))]
    private string _detectionMessage = "";

    public bool HasDetectionMessage => DetectionMessage.Length > 0;

    /// <summary>Empty once the currently-typed phones parse clean against PhonemeFeatureTable's canonical inventory (#18) - AddVariant refuses to save while this is non-empty, so a template can never be silently unmatchable.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddVariantCommand))]
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    private string _validationMessage = "";

    public bool HasValidationMessage => ValidationMessage.Length > 0;

    private PhoneAlphabet _detectedAlphabet = PhoneAlphabet.Ipa;

    partial void OnNewVariantPhonesRawChanged(string value) => RecomputeDetectionAndValidation(value);

    private void RecomputeDetectionAndValidation(string phonesRaw)
    {
        var symbols = phonesRaw.Split(['|', ' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Where(s => s != "|")
            .ToList();

        if (symbols.Count == 0)
        {
            DetectionMessage = "";
            ValidationMessage = "";
            return;
        }

        var detection = PhoneAlphabetDetector.Detect(symbols);
        if (!detection.IsConfident)
        {
            DetectionMessage = $"Alphabet is ambiguous ({detection.Explanation}) - defaulting to IPA. Say so explicitly if this is ARPABET.";
            _detectedAlphabet = PhoneAlphabet.Ipa;
        }
        else
        {
            DetectionMessage = $"Detected {(detection.Alphabet == PhoneAlphabet.Ipa ? "IPA" : "ARPABET")} ({detection.Explanation})";
            _detectedAlphabet = detection.Alphabet!.Value;
        }

        var parsed = TemplatePhoneParser.ParseSymbols(phonesRaw, _detectedAlphabet);
        var unknown = parsed.Where(p => !p.IsKnown).Select(p => p.AsAuthored).ToList();
        ValidationMessage = unknown.Count == 0
            ? ""
            : $"Unrecognised symbol(s), this variant will never match: {string.Join(' ', unknown)}";
    }

    public ObservableCollection<SearchResultRowViewModel> Results { get; } = [];

    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            var summaries = await templateService.GetAllAsync();
            var previouslySelectedId = SelectedTemplate?.Id;

            Templates.Clear();
            foreach (var summary in summaries)
            {
                Templates.Add(new TemplateRowViewModel(summary));
            }

            var catalogs = await catalogService.GetAllAsync();
            CatalogOptions.Clear();
            CatalogOptions.Add(new CatalogOption(null, "All indexed media"));
            foreach (var catalog in catalogs)
            {
                CatalogOptions.Add(new CatalogOption(catalog.Id, catalog.Name));
            }

            SelectedTemplate = Templates.FirstOrDefault(t => t.Id == previouslySelectedId);
            StatusMessage = Templates.Count > 0 ? $"{Templates.Count} template(s)." : "No templates yet.";
        }
        catch (Exception ex)
        {
            SetError($"Failed to load templates: {ex.Message}");
        }
    }

    partial void OnSelectedTemplateChanged(TemplateRowViewModel? value) => _ = LoadVariantsAsync();

    private async Task LoadVariantsAsync()
    {
        Variants.Clear();
        Results.Clear();
        NewVariantLabel = "";
        NewVariantPhonesRaw = "";

        if (SelectedTemplate is null)
        {
            SelectedCatalogOption = null;
            return;
        }

        SelectedCatalogOption = CatalogOptions.FirstOrDefault(o => o.Id == SelectedTemplate.TargetCatalogId)
            ?? CatalogOptions[0];

        _suppressModeChangePersist = true;
        SelectedMode = SelectedTemplate.Mode;
        _suppressModeChangePersist = false;

        var summaries = await templateService.GetAllAsync();
        var summary = summaries.FirstOrDefault(t => t.Id == SelectedTemplate.Id);
        if (summary is null)
        {
            return;
        }

        foreach (var variant in summary.Variants)
        {
            Variants.Add(new TemplateVariantRowViewModel(variant));
        }
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        var name = NewTemplateName.Trim();
        if (name.Length == 0)
        {
            SetError("Enter a name for the template.");
            return;
        }

        var description = NewTemplateDescription.Trim();
        await templateService.CreateAsync(name, description.Length == 0 ? null : description);

        NewTemplateName = "";
        NewTemplateDescription = "";
        await LoadAsync();
        StatusMessage = $"Created template \"{name}\".";
    }

    [RelayCommand]
    private async Task DeleteAsync(TemplateRowViewModel template)
    {
        if (!template.IsPendingDelete)
        {
            template.IsPendingDelete = true;
            return;
        }

        await templateService.DeleteAsync(template.Id);
        if (SelectedTemplate?.Id == template.Id)
        {
            SelectedTemplate = null;
        }

        await LoadAsync();
        StatusMessage = $"Deleted template \"{template.Name}\".";
    }

    [RelayCommand]
    private void CancelDelete(TemplateRowViewModel template) => template.IsPendingDelete = false;

    [RelayCommand]
    private void BeginRename(TemplateRowViewModel template)
    {
        template.EditName = template.Name;
        template.EditDescription = template.Description ?? "";
        template.IsEditing = true;
    }

    [RelayCommand]
    private void CancelRename(TemplateRowViewModel template) => template.IsEditing = false;

    [RelayCommand]
    private async Task SaveRenameAsync(TemplateRowViewModel template)
    {
        var name = template.EditName.Trim();
        if (name.Length == 0)
        {
            SetError("Enter a name for the template.");
            return;
        }

        var description = template.EditDescription.Trim();
        await templateService.RenameAsync(template.Id, name, description.Length == 0 ? null : description);
        await LoadAsync();
        StatusMessage = $"Renamed template to \"{name}\".";
    }

    private async Task SetModeAsync(SearchMode mode)
    {
        if (SelectedTemplate is null)
        {
            return;
        }

        await templateService.SetModeAsync(SelectedTemplate.Id, mode);
        await LoadAsync();
    }

    private bool CanAddVariant() => ValidationMessage.Length == 0 && NewVariantPhonesRaw.Trim().Length > 0;

    [RelayCommand(CanExecute = nameof(CanAddVariant))]
    private async Task AddVariantAsync()
    {
        if (SelectedTemplate is null)
        {
            return;
        }

        var label = NewVariantLabel.Trim();
        await templateService.AddVariantAsync(
            SelectedTemplate.Id,
            label.Length == 0 ? $"Variant {Variants.Count + 1}" : label,
            NewVariantPhonesRaw.Trim(),
            _detectedAlphabet);

        NewVariantLabel = "";
        NewVariantPhonesRaw = "";
        await LoadVariantsAsyncPreservingSelection();
        StatusMessage = "Added variant.";
    }

    [RelayCommand]
    private async Task RemoveVariantAsync(TemplateVariantRowViewModel variant)
    {
        await templateService.RemoveVariantAsync(variant.Id);
        await LoadVariantsAsyncPreservingSelection();
    }

    /// <summary>LoadVariantsAsync clears NewVariant* fields, which is right when switching templates but wrong right after adding/removing a variant within the same template - re-list only.</summary>
    private async Task LoadVariantsAsyncPreservingSelection()
    {
        if (SelectedTemplate is null)
        {
            return;
        }

        Variants.Clear();
        var summaries = await templateService.GetAllAsync();
        var summary = summaries.FirstOrDefault(t => t.Id == SelectedTemplate.Id);
        if (summary is null)
        {
            return;
        }

        foreach (var variant in summary.Variants)
        {
            Variants.Add(new TemplateVariantRowViewModel(variant));
        }
    }

    [RelayCommand]
    private async Task RunAsync(TemplateRowViewModel template)
    {
        Results.Clear();
        StatusMessage = "Searching...";

        try
        {
            var results = await templateSearchService.SearchAsync(template.Id);
            var mediaPaths = await libraryService.GetPathsAsync(results.Select(r => r.MediaId));

            foreach (var result in results)
            {
                Results.Add(new SearchResultRowViewModel(result, playerLauncher, clipboard, clipExtractor, filePicker)
                {
                    MediaPath = mediaPaths.GetValueOrDefault(result.MediaId),
                });
            }

            StatusMessage = Results.Count > 0 ? $"{Results.Count} result(s)." : "No matches found.";
        }
        catch (Exception ex)
        {
            SetError($"Search failed: {ex.Message}");
        }
    }
}
