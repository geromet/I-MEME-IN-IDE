using MemeSearcher.Core.Transcripts;

namespace MemeSearcher.Tests.Transcription;

public class TextNormalizerTests
{
    [Theory]
    [InlineData("Hello,  World!", "hello world")]
    [InlineData("  leading and trailing  ", "leading and trailing")]
    [InlineData("don't stop", "don't stop")]
    public void Normalize_LowercasesAndStripsPunctuation(string input, string expected)
    {
        Assert.Equal(expected, TextNormalizer.Normalize(input));
    }

    [Fact]
    public void Tokenize_SplitsOnSpaces()
    {
        Assert.Equal(["among", "us"], TextNormalizer.Tokenize("among us"));
    }

    [Fact]
    public void Tokenize_EmptyStringReturnsNoTokens()
    {
        Assert.Empty(TextNormalizer.Tokenize(""));
    }
}
