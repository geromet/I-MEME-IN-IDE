using MemeSearcher.Core.Jobs;
using MemeSearcher.Infrastructure.Jobs;

namespace MemeSearcher.Tests.Jobs;

/// <summary>
/// Unit-level proof of #14's job-queue semantics, independent of any real external process (the
/// process-kill guarantee itself is proven separately in ProcessRunnerTests against real ffmpeg).
/// </summary>
public class JobQueueServiceTests
{
    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var elapsed = 0;
        while (!condition() && elapsed < timeoutMs)
        {
            await Task.Delay(10);
            elapsed += 10;
        }
    }

    [Fact]
    public async Task Enqueue_RunsTheJobAndReportsSucceeded()
    {
        var queue = new JobQueueService();

        var job = queue.Enqueue(JobKind.Reindex, "Rebuild index", async (progress, ct) =>
        {
            progress.Report("Working...");
            await Task.Delay(10, ct);
        });

        await WaitUntilAsync(() => job.State == JobState.Succeeded);

        Assert.Equal(JobState.Succeeded, job.State);
    }

    /// <summary>#14 exit criterion: "multiple queued imports process sequentially ... with visible per-job state."</summary>
    [Fact]
    public async Task TwoJobs_WithConcurrencyOne_NeverRunAtTheSameTime()
    {
        var queue = new JobQueueService(maxConcurrency: 1);
        var overlapDetected = false;
        var running = 0;

        Func<IProgress<string>, CancellationToken, Task> work = async (_, ct) =>
        {
            if (Interlocked.Increment(ref running) > 1)
            {
                overlapDetected = true;
            }

            await Task.Delay(50, ct);
            Interlocked.Decrement(ref running);
        };

        var first = queue.Enqueue(JobKind.Import, "a.srt", work);
        var second = queue.Enqueue(JobKind.Import, "b.srt", work);

        await WaitUntilAsync(() => first.State == JobState.Succeeded && second.State == JobState.Succeeded);

        Assert.False(overlapDetected, "Two jobs ran concurrently despite maxConcurrency: 1.");
        Assert.Equal(JobState.Succeeded, first.State);
        Assert.Equal(JobState.Succeeded, second.State);
    }

    /// <summary>#14 exit criterion: "a failed job's error remains readable after subsequent jobs run."</summary>
    [Fact]
    public async Task FailedJobsError_SurvivesLaterJobsCompleting()
    {
        var queue = new JobQueueService();

        var failing = queue.Enqueue(JobKind.Import, "bad.srt", (_, _) =>
            throw new InvalidOperationException("Malformed transcript."));

        await WaitUntilAsync(() => failing.State == JobState.Failed);
        Assert.Equal("Malformed transcript.", failing.Error);

        var later = queue.Enqueue(JobKind.Import, "good.srt", async (_, ct) => await Task.Delay(10, ct));
        await WaitUntilAsync(() => later.State == JobState.Succeeded);

        Assert.Equal(JobState.Failed, failing.State);
        Assert.Equal("Malformed transcript.", failing.Error);
    }

    [Fact]
    public async Task Cancel_WhileRunning_StopsTheJobAsCancelled()
    {
        var queue = new JobQueueService();
        var started = new TaskCompletionSource();

        var job = queue.Enqueue(JobKind.Realign, "clip.mp4", async (_, ct) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
        });

        await started.Task;
        queue.Cancel(job.Id);

        await WaitUntilAsync(() => job.State == JobState.Cancelled);
        Assert.Equal(JobState.Cancelled, job.State);
    }

    /// <summary>A job cancelled before it ever acquires a concurrency slot must never run its work at all.</summary>
    [Fact]
    public async Task Cancel_WhileStillQueued_NeverRunsTheWork()
    {
        var queue = new JobQueueService(maxConcurrency: 1);
        var blocker = new TaskCompletionSource();

        var occupying = queue.Enqueue(JobKind.Import, "a.srt", async (_, ct) =>
        {
            await blocker.Task.WaitAsync(ct);
        });

        var ran = false;
        var queued = queue.Enqueue(JobKind.Import, "b.srt", (_, _) =>
        {
            ran = true;
            return Task.CompletedTask;
        });

        // occupying holds the only concurrency slot, so queued is still waiting on it - cancel it
        // in that state, before it has ever run. Wait for the cancellation to actually land before
        // freeing the slot, so releasing it can't race the cancellation into handing queued the
        // slot anyway.
        Assert.Equal(JobState.Queued, queued.State);
        queue.Cancel(queued.Id);
        await WaitUntilAsync(() => queued.State == JobState.Cancelled);

        blocker.SetResult();
        await WaitUntilAsync(() => occupying.State == JobState.Succeeded);

        Assert.False(ran, "A job cancelled before its turn must not have executed its work.");
        Assert.Equal(JobState.Cancelled, queued.State);
    }

    [Fact]
    public async Task Changed_FiresOnEnqueueAndOnCompletion()
    {
        var queue = new JobQueueService();
        var changeCount = 0;
        queue.Changed += (_, _) => Interlocked.Increment(ref changeCount);

        var job = queue.Enqueue(JobKind.Reindex, "Rebuild index", async (_, ct) => await Task.Delay(10, ct));
        await WaitUntilAsync(() => job.State == JobState.Succeeded);

        Assert.True(changeCount >= 3, $"Expected at least enqueue+running+succeeded change notifications, got {changeCount}.");
    }
}
