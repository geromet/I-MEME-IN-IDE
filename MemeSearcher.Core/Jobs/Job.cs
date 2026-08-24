namespace MemeSearcher.Core.Jobs;

/// <summary>
/// A queued unit of background work (#14). Deliberately a plain mutable class rather than an
/// ObservableObject: the queue notifies interested parties with one coarse <c>Changed</c> event
/// (see <see cref="IJobQueue"/>) and callers rebuild whatever view they need from
/// <see cref="IJobQueue.Jobs"/> - the same "rebuild-everything" pattern
/// <c>LibraryViewModel.LoadAsync</c> already uses - rather than each Job raising its own
/// per-property notifications across threads.
/// </summary>
public class Job(Guid id, JobKind kind, string title)
{
    public Guid Id { get; } = id;

    public JobKind Kind { get; } = kind;

    public string Title { get; } = title;

    public JobState State { get; set; } = JobState.Queued;

    public string? StatusMessage { get; set; }

    /// <summary>
    /// Set only on Failed. Left readable indefinitely - a failed job's row keeps its error even
    /// after later jobs run and complete, unlike the single StatusMessage bar it replaces for
    /// these three operations (#14's "error remains readable" exit criterion).
    /// </summary>
    public string? Error { get; set; }

    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;

    public CancellationTokenSource CancellationTokenSource { get; } = new();

    public bool IsCancellable => State is JobState.Queued or JobState.Running;
}
