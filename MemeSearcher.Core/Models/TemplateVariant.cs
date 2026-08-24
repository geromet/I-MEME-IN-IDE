using MemeSearcher.Core.Phonetics;

namespace MemeSearcher.Core.Models;

/// <summary>
/// One pronunciation variant of a Template (handoff §33: accents, regional pronunciations, names,
/// slang - never assume one written form maps to one phoneme sequence). A search against the
/// template matches if any variant matches (#21 exit criterion).
/// </summary>
public class TemplateVariant
{
    public Guid Id { get; set; }
    public Guid TemplateId { get; set; }

    public required string Label { get; set; }

    /// <summary>
    /// As authored, in <see cref="Alphabet"/> - never converted-and-discarded (kept native per #18
    /// §4's "store native, derive canonical" for the same reason it applies to corpus phones: a
    /// conversion-table fix must be replayable, not require re-authoring). Phones are
    /// space-separated; "|" marks an optional word-boundary group (PhoneToken.Boundary) between
    /// runs of phones - most templates are a single unbroken sound and need none.
    /// </summary>
    public required string PhonesRaw { get; set; }

    public PhoneAlphabet Alphabet { get; set; } = PhoneAlphabet.Ipa;

    /// <summary>Display/edit order within the template - membership order carries no other meaning.</summary>
    public int Sequence { get; set; }
}
