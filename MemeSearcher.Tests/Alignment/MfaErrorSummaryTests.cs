using MemeSearcher.Infrastructure.Alignment;

namespace MemeSearcher.Tests.Alignment;

public class MfaErrorSummaryTests
{
    /// <summary>
    /// Captured verbatim from a real `mfa align` run against a machine with no models installed -
    /// the exact situation a fresh MFA install produces.
    /// </summary>
    private const string RealMfaModelNotFoundStderr = @"                                                                                
 Usage:                                                                         
 mfa align                                                                      
 [OPTIONS] CORPUS_DIRECTORY DICTIONARY_PATH ACOUSTIC_MODEL_PATH                 
 OUTPUT_DIRECTORY                                                               
                                                                                
╭─ Error ──────────────────────────────────────────────────────────────────────╮
│ Invalid value for 'DICTIONARY_PATH': PretrainedModelNotFoundError:           │
│                                                                              │
│ Could not find a model named ""english_us_arpa"" for dictionary.               │
╰──────────────────────────────────────────────────────────────────────────────╯
                                                                                
";

    /// <summary>
    /// MFA renders errors as a Unicode box, preceded by a usage banner longer than the message.
    /// Passed through verbatim into a one-line status bar it reads as noise or as nothing at all,
    /// which is how a perfectly clear model-not-found looked like the Realign button doing nothing.
    /// </summary>
    [Fact]
    public void SummarizeMfaError_ExtractsTheMessageFromTheErrorBox()
    {
        var summary = MfaAlignmentProvider.SummarizeMfaError(RealMfaModelNotFoundStderr);

        Assert.Contains(@"Could not find a model named ""english_us_arpa"" for dictionary.", summary);
        Assert.DoesNotContain("OPTIONS", summary);
        Assert.DoesNotContain("\u2502", summary);
        Assert.DoesNotContain("\u2500", summary);
    }

    [Fact]
    public void SummarizeMfaError_IsASingleLine()
    {
        // The status bar is one line; a multi-line message is truncated to its least useful part.
        Assert.DoesNotContain("\n", MfaAlignmentProvider.SummarizeMfaError(RealMfaModelNotFoundStderr));
    }

    [Fact]
    public void SummarizeMfaError_FallsBackToWholeOutputWhenThereIsNoBox()
    {
        Assert.Equal("something broke", MfaAlignmentProvider.SummarizeMfaError("something broke\n"));
    }

    [Fact]
    public void SummarizeMfaError_SaysSoWhenThereIsNothingToReport()
    {
        Assert.Equal("no error output", MfaAlignmentProvider.SummarizeMfaError("   \n  \n"));
    }
}
