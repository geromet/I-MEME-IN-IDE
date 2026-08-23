namespace MemeSearcher.Core.Languages;

/// <summary>
/// One user-selectable language, plus the per-tool code each external tool actually wants.
///
/// These codes are NOT interchangeable and that is the whole reason this type exists (#23): a
/// single "language" string was previously passed to both espeak-ng and whisperx, and the two use
/// incompatible code spaces. espeak-ng takes *voice names*, where the region suffix is meaningful
/// - en-us and en-gb are different voices producing genuinely different IPA. whisperx takes
/// ISO 639-1 two-letter codes and rejects region-qualified tags outright (argparse `choices`), so
/// "en-US" fails the run before any audio is read.
///
/// <see cref="Id"/> is the neutral identifier: it is what the UI selects, what is stored in the
/// database, and what is passed across every interface boundary. Tool-specific codes are derived
/// from it at the point of invocation and never stored, so a correction to a mapping is a code
/// change rather than a data migration.
/// </summary>
/// <param name="Id">Neutral, stable identifier (BCP-47 shaped). Stored on Media/Transcript/history rows.</param>
/// <param name="DisplayName">Human-readable name for selection UI.</param>
/// <param name="EspeakVoice">Voice name for `espeak-ng -v`.</param>
/// <param name="WhisperCode">ISO 639-1 code for `whisperx --language`.</param>
public record LanguageOption(string Id, string DisplayName, string EspeakVoice, string WhisperCode);
