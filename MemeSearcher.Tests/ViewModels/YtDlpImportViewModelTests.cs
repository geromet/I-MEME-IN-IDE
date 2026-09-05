using MemeSearcher.Core.Jobs;
using MemeSearcher.Core.Models;
using MemeSearcher.Infrastructure.YtDlp;
using MemeSearcher.ViewModels;

namespace MemeSearcher.Tests.ViewModels;

public class YtDlpImportViewModelTests
{
    private sealed class RecordingJobQueue : IJobQueue
    {
        private readonly List<Job> _jobs = [];

        public IReadOnlyList<Job> Jobs => _jobs;

        public event EventHandler? Changed;

        public JobKind? EnqueuedKind { get; private set; }

        public Func<IProgress<string>, CancellationToken, Task>? EnqueuedWork { get; private set; }

        public Job Enqueue(JobKind kind, string title, Func<IProgress<string>, CancellationToken, Task> work)
        {
            var job = new Job(Guid.NewGuid(), kind, title);
            _jobs.Add(job);
            EnqueuedKind = kind;
            EnqueuedWork = work;
            Changed?.Invoke(this, EventArgs.Empty);
            return job;
        }

        public void Cancel(Guid jobId)
        {
            _jobs.FirstOrDefault(job => job.Id == jobId)?.CancellationTokenSource.Cancel();
        }
    }

    private static YtDlpImportPlan BuildPlan() => new(
    [
        new(new YtDlpVideoEntry("new-1", "New one", "Channel", "https://example.test/new-1"), YtDlpImportPlanStatus.New),
        new(new YtDlpVideoEntry("old-1", "Imported", "Channel", "https://example.test/old-1"), YtDlpImportPlanStatus.AlreadyImported),
        new(new YtDlpVideoEntry("failed-1", "Failed before", "Channel", "https://example.test/failed-1"), YtDlpImportPlanStatus.PreviouslyFailed),
        new(new YtDlpVideoEntry("new-2", "New two", "Channel", "https://example.test/new-2"), YtDlpImportPlanStatus.New),
    ]);

    private static YtDlpImportViewModel CreateViewModel(
        RecordingJobQueue queue,
        YtDlpImportPlan plan,
        Action? importCalled = null,
        Action<CancellationToken>? importToken = null) =>
        new(
            (_, _) => Task.FromResult(plan),
            (_, _, _, cancellationToken) =>
            {
                importCalled?.Invoke();
                importToken?.Invoke(cancellationToken);
                return Task.FromResult(new YtDlpImportSummary(plan.NewCount, 0));
            },
            queue,
            () => "en-US",
            () => YtDlpMediaKind.Audio,
            () => "/configured/ytdlp");

    [Fact]
    public async Task ReviewAsync_ShowsPlannerCountsAndNewItemsWithoutEnqueueingWork()
    {
        var queue = new RecordingJobQueue();
        var importCalls = 0;
        var viewModel = CreateViewModel(queue, BuildPlan(), () => importCalls++);
        viewModel.SourceUrl = "https://example.test/playlist";

        await viewModel.ReviewCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasPlan);
        Assert.Equal(4, viewModel.TotalCount);
        Assert.Equal(2, viewModel.NewCount);
        Assert.Equal(1, viewModel.AlreadyImportedCount);
        Assert.Equal(1, viewModel.PreviouslyFailedCount);
        Assert.Equal(["new-1", "new-2"], viewModel.NewItems.Select(item => item.Entry.VideoId));
        Assert.Equal("Audio only", viewModel.MediaKindDisplay);
        Assert.Equal("/configured/ytdlp", viewModel.DownloadLocationDisplay);
        Assert.Empty(queue.Jobs);
        Assert.Equal(0, importCalls);
    }

    [Fact]
    public async Task CancelReview_BeforeConfirmation_PerformsNoImportWork()
    {
        var queue = new RecordingJobQueue();
        var importCalls = 0;
        var viewModel = CreateViewModel(queue, BuildPlan(), () => importCalls++);
        viewModel.SourceUrl = "https://example.test/playlist";
        await viewModel.ReviewCommand.ExecuteAsync(null);

        viewModel.CancelReviewCommand.Execute(null);

        Assert.False(viewModel.HasPlan);
        Assert.Empty(queue.Jobs);
        Assert.Equal(0, importCalls);
        Assert.Contains("No download was started", viewModel.StatusMessage);
    }

    [Fact]
    public async Task ConfirmImport_EnqueuesOneExistingYtDlpJobAndDelegatesTheReviewedPlan()
    {
        var queue = new RecordingJobQueue();
        var plan = BuildPlan();
        YtDlpImportPlan? importedPlan = null;
        string? importedLanguage = null;
        var viewModel = new YtDlpImportViewModel(
            (_, _) => Task.FromResult(plan),
            (receivedPlan, language, _, _) =>
            {
                importedPlan = receivedPlan;
                importedLanguage = language;
                return Task.FromResult(new YtDlpImportSummary(receivedPlan.NewCount, 0));
            },
            queue,
            () => "nl-NL",
            () => YtDlpMediaKind.Video,
            () => "/configured/video");
        viewModel.SourceUrl = "https://example.test/channel";
        await viewModel.ReviewCommand.ExecuteAsync(null);

        viewModel.ConfirmImportCommand.Execute(null);

        var job = Assert.Single(queue.Jobs);
        Assert.Equal(JobKind.YtDlpImport, job.Kind);
        Assert.Equal(JobKind.YtDlpImport, queue.EnqueuedKind);
        Assert.NotNull(queue.EnqueuedWork);
        Assert.False(viewModel.HasPlan); // a second click cannot enqueue the same reviewed plan twice

        await queue.EnqueuedWork!(new Progress<string>(), CancellationToken.None);

        Assert.Same(plan, importedPlan);
        Assert.Equal("nl-NL", importedLanguage);
    }

    [Fact]
    public async Task QueuedImport_PassesTheQueueCancellationTokenToTheExistingOrchestratorPath()
    {
        var queue = new RecordingJobQueue();
        CancellationToken observedToken = default;
        var viewModel = CreateViewModel(queue, BuildPlan(), importToken: token => observedToken = token);
        viewModel.SourceUrl = "https://example.test/channel";
        await viewModel.ReviewCommand.ExecuteAsync(null);
        viewModel.ConfirmImportCommand.Execute(null);

        using var cancellation = new CancellationTokenSource();
        Assert.NotNull(queue.EnqueuedWork);
        await queue.EnqueuedWork!(new Progress<string>(), cancellation.Token);

        Assert.Equal(cancellation.Token, observedToken);
    }
}
