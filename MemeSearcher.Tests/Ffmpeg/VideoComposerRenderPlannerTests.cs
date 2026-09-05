using MemeSearcher.Infrastructure.Ffmpeg;

namespace MemeSearcher.Tests.Ffmpeg;

public class VideoComposerRenderPlannerTests
{
    [Fact]
    public void SingleInput_WithSpaces_RemainsOneArgumentToken()
    {
        var input = Path.Combine(Path.GetTempPath(), "media folder", "source clip.mp4");
        var output = Path.Combine(Path.GetTempPath(), "render folder", "finished meme.mp4");

        var plan = VideoComposerRenderPlanner.Create(
            [new VideoRenderInput(input, 1.25, 4.5)], output);

        Assert.Contains(Path.GetFullPath(input), plan.Arguments);
        Assert.Equal(Path.GetFullPath(output), plan.Arguments[^1]);
        Assert.Equal("1.25", plan.Arguments[2]);
        Assert.Equal("4.5", plan.Arguments[4]);
        Assert.DoesNotContain(plan.Arguments, argument => argument.Contains('"'));
    }

    [Fact]
    public void Composite_PreservesSelectedComponentOrder()
    {
        var first = Path.Combine(Path.GetTempPath(), "second-in-corpus.mp4");
        var second = Path.Combine(Path.GetTempPath(), "first-in-corpus.mp4");
        var output = Path.Combine(Path.GetTempPath(), "superman.mp4");

        var plan = VideoComposerRenderPlanner.Create(
            [
                new VideoRenderInput(first, 8, 8.4),
                new VideoRenderInput(second, 1, 1.5),
            ],
            output);

        var firstIndex = plan.Arguments.IndexOf(Path.GetFullPath(first));
        var secondIndex = plan.Arguments.IndexOf(Path.GetFullPath(second));
        Assert.True(firstIndex >= 0 && secondIndex > firstIndex);

        var filter = plan.Arguments[plan.Arguments.IndexOf("-filter_complex") + 1];
        Assert.Contains("[0:v:0][0:a:0][1:v:0][1:a:0]concat=n=2:v=1:a=1[vbase][abase]", filter);
        Assert.Equal("[vbase]", plan.Arguments[plan.Arguments.IndexOf("-map") + 1]);
    }

    [Fact]
    public void Caption_IsEscapedInsideSingleFilterArgument_NotShellConstructed()
    {
        var input = Path.Combine(Path.GetTempPath(), "clip.mp4");
        var output = Path.Combine(Path.GetTempPath(), "captioned.mp4");
        var caption = "it's 100%: [safe]; yes, really";

        var plan = VideoComposerRenderPlanner.Create(
            [new VideoRenderInput(input, 0, 2)],
            output,
            new VideoCaption(caption, "top"));

        var filterIndex = plan.Arguments.IndexOf("-filter_complex");
        Assert.True(filterIndex >= 0);
        var filter = plan.Arguments[filterIndex + 1];
        Assert.Contains("it\\'s 100\\%\\: \\[safe\\]\\; yes\\, really", filter);
        Assert.Contains("y=h*0.06", filter);
        Assert.DoesNotContain("sh -c", filter, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    public void InvalidRanges_FailClosed(double start, double end)
    {
        Assert.Throws<ArgumentException>(() => VideoComposerRenderPlanner.Create(
            [new VideoRenderInput(Path.Combine(Path.GetTempPath(), "clip.mp4"), start, end)],
            Path.Combine(Path.GetTempPath(), "out.mp4")));
    }

    [Fact]
    public void OutputCannotOverwriteSource()
    {
        var source = Path.Combine(Path.GetTempPath(), "clip.mp4");

        Assert.Throws<ArgumentException>(() => VideoComposerRenderPlanner.Create(
            [new VideoRenderInput(source, 0, 1)], source));
    }

    [Fact]
    public void InvalidCaptionPosition_FailsClosed()
    {
        Assert.Throws<ArgumentException>(() => VideoComposerRenderPlanner.Create(
            [new VideoRenderInput(Path.Combine(Path.GetTempPath(), "clip.mp4"), 0, 1)],
            Path.Combine(Path.GetTempPath(), "out.mp4"),
            new VideoCaption("hello", "left")));
    }
}

internal static class ArgumentListTestExtensions
{
    public static int IndexOf(this IReadOnlyList<string> values, string value)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] == value)
            {
                return i;
            }
        }

        return -1;
    }
}
