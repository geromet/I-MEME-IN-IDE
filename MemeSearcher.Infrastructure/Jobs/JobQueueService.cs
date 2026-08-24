using MemeSearcher.Core.Jobs;

namespace MemeSearcher.Infrastructure.Jobs;

/// <summary>
/// Runs enqueued work sequentially by default (#14: "multiple queued imports process
/// sequentially") via a <see cref="SemaphoreSlim"/> concurrency gate. A job cancelled while still
/// queued (before it acquires the gate) never runs at all - the wait on the gate observes the same
/// token the work itself would have received.
/// </summary>
public class JobQueueService(int maxConcurrency = 1) : IJobQueue
{
    private readonly List<Job> _jobs = [];
    private readonly object _gate = new();
    private readonly SemaphoreSlim _concurrencyGate = new(maxConcurrency, maxConcurrency);

    public IReadOnlyList<Job> Jobs
    {
        get
        {
            lock (_gate)
            {
                return _jobs.ToList();
            }
        }
    }

    public event EventHandler? Changed;

    public Job Enqueue(JobKind kind, string title, Func<IProgress<string>, CancellationToken, Task> work)
    {
        var job = new Job(Guid.NewGuid(), kind, title);

        lock (_gate)
        {
            _jobs.Add(job);
        }

        RaiseChanged();

        _ = RunAsync(job, work);

        return job;
    }

    public void Cancel(Guid jobId)
    {
        Job? job;
        lock (_gate)
        {
            job = _jobs.FirstOrDefault(j => j.Id == jobId);
        }

        job?.CancellationTokenSource.Cancel();
    }

    private async Task RunAsync(Job job, Func<IProgress<string>, CancellationToken, Task> work)
    {
        var token = job.CancellationTokenSource.Token;

        try
        {
            await _concurrencyGate.WaitAsync(token);
        }
        catch (OperationCanceledException)
        {
            job.State = JobState.Cancelled;
            RaiseChanged();
            return;
        }

        try
        {
            job.State = JobState.Running;
            RaiseChanged();

            var progress = new Progress<string>(message =>
            {
                job.StatusMessage = message;
                RaiseChanged();
            });

            await work(progress, token);

            job.State = JobState.Succeeded;
        }
        catch (OperationCanceledException)
        {
            job.State = JobState.Cancelled;
        }
        catch (Exception ex)
        {
            job.Error = ex.Message;
            job.State = JobState.Failed;
        }
        finally
        {
            _concurrencyGate.Release();
            RaiseChanged();
        }
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
