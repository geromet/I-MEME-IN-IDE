namespace MemeSearcher.Core.Languages;

/// <summary>
/// Thrown when a language identifier does not resolve to a <see cref="LanguageOption"/>. Carries
/// the supported set so the message is actionable rather than "invalid language".
/// </summary>
public class UnsupportedLanguageException(string languageId, IEnumerable<string> supportedIds)
    : InvalidOperationException(
        $"'{languageId}' is not a supported language. Supported: {string.Join(", ", supportedIds)}.")
{
    public string LanguageId { get; } = languageId;
}

/// <summary>
/// The set of languages the app offers, and the single place where a neutral language id is
/// turned into a tool-specific code.
///
/// Deliberately a curated list rather than the full espeak-ng inventory (142 voices) or the full
/// whisper inventory (~99 languages): a language is only usable here if *both* tools support it,
/// since transcription and phonemization are two halves of one pipeline. Growing the list means
/// checking both sides, which is exactly the check a dynamically-enumerated list would skip.
/// Every entry below was verified against the installed espeak-ng voices and whisperx's own
/// `--language` choices.
/// </summary>
public static class LanguageCatalog
{
    public static IReadOnlyList<LanguageOption> All { get; } =
    [
        new("en-US", "English (US)",        "en-us", "en"),
        new("en-GB", "English (UK)",        "en-gb", "en"),
        new("nl",    "Dutch",               "nl",    "nl"),
        new("de",    "German",              "de",    "de"),
        new("fr-FR", "French",              "fr-fr", "fr"),
        new("es",    "Spanish",             "es",    "es"),
        new("it",    "Italian",             "it",    "it"),
        new("pt",    "Portuguese (Europe)", "pt",    "pt"),
        new("ru",    "Russian",             "ru",    "ru"),
        new("ja",    "Japanese",            "ja",    "ja"),
    ];

    /// <summary>
    /// The language assumed when nothing has been chosen. A single shared default so the UI can
    /// no longer drift from itself the way SearchViewModel and LibraryViewModel each holding
    /// their own "en-US" constant allowed.
    /// </summary>
    public static LanguageOption Default { get; } = All[0];

    private static readonly Dictionary<string, LanguageOption> ById =
        All.ToDictionary(o => o.Id, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyCollection<string> SupportedIds { get; } = All.Select(o => o.Id).ToArray();

    public static bool TryGet(string? languageId, out LanguageOption option)
    {
        option = Default;
        return languageId is not null && ById.TryGetValue(languageId, out option!);
    }

    /// <summary>
    /// Resolves an id, throwing <see cref="UnsupportedLanguageException"/> if it is unknown.
    /// Callers about to spawn an external tool should use this rather than passing the raw string
    /// through: an unsupported id caught here produces a clear message, while the same id passed
    /// to whisperx produces a non-zero exit and a wall of argparse output.
    /// </summary>
    public static LanguageOption Get(string languageId) =>
        TryGet(languageId, out var option)
            ? option
            : throw new UnsupportedLanguageException(languageId, SupportedIds);
}
