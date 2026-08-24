using MemeSearcher.Core.Jobs;
using MemeSearcher.Infrastructure.Jobs;
using MemeSearcher.ViewModels;

namespace MemeSearcher.Tests.ViewModels;

/// <summary>
/// #14: the Jobs panel is a thin, coarsely-rebuilt view over IJobQueue - this proves the view model
/// actually reflects the queue's state (including after a real cancel) rather than just wrapping it
/// opaquely.
/// </summary>
public class JobsPanelViewModelTests
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
    public async Task Rebuilds_WhenTheQueueChanges()
    {
        var queue = new JobQueueService();
        var panel = new JobsPanelViewModel(queue);

        Assert.False(panel.HasJobs);

        var job = queue.Enqueue(JobKind.Import, "clip.srt", async (_, ct) => await Task.Delay(20, ct));

        await WaitUntilAsync(() => panel.Jobs.Any(j => j.Id == job.Id && j.State == JobState.Succeeded));

        var row = Assert.Single(panel.Jobs);
        Assert.Equal(JobState.Succeeded, row.State);
        Assert.True(panel.HasJobs);
    }

    /// <summary>The Cancel button's command is what a real click invokes - going through it (not queue.Cancel directly) proves the wiring, not just the underlying queue behaviour.</summary>
    [Fact]
    public async Task CancelCommand_StopsARunningJob()
    {
        var queue = new JobQueueService();
        var panel = new JobsPanelViewModel(queue);
        var started = new TaskCompletionSource();

        var job = queue.Enqueue(JobKind.Realign, "clip.mp4", async (_, ct) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
        });

        await started.Task;
        await WaitUntilAsync(() => panel.Jobs.Any(j => j.Id == job.Id && j.State == JobState.Running));

        var row = panel.Jobs.Single(j => j.Id == job.Id);
        Assert.True(row.IsCancellable);

        panel.CancelCommand.Execute(row);

        await WaitUntilAsync(() => panel.Jobs.Any(j => j.Id == job.Id && j.State == JobState.Cancelled));

        var updatedRow = panel.Jobs.Single(j => j.Id == job.Id);
        Assert.Equal(JobState.Cancelled, updatedRow.State);
        Assert.False(updatedRow.IsCancellable);
    }
}
