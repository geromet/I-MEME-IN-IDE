namespace MemeSearcher.Core.Jobs;

/// <summary>State of a background job (#14, addendum §27/§28).</summary>
public enum JobState
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Cancelled,
}
