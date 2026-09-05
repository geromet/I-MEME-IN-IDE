using MemeSearcher.Core.Models;
using MemeSearcher.Infrastructure.Jobs;
using MemeSearcher.Infrastructure.YtDlp;
using MemeSearcher.ViewModels;

namespace MemeSearcher.Tests.ViewModels;

public class YtDlpImportViewModelConcurrencyTests
{
    [Fact]
    public async Task StartingANewerReview_PreventsTheCancelledReviewFromOverwritingItsState()
    {
        var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstCancellation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondPlan = new YtDlpImportPlan(
        [
            new(new YtDlpVideoEntry("second", "Second result", null, "https://example.test/second"), YtDlpImportPlanStatus.New),
        ]);

        async Task<YtDlpImportPlan> PlanAsync(string url, CancellationToken cancellationToken)
        {
            if (url.EndsWith("/first", StringComparison.Ordinal))
            {
                firstStarted.SetResult(true);
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Hold the stale completion until the second review has already published its
                    // state, then prove this catch cannot overwrite the newer status.
                    await releaseFirstCancellation.Task;
                    throw;
                }
            }

            return secondPlan;
        }

        var viewModel = new YtDlpImportViewModel(
            PlanAsync,
            (_, _, _, _) => Task.FromResult(new YtDlpImportSummary(0, 0)),
            new JobQueueService(),
            () => "en-US",
            () => YtDlpMediaKind.Audio,
            () => "/tmp/ytdlp");

        viewModel.SourceUrl = "https://example.test/first";
        var firstReview = viewModel.ReviewCommand.ExecuteAsync(null);
        await firstStarted.Task;

        viewModel.SourceUrl = "https://example.test/second";
        await viewModel.ReviewCommand.ExecuteAsync(null);
        Assert.True(viewModel.HasPlan);
        Assert.Equal("second", Assert.Single(viewModel.NewItems).Entry.VideoId);
        var newerStatus = viewModel.StatusMessage;

        releaseFirstCancellation.SetResult(true);
        await firstReview;

        Assert.True(viewModel.HasPlan);
        Assert.Equal("second", Assert.Single(viewModel.NewItems).Entry.VideoId);
        Assert.Equal(newerStatus, viewModel.StatusMessage);
    }
}
