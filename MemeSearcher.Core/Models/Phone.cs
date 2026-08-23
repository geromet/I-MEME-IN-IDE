using MemeSearcher.Core.Phonetics;

namespace MemeSearcher.Core.Models;

public class Phone
{
    public Guid Id { get; set; }
    public Guid WordId { get; set; }
    public int Sequence { get; set; }
    /// <summary>
    /// The symbol exactly as the alignment provider wrote it, in <see cref="Alphabet"/> - not
    /// converted on write. Keeping the native form means a mistake in the conversion table is
    /// fixed by a code change and a reindex, rather than by re-running alignment against source
    /// media that may no longer exist (#18).
    /// </summary>
    public required string Symbol { get; set; }

    /// <summary>Which alphabet <see cref="Symbol"/> is written in. MFA writes ARPABET here.</summary>
    public PhoneAlphabet Alphabet { get; set; } = PhoneAlphabet.Ipa;
    public double? StartSeconds { get; set; }
    public double? EndSeconds { get; set; }
}
