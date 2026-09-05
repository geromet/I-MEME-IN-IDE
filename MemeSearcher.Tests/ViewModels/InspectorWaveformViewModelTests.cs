using System.Collections.Generic;
using MemeSearcher.Core.Search;
using MemeSearcher.Infrastructure.Ffmpeg;
using MemeSearcher.Infrastructure.Processes;
using MemeSearcher.Tests.TestDoubles;
using MemeSearcher.ViewModels;

namespace MemeSearcher.Tests.ViewModels;

public class InspectorWaveformViewModelTests
{
    private static FFmpegClipExtractor MakeClipExtractor() => new(new FFmpegToolLocator());

    [Fact]
    public async Task NewerSelection_PreventsStaleWaveformCompletionFromPublishing()
    {
        var stale = new TaskCompletionSource<WaveformSampleResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inspector = new InspectorViewModel(
            new FakeMediaPlayerLauncher(),
            (path, start, end, cancellationToken) => path.EndsWith("first.mp4", StringComparison.Ordinal)
                ? stale.Task
                : Task.FromResult(Success([0.5], start, end)));

        inspector.Show(MakeRow("/media/first.mp4", 1, 2));
        Assert.True(inspector.Waveform.IsLoading);

        inspector.Show(MakeRow("/media/second.mp4", 5, 6));
        Assert.Single(inspector.Waveform.Bars);
        var newerHeight = inspector.Waveform.Bars[0].Height;

        stale.SetResult(Success([1.0], 1, 2));
        await Task.Yield();
        await Task.Yield();

        Assert.Single(inspector.Waveform.Bars);
        Assert.Equal(newerHeight, inspector.Waveform.Bars[0].Height);
        Assert.Equal(24, newerHeight, 6);
    }

    [Fact]
    public void MissingMedia_ReportsUnavailableWithoutInvokingSampler()
    {
        var calls = 0;
        var inspector = new InspectorViewModel(
            new FakeMediaPlayerLauncher(),
            (path, start, end, cancellationToken) =>
            {
                calls++;
                return Task.FromResult(Success([1], start, end));
            });

        inspector.Show(MakeRow(null, 1, 2));

        Assert.Equal(0, calls);
        Assert.False(inspector.Waveform.HasBars);
        Assert.Contains("media file or timing is missing", inspector.Waveform.Status);
    }

    [Fact]
    public void CompositeSelection_LoadsEachComponentsOwnMediaInterval()
    {
        var calls = new List<(string Path, double Start, double End)>();
        var inspector = new InspectorViewModel(
            new FakeMediaPlayerLauncher(),
            (path, start, end, cancellationToken) =>
            {
                calls.Add((path, start, end));
                return Task.FromResult(Success([0.25, 0.75], start, end));
            });

        inspector.ShowComposite(MakeCompositeRow());

        Assert.Equal(2, calls.Count);
        Assert.Equal(("/media/a.mp4", 1.0, 1.4), calls[0]);
        Assert.Equal(("/media/b.mp4", 8.0, 8.5), calls[1]);
        Assert.All(inspector.CompositeComponents, component => Assert.Equal(2, component.Waveform.Bars.Count));
    }

    private static WaveformSampleResult Success(IReadOnlyList<double> amplitudes, double start, double end) =>
        new(true, amplitudes, Math.Max(0, start - WaveformSampler.PaddingSeconds), end + WaveformSampler.PaddingSeconds, start, end, null);

    private static SearchResultRowViewModel MakeRow(string? mediaPath, double start, double end)
    {
        var result = new SearchResult(
            Guid.NewGuid(), start, end, "hello", "hɛloʊ", ["h"], ["h"], 0.9,
            [new MatchedPhone("h", start, end, true)], [], 0, 1);

        return new SearchResultRowViewModel(
            result,
            new FakeMediaPlayerLauncher(),
            new FakeClipboardService(),
            MakeClipExtractor(),
            new FakeFilePickerService())
        {
            MediaPath = mediaPath,
        };
    }

    private static CompositeSearchResultRowViewModel MakeCompositeRow()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var result = new CompositeSearchResult(
            0.88,
            [
                new CompositeMatchComponent(
                    firstId, 1.0, 1.4, "super", "suːpər", ["s"], 0.9, 0, 1,
                    [new MatchedPhone("s", 1.0, 1.2, true)]),
                new CompositeMatchComponent(
                    secondId, 8.0, 8.5, "man", "mæn", ["m"], 0.85, 1, 2,
                    [new MatchedPhone("m", 8.0, 8.2, true)]),
            ],
            ["s", "m"]);

        return new CompositeSearchResultRowViewModel(
            result,
            new Dictionary<Guid, string> { [firstId] = "Source A", [secondId] = "Source B" },
            new Dictionary<Guid, string> { [firstId] = "/media/a.mp4", [secondId] = "/media/b.mp4" },
            MakeClipExtractor(),
            new FakeFilePickerService());
    }
}
