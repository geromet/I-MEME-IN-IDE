using System;
using CommunityToolkit.Mvvm.ComponentModel;
using MemeSearcher.Core.Phonetics;
using MemeSearcher.Infrastructure.Templates;

namespace MemeSearcher.ViewModels;

public partial class TemplateVariantRowViewModel(TemplateVariantSummary summary) : ObservableObject
{
    public Guid Id { get; } = summary.Id;
    public string Label { get; } = summary.Label;
    public string PhonesRaw { get; } = summary.PhonesRaw;
    public PhoneAlphabet Alphabet { get; } = summary.Alphabet;
    public string AlphabetDisplay { get; } = summary.Alphabet == PhoneAlphabet.Ipa ? "IPA" : "ARPABET";

    [ObservableProperty]
    private bool _isPendingDelete;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _editLabel = summary.Label;

    [ObservableProperty]
    private string _editPhonesRaw = summary.PhonesRaw;

    public void ResetEditor()
    {
        EditLabel = Label;
        EditPhonesRaw = PhonesRaw;
    }
}
