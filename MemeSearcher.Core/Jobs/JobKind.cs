namespace MemeSearcher.Core.Jobs;

/// <summary>What a queued job actually does (#14) - the three operations addendum §27/§28 and
/// handoff §28 call out as needing observable state and real cancellation.</summary>
public enum JobKind
{
    Import,
    Realign,
    Reindex,
}
