using MemeSearcher.Core.Phonetics;
using MemeSearcher.Core.Search;

namespace MemeSearcher.Tests.Search;

/// <summary>Pure unit tests for the query-side phone parsing #21 relies on - no DB, no espeak.</summary>
public class TemplatePhoneParserTests
{
    [Fact]
    public void BuildTokens_SingleGroup_HasNoBoundaries()
    {
        var tokens = TemplatePhoneParser.BuildTokens("h ə l oʊ", PhoneAlphabet.Ipa);

        Assert.Equal(4, tokens.Count);
        Assert.DoesNotContain(tokens, t => t.IsBoundary);
        Assert.Equal(["h", "ə", "l", "oʊ"], tokens.Select(t => t.Symbol));
    }

    [Fact]
    public void BuildTokens_PipeSeparatedGroups_InsertsABoundaryBetweenThem()
    {
        var tokens = TemplatePhoneParser.BuildTokens("h ə l oʊ | w ɜr l d", PhoneAlphabet.Ipa);

        Assert.Equal(9, tokens.Count);
        Assert.True(tokens[4].IsBoundary);
        Assert.Equal(1, tokens.Count(t => t.IsBoundary));
    }

    [Fact]
    public void BuildTokens_ArpabetInput_ConvertsToCanonicalIpa()
    {
        // HH AH0 L OW1 - the same "hello" pronunciation AlignedPhoneSearchTests uses for the corpus side.
        var tokens = TemplatePhoneParser.BuildTokens("HH AH0 L OW1", PhoneAlphabet.Arpabet);

        Assert.Equal(["h", "ə", "l", "oʊ"], tokens.Select(t => t.Symbol));
    }

    [Fact]
    public void ParseSymbols_KnownIpaSymbols_AreAllReportedKnown()
    {
        var parsed = TemplatePhoneParser.ParseSymbols("h ə l oʊ", PhoneAlphabet.Ipa);

        Assert.All(parsed, p => Assert.True(p.IsKnown));
    }

    [Fact]
    public void ParseSymbols_UnrecognisedSymbol_IsReportedUnknown()
    {
        var parsed = TemplatePhoneParser.ParseSymbols("h ə zzz oʊ", PhoneAlphabet.Ipa);

        var unknown = Assert.Single(parsed, p => !p.IsKnown);
        Assert.Equal("zzz", unknown.AsAuthored);
    }
}
