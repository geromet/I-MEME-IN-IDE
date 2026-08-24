using System.Text.Json;

namespace MemeSearcher.Infrastructure.Templates;

/// <summary>
/// A small shipped set of pre-set templates (#21's "chosen to demonstrate the point - patterns
/// text search genuinely cannot express"), delivered through the same TemplateExportFile format
/// and TemplateImportExportService.ImportAsync path a user-shared file would use, rather than a
/// separate seeding mechanism - one less thing to keep correct. All phones are drawn from
/// PhonemeFeatureTable's en-US inventory, so "Run" never trips the unknown-symbol validation on a
/// template we shipped.
///
/// Only "Stutter" actually demonstrates the milestone's central claim (a tripled leading
/// consonant has no English spelling to phonemize from at all). The laugh/scream ones are
/// starting points for hand-tuning, not proof that text search can't reach them -
/// TemplateSearchServiceTests's own criterion-1 finding showed SimilarPhonetic's default fuzzy
/// tolerance bridges even large phone mismatches, so a repeated real word/vowel is not reliably
/// out of text search's reach the way an unspellable consonant cluster is. Their descriptions say
/// so rather than overclaiming.
/// </summary>
public static class StarterTemplates
{
    public static string BuildExportJson()
    {
        var file = new TemplateExportFile([
            new TemplateExportEntry(
                "Stutter (b-b-boy)",
                "A repeated onset consonant before the real word - \"b-b-boy\" isn't a spelling anyone would type into a text search, and unlike a repeated word or vowel, a tripled leading consonant has no plausible English spelling to phonemize from in the first place.",
                "SimilarPhonetic",
                null,
                [
                    new TemplateExportVariant("Triple", "b b b ɔɪ", "Ipa"),
                ]),
            new TemplateExportEntry(
                "Drawn-out laugh",
                "A held \"ha\" repeated with no real word boundary between the repeats - a starting point for a template you'd tune (e.g. more repeats, or a longer held vowel) until it matches your specific clip and a plain text query for \"haha\" doesn't.",
                "SimilarPhonetic",
                null,
                [
                    new TemplateExportVariant("US-ish", "h æ h æ h æ", "Ipa"),
                    new TemplateExportVariant("Open-mouthed", "h ɑ h ɑ h ɑ", "Ipa"),
                ]),
            new TemplateExportEntry(
                "Wordless scream",
                "A held vowel with no consonant and no word around it - a starting point for hand-tuning to a specific scream's actual duration/vowel quality rather than a claim that this exact sequence is unreachable by text.",
                "LoosePhonetic",
                null,
                [
                    new TemplateExportVariant("Open back", "ɑ ɑ ɑ ɑ", "Ipa"),
                    new TemplateExportVariant("Open front", "æ æ æ æ", "Ipa"),
                ]),
        ]);

        return JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true });
    }
}
