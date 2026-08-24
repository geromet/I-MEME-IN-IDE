using MemeSearcher.Core.Interfaces;
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
            () => provider.AlignAsync("/some/media.wav", [new AlignmentUtterance(0, 1, "hello world")], 1, CancellationToken.None));

        Assert.Contains("mfa is not available", ex.Message);
    }

    /// <summary>
    /// #33: a TextGrid-corpus input makes MFA treat the input tier's name as a speaker label, so
    /// its output tiers are named "&lt;speaker&gt; - words"/"&lt;speaker&gt; - phones" rather than
    /// the bare "words"/"phones" a single `.lab` corpus produced - both must parse.
    /// </summary>
    [Fact]
    public void ParseAlignmentResult_SpeakerPrefixedTierNames_StillParse()
    {
        const string speakerPrefixed = """
            File type = "ooTextFile"
            Object class = "TextGrid"
            item []:
                item [1]:
                    class = "IntervalTier"
                    name = "utterances - words"
                    intervals: size = 1
                    intervals [1]:
                        xmin = 0.0
                        xmax = 1.0
                        text = "hi"
                item [2]:
                    class = "IntervalTier"
                    name = "utterances - phones"
                    intervals: size = 1
                    intervals [1]:
                        xmin = 0.0
                        xmax = 1.0
                        text = "HH"
            """;

        var result = MfaAlignmentProvider.ParseAlignmentResult(speakerPrefixed);

        Assert.Single(result.Words);
        Assert.Equal("hi", result.Words[0].Text);
        Assert.NotNull(result.Phones);
        Assert.Single(result.Phones!);
        Assert.Equal("HH", result.Phones![0].Symbol);
    }

    [Fact]
    public void SummarizeMfaError_BoxedError_ExtractsTheBoxContent()
    {
        var stderr = "usage: mfa align ...\n"
            + "╭─ Error ─╮\n"
            + "│ Could not find a model named 'foo'. │\n"
            + "╰───────────────╯\n";

        var summary = MfaAlignmentProvider.SummarizeMfaError(stderr);

        Assert.Equal("Could not find a model named 'foo'.", summary);
    }

    /// <summary>#33's own secondary finding: a plain Python traceback has no box, and the whole wall of text was passing through with the actual exception buried at the end.</summary>
    [Fact]
    public void SummarizeMfaError_UnboxedTraceback_ExtractsTheFinalExceptionLine()
    {
        var stderr = """
            Traceback (most recent call last):
              File "mfa/command_line.py", line 42, in align
                aligner.align()
              File "mfa/alignment.py", line 100, in align
                raise NoAlignmentsError(...)
            mfa.exceptions.NoAlignmentsError: There were no successful alignments for 1 utterances.
            The current set up used a beam of 10 and a retry beam of 40.
            """;

        var summary = MfaAlignmentProvider.SummarizeMfaError(stderr);

        Assert.Equal(
            "mfa.exceptions.NoAlignmentsError: There were no successful alignments for 1 utterances. "
            + "The current set up used a beam of 10 and a retry beam of 40.",
            summary);
    }

    [Fact]
    public void SummarizeMfaError_NoRecognizableShape_FallsBackToTheLastNonEmptyLine()
    {
        var summary = MfaAlignmentProvider.SummarizeMfaError("some warning\nanother line\nthe actual problem\n");

        Assert.Equal("the actual problem", summary);
    }

    [Fact]
    public void SummarizeMfaError_EmptyOutput_ReportsNoErrorOutput()
    {
        Assert.Equal("no error output", MfaAlignmentProvider.SummarizeMfaError(""));
    }
}
