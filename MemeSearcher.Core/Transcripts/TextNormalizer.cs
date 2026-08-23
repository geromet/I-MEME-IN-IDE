using System.Text;

namespace MemeSearcher.Core.Transcripts;

/// <summary>
/// Normalization applied before phonemization/search, kept separate from phonemization itself
/// so the choice of normalization rules can change without touching the phonemizer (handoff §29:
/// "normalization version" is tracked independently of "phonemizer version").
/// </summary>
public static class TextNormalizer
{
    public static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        var lastWasSpace = false;

        foreach (var c in text.Trim())
        {
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                lastWasSpace = true;
                continue;
            }

            if (char.IsLetterOrDigit(c) || c == '\'')
            {
                builder.Append(char.ToLowerInvariant(c));
                lastWasSpace = false;
            }
        }

        return builder.ToString().TrimEnd();
    }

    public static string[] Tokenize(string normalizedText) =>
        normalizedText.Length == 0
            ? []
            : normalizedText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
}
