using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using MemeSearcher.Core.Phonetics;
using MemeSearcher.Infrastructure.Templates;

namespace MemeSearcher.ViewModels;

public partial class TemplatesViewModel
{
    [RelayCommand]
    private async Task SaveSearchOptionsAsync(TemplateRowViewModel template)
    {
        if (!template.TryBuildSearchOptions(out var options, out var error))
        {
            SetError(error);
            return;
        }

        await templateService.SetSearchOptionsAsync(template.Id, JsonSerializer.Serialize(options));
        template.AcceptSavedSearchOptions(options);
        StatusMessage = $"Saved search options for \"{template.Name}\".";
    }

    [RelayCommand]
    private async Task ResetSearchOptionsAsync(TemplateRowViewModel template)
    {
        await templateService.SetSearchOptionsAsync(template.Id, null);
        template.ResetSearchOptionFields();
        StatusMessage = $"Reset \"{template.Name}\" to the {template.Mode} defaults.";
    }

    [RelayCommand]
    private void BeginVariantEdit(TemplateVariantRowViewModel variant)
    {
        variant.ResetEditor();
        variant.IsEditing = true;
    }

    [RelayCommand]
    private void CancelVariantEdit(TemplateVariantRowViewModel variant)
    {
        variant.ResetEditor();
        variant.IsEditing = false;
    }

    [RelayCommand]
    private async Task SaveVariantEditAsync(TemplateVariantRowViewModel variant)
    {
        var phonesRaw = variant.EditPhonesRaw.Trim();
        if (phonesRaw.Length == 0)
        {
            SetError("A template variant must contain at least one phone.");
            return;
        }

        var parsed = TemplatePhoneParser.ParseSymbols(phonesRaw, variant.Alphabet);
        var unknown = parsed.Where(phone => !phone.IsKnown).Select(phone => phone.AsAuthored).ToList();
        if (unknown.Count > 0)
        {
            SetError($"Unrecognised symbol(s), this variant would never match: {string.Join(' ', unknown)}");
            return;
        }

        var label = variant.EditLabel.Trim();
        if (label.Length == 0)
        {
            SetError("A template variant label cannot be empty when editing in place.");
            return;
        }

        await templateService.UpdateVariantAsync(variant.Id, label, phonesRaw, variant.Alphabet);
        await LoadVariantsAsyncPreservingSelection();
        StatusMessage = $"Updated variant \"{label}\".";
    }
}
