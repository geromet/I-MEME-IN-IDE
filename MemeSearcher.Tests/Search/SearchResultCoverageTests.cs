using MemeSearcher.Core.Interfaces;
using MemeSearcher.Core.Phonetics;
using MemeSearcher.Core.Search;
using MemeSearcher.Infrastructure.Database;
using MemeSearcher.Infrastructure.Ffmpeg;
using MemeSearcher.Infrastructure.Library;
using MemeSearcher.Infrastructure.Phonetics;
using MemeSearcher.Infrastructure.Processes;
using MemeSearcher.Infrastructure.Search;
using MemeSearcher.Infrastructure.Transcription;
using MemeSearcher.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MemeSearcher.Tests.Search;

/// <summary>
/// #25's central claim: a result whose covered span is shorter than the query is real, not a
/// fixture artefact - the "moeten" report's actual shape is a short genuine overlap plus phones the
/// candidate simply doesn't have at all. PhoneticSequenceMatcherTests already proves this at the DP
/// level with synthetic tokens (AlignmentOp.QueryExtra for the uncovered tail); this proves it
/// survives all the way out through PhoneticSearchService.ToSearchResult as QueryStart/QueryEnd -
/// which advisor review flagged as unverified, since Move.Substitute can just as easily win against
/// a real candidate stream and leave nothing to distinguish.
///
/// Uses the query-tokens overload (#21) to control the query's exact phones directly, and a forced
/// realignment (the same technique TemplateSearchServiceTests uses) to control the candidate's exact
/// phones - both sides deterministic, no dependence on what espeak happens to predict for any
/// particular English word.
/// </summary>
public class SearchResultCoverageTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"memesearcher-coverage-test-{Guid.NewGuid():N}.db");
    private readonly string _tempDir = Directory.CreateTempSubdirectory("memesearcher-coverage-test-").FullName;

    private async Task<(IPhonemizer Phonemizer, IServiceProvider Services)?> TrySetUpAsync()
    {
        var locator = new EspeakToolLocator();
        if (!(await locator.LocateAsync()).IsInstalled)
        {
            return null;
        }

        var services = new ServiceCollection()
            .AddDbContextFactory<MemeSearcherDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"))
            .BuildServiceProvider();

        await using (var context = await services.GetRequiredService<IDbContextFactory<MemeSearcherDbContext>>().CreateDbContextAsync())
        {
            await context.Database.MigrateAsync();
        }

        return (new EspeakPhonemizer(locator), services);
    }

    private IDbContextFactory<MemeSearcherDbContext> Factory(IServiceProvider services) =>
        services.GetRequiredService<IDbContextFactory<MemeSearcherDbContext>>();

    /// <summary>Imports a one-word transcript, then overwrites its aligned phones with an arbitrary short IPA sequence via a fake aligner, so the candidate stream this media contributes is exactly and only these three phones.</summary>
    private async Task<Guid> ImportWithExactCandidatePhonesAsync(IServiceProvider services, IPhonemizer phonemizer, IReadOnlyList<string> candidatePhones)
    {
        var mediaPath = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.mp4");
        await File.WriteAllTextAsync(mediaPath, $"placeholder - never decoded, the aligner is faked - {Guid.NewGuid():N}");
        var srtPath = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.srt");
        await File.WriteAllTextAsync(srtPath, """
            1
            00:00:01,000 --> 00:00:03,000
            word

            """);

        Guid mediaId;
        await using (var importContext = await Factory(services).CreateDbContextAsync())
        {
            var result = await new MediaIngestionService(
                importContext, TranscriptParserFactory.CreateDefault(), phonemizer,
                new UnusedTranscriptionProvider(), new MediaMetadataProbe(new FFprobeToolLocator()))
                .ImportAsync(new MediaIngestionRequest(mediaPath, srtPath, "en-US"));
            mediaId = result.Media.Id;
        }

        var span = 2.0 / candidatePhones.Count;
        var phones = candidatePhones
            .Select((symbol, i) => new AlignedPhone(symbol, 1.0 + i * span, 1.0 + (i + 1) * span))
            .ToList();
        var alignment = new AlignmentResult([new AlignedWord("word", 1.0, 3.0)], phones);

        await using var context = await Factory(services).CreateDbContextAsync();
        await new MediaIngestionService(
            context, TranscriptParserFactory.CreateDefault(), phonemizer,
            new UnusedTranscriptionProvider(), new MediaMetadataProbe(new FFprobeToolLocator()),
            new FakeAlignmentProvider(alignment, PhoneAlphabet.Ipa))
            .RealignAsync(mediaId);

        return mediaId;
    }

    [Fact]
    public async Task SearchAsync_CandidateShorterThanQuery_ReportsAPartialCoveredSpan()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (phonemizer, services) = setup.Value;

        // The candidate stream is exactly these 3 phones - nothing else exists anywhere in the
        // corpus for the matcher to substitute against.
        string[] candidatePhones = ["a", "n", "a"];
        await ImportWithExactCandidatePhonesAsync(services, phonemizer, candidatePhones);

        // A 5-phone query: the middle 3 exactly match the candidate; "ʁ" and "ɣ" are outside the
        // candidate (and outside en-US's inventory entirely, per PhonemeFeatureTable's own comment)
        // - with only 3 candidate phones to align against, the DP cannot substitute all 5 query
        // positions no matter how the alignment runs, so the two outer positions must fall to
        // AlignmentOp.QueryExtra ("candidate simply doesn't have this").
        List<PhoneToken> queryTokens = [
            PhoneToken.Phoneme("ʁ"),
            PhoneToken.Phoneme("a"),
            PhoneToken.Phoneme("n"),
            PhoneToken.Phoneme("a"),
            PhoneToken.Phoneme("ɣ"),
        ];

        var dbFactory = Factory(services);
        var searchService = new PhoneticSearchService(dbFactory, phonemizer, new InMemoryQueryPhonemizationCache());

        // MinimumScore: 0 - this test is about whether the covered-span mechanism fires at all,
        // not about whether this particular partial match would pass the default quality bar.
        var options = new PhoneticSearchOptions { MinimumScore = 0 };
        var results = await searchService.SearchAsync(queryTokens, new SearchScope.AllIndexedMedia(), SearchMode.SimilarPhonetic, options);

        var result = Assert.Single(results);
        Assert.Equal(5, result.QueryPhonemes.Count);

        // The claim this whole test exists to prove: coverage is a real subset of the query, not
        // always [0, Count) regardless of what actually matched.
        Assert.True(result.QueryEnd - result.QueryStart < result.QueryPhonemes.Count,
            $"expected a partial span, got [{result.QueryStart}, {result.QueryEnd}) over {result.QueryPhonemes.Count} query phonemes");
        Assert.Equal(1, result.QueryStart);
        Assert.Equal(4, result.QueryEnd);

        // And the strip built from it agrees: the outer two positions are outside the span, the
        // inner three are exact matches.
        var cells = MemeSearcher.ViewModels.PhoneCoverageStripBuilder.Build(result.QueryPhonemes, result.AlignmentSteps, result.QueryStart, result.QueryEnd);
        Assert.True(cells[0].IsOutsideSpan);
        Assert.True(cells[1].IsMatch);
        Assert.True(cells[2].IsMatch);
        Assert.True(cells[3].IsMatch);
        Assert.True(cells[4].IsOutsideSpan);
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }

        Directory.Delete(_tempDir, recursive: true);
    }
}
