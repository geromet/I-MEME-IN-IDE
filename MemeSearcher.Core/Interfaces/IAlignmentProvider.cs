namespace MemeSearcher.Core.Interfaces;

public record AlignedWord(string Text, double StartSeconds, double EndSeconds);

/// <summary>Phone-level timing (Milestone 6) - optional because not every alignment provider produces it (WhisperX doesn't; MFA does).</summary>
public record AlignedPhone(string Symbol, double StartSeconds, double EndSeconds);

public record AlignmentResult(IReadOnlyList<AlignedWord> Words, IReadOnlyList<AlignedPhone>? Phones = null);

public interface IAlignmentProvider
{
    string ProviderName { get; }

    Task<AlignmentResult> AlignAsync(string mediaPath, string transcriptText, CancellationToken cancellationToken = default);
}
