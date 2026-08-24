namespace MemeSearcher.Core.Jobs;

/// <summary>
/// Runs import/realign/reindex as observable, cancellable background jobs (#14). Concurrency is
/// limited so queuing several jobs doesn't launch them all at once - see
/// <c>JobQueueService</c>'s constructor parameter.
/// </summary>
public interface IJobQueue
{
    /// <summary>Snapshot of every job the queue knows about, oldest first. Never null, never mutated in place by the caller.</summary>
    IReadOnlyList<Job> Jobs { get; }

    /// <summary>
    /// Raised whenever any job's state changes (queued, started, progressed, finished). Coarse by
    /// design - subscribers rebuild their view from <see cref="Jobs"/> rather than diffing.
    /// Handlers may be invoked from a background thread; a UI subscriber must marshal to its own
    /// thread before touching UI-bound state.
    /// </summary>
    event EventHandler? Changed;

    /// <summary>
    /// Adds a job to the queue and returns immediately; the work runs when a concurrency slot is
    /// free. <paramref name="work"/> receives an <see cref="IProgress{T}"/> for status messages and
    /// the job's own cancellation token, which it must pass through to whatever it awaits.
    /// </summary>
    Job Enqueue(JobKind kind, string title, Func<IProgress<string>, CancellationToken, Task> work);

    /// <summary>Requests cancellation of a queued or running job. No-op if the job is already finished or unknown.</summary>
    void Cancel(Guid jobId);
}
