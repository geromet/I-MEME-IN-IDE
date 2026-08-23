using MemeSearcher.Infrastructure.Alignment;
using MemeSearcher.Infrastructure.Processes;
using MemeSearcher.Infrastructure.Settings;
using MemeSearcher.Tests.TestDoubles;

namespace MemeSearcher.Tests.Alignment;

public class MfaAlignmentProviderTests
{
    private const string SampleTextGrid = """
        File type = "ooTextFile"
        Object class = "TextGrid"

        xmin = 0.0
        xmax = 2.5
        tiers? <exists>
        size = 2
        item []:
            item [1]:
                class = "IntervalTier"
                name = "words"
                xmin = 0.0
                xmax = 2.5
                intervals: size = 3
                intervals [1]:
                    xmin = 0.0
                    xmax = 0.5
                    text = ""
                intervals [2]:
                    xmin = 0.5
                    xmax = 1.2
                    text = "hello"
                intervals [3]:
                    xmin = 1.2
                    xmax = 2.5
                    text = "world"
            item [2]:
                class = "IntervalTier"
                name = "phones"
                xmin = 0.0
                xmax = 2.5
                intervals: size = 5
                intervals [1]:
                    xmin = 0.0
                    xmax = 0.5
                    text = "sil"
                intervals [2]:
                    xmin = 0.5
                    xmax = 0.8
                    text = "HH"
                intervals [3]:
                    xmin = 0.8
                    xmax = 1.2
                    text = "AH0"
                intervals [4]:
                    xmin = 1.2
                    xmax = 1.8
                    text = "W"
                intervals [5]:
                    xmin = 1.8
                    xmax = 2.5
                    text = "ER1"
        """;

    [Fact]
    public void ParseAlignmentResult_ExtractsWordsAndPhonesSkippingSilence()
    {
        var result = MfaAlignmentProvider.ParseAlignmentResult(SampleTextGrid);

        Assert.Equal(2, result.Words.Count);
        Assert.Equal("hello", result.Words[0].Text);
        Assert.Equal(0.5, result.Words[0].StartSeconds);
        Assert.Equal(1.2, result.Words[0].EndSeconds);
        Assert.Equal("world", result.Words[1].Text);

        Assert.NotNull(result.Phones);
        Assert.Equal(4, result.Phones!.Count); // "sil" excluded
        Assert.Equal("HH", result.Phones[0].Symbol);
        Assert.Equal("ER1", result.Phones[^1].Symbol);
    }

    [Fact]
    public void ParseAlignmentResult_NoPhonesTierResultsInNullPhones()
    {
        const string wordsOnly = """
            File type = "ooTextFile"
            Object class = "TextGrid"
            item []:
                item [1]:
                    class = "IntervalTier"
                    name = "words"
                    intervals: size = 1
                    intervals [1]:
                        xmin = 0.0
                        xmax = 1.0
                        text = "hi"
            """;

        var result = MfaAlignmentProvider.ParseAlignmentResult(wordsOnly);

        Assert.Single(result.Words);
        Assert.Null(result.Phones);
    }

    [Fact]
    public async Task AlignAsync_ThrowsAClearErrorWhenMfaIsNotInstalled()
    {
        var locator = new MfaToolLocator();
        var status = await locator.LocateAsync();
        if (status.IsInstalled)
        {
            return; // Can't exercise the "tool missing" path on a machine that has it.
        }

        var provider = new MfaAlignmentProvider(
            locator, new InMemorySettingsStore(), new MfaSettings());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.AlignAsync("/some/media.wav", "hello world", CancellationToken.None));

        Assert.Contains("mfa is not available", ex.Message);
    }
}
