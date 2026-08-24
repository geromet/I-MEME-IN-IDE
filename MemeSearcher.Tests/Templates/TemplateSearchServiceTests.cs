using MemeSearcher.Core.Interfaces;
using MemeSearcher.Core.Phonetics;
using MemeSearcher.Core.Search;
using MemeSearcher.Infrastructure.Catalogs;
using MemeSearcher.Infrastructure.Database;
using MemeSearcher.Infrastructure.Ffmpeg;
using MemeSearcher.Infrastructure.Library;
using MemeSearcher.Infrastructure.Phonetics;
using MemeSearcher.Infrastructure.Processes;
using MemeSearcher.Infrastructure.Search;
using MemeSearcher.Infrastructure.Templates;
using MemeSearcher.Infrastructure.Transcription;
using MemeSearcher.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MemeSearcher.Tests.Templates;

/// <summary>
/// Milestone 18 (#21) exit criteria, end to end through the real database and matcher:
/// 1. A hand-authored template finds a match the equivalent text query provably does not.
/// 2. A template with two pronunciation variants matches sources matching either.
/// Uses the same FakeAlignmentProvider fixture pattern as AlignedPhoneSearchTests (#18) to give a
/// media item real per-phone data without a real MFA install.
/// </summary>
public class TemplateSearchServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"memesearcher-templatesearch-test-{Guid.NewGuid():N}.db");
    private readonly string _tempDir = Directory.CreateTempSubdirectory("memesearcher-templatesearch-test-").FullName;

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

    /// <summary>Imports a one-word transcript, then overwrites its aligned phones with an arbitrary IPA sequence via a fake aligner - standing in for a sound that has no realistic English spelling (a growl, a scream) rather than reusing #18's "mispronunciation" fixture, since that one is deliberately close enough in feature space to still fuzzy-match a text query.</summary>
    private async Task<Guid> ImportAndForceAlignAsync(
        IServiceProvider services, IPhonemizer phonemizer, string word, IReadOnlyList<string> actualIpaPhones)
    {
        var mediaPath = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.mp4");
        // Content, not just filename, must be unique per call - identity/ContentHash (addendum §3)
        // hashes the media file's bytes, and two byte-identical "placeholder" files would dedupe to
        // one Media row regardless of their distinct paths or transcript text.
        await File.WriteAllTextAsync(mediaPath, $"placeholder - never decoded, the aligner is faked - {Guid.NewGuid():N}");
        var srtPath = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.srt");
        await File.WriteAllTextAsync(srtPath, $"""
            1
            00:00:01,000 --> 00:00:03,000
            {word}

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

        var span = 2.0 / actualIpaPhones.Count;
        var phones = actualIpaPhones
            .Select((symbol, i) => new AlignedPhone(symbol, 1.0 + i * span, 1.0 + (i + 1) * span))
            .ToList();
        var alignment = new AlignmentResult([new AlignedWord(word, 1.0, 3.0)], phones);

        await using var context = await Factory(services).CreateDbContextAsync();
        await new MediaIngestionService(
            context, TranscriptParserFactory.CreateDefault(), phonemizer,
            new UnusedTranscriptionProvider(), new MediaMetadataProbe(new FFprobeToolLocator()),
            new FakeAlignmentProvider(alignment, PhoneAlphabet.Ipa))
            .RealignAsync(mediaId);

        return mediaId;
    }

    [Fact]
    public async Task TemplateSearch_FindsAMatch_TheEquivalentTextQueryProvablyDoesNot()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (phonemizer, services) = setup.Value;

        // "growl" is spoken, but the actual sound has none of that word's real phones - three
        // consecutive uvular/velar fricatives, entirely outside espeak's en-US inventory
        // (PhonemeFeatureTable's own comments mark ʁ/ɣ as "outside en-US inventory"), so no English
        // spelling could ever phonemize to this exactly.
        //
        // Note this only proves the claim under ExactPhonetic. Tried lengthening this sequence to
        // 8 phones on the theory that SimilarPhonetic's default (0.5 minimum score) would fail once
        // the mismatch accumulated - it didn't move the score at all (still 0.545, still "found"):
        // the matcher's candidate window slides to the best-scoring 3-phone span inside the longer
        // corpus stream regardless of how much unrelated corpus surrounds it, so corpus length past
        // the query's own length doesn't add cost. SimilarPhonetic's fuzzy tolerance really will
        // bridge three totally foreign phones against "growl"'s predicted [ɡ ɹ aʊ l] at its default
        // threshold - a real, worth-flagging property of the matcher, not a gap in this fixture.
        string[] actualPhones = ["ʁ", "ɣ", "ʁ"];
        await ImportAndForceAlignAsync(services, phonemizer, "growl", actualPhones);

        var dbFactory = Factory(services);
        var queryCache = new InMemoryQueryPhonemizationCache();
        var searchService = new PhoneticSearchService(dbFactory, phonemizer, queryCache);

        // The equivalent text query, searched exactly, finds nothing - no English spelling can ever
        // phonemize to three uvular/velar fricatives. ExactPhonetic is what makes "provably does
        // not" unambiguous; see the note above for why SimilarPhonetic's default does not clear
        // this bar.
        var textResults = await searchService.SearchAsync("growl", "en-US", new SearchScope.AllIndexedMedia(), SearchMode.ExactPhonetic);
        Assert.Empty(textResults);

        // The hand-authored phone template, bypassing the phonemizer entirely, finds it.
        var templateService = new TemplateService(dbFactory);
        var catalogService = new CatalogService(dbFactory);
        var templateId = await templateService.CreateAsync("Growl", null);
        await templateService.AddVariantAsync(templateId, "Exact", string.Join(' ', actualPhones), PhoneAlphabet.Ipa);

        var templateSearchService = new TemplateSearchService(dbFactory, searchService, catalogService);
        var outcome = await templateSearchService.SearchAsync(templateId);

        Assert.NotEmpty(outcome.Results);
        Assert.Contains("ʁ", Assert.Single(outcome.Results).Phonemes);
    }

    [Fact]
    public async Task TemplateSearch_WithTwoVariants_MatchesSourcesMatchingEither()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (phonemizer, services) = setup.Value;

        string[] usPhones = ["ʁ", "ɣ", "ʁ"];
        string[] ukPhones = ["ɣ", "ʁ", "ɣ"];
        // Distinct transcript text per media - see the catalog-scoping test below for why.
        var usMediaId = await ImportAndForceAlignAsync(services, phonemizer, "growl", usPhones);
        var ukMediaId = await ImportAndForceAlignAsync(services, phonemizer, "growled", ukPhones);

        var dbFactory = Factory(services);
        var searchService = new PhoneticSearchService(dbFactory, phonemizer, new InMemoryQueryPhonemizationCache());
        var templateService = new TemplateService(dbFactory);
        var catalogService = new CatalogService(dbFactory);

        var templateId = await templateService.CreateAsync("Growl", null);
        await templateService.AddVariantAsync(templateId, "US", string.Join(' ', usPhones), PhoneAlphabet.Ipa);
        await templateService.AddVariantAsync(templateId, "UK", string.Join(' ', ukPhones), PhoneAlphabet.Ipa);

        var templateSearchService = new TemplateSearchService(dbFactory, searchService, catalogService);
        var outcome = await templateSearchService.SearchAsync(templateId);

        var matchedMediaIds = outcome.Results.Select(r => r.MediaId).ToHashSet();
        Assert.Contains(usMediaId, matchedMediaIds);
        Assert.Contains(ukMediaId, matchedMediaIds);
    }

    [Fact]
    public async Task TemplateSearch_WithATargetCatalog_SearchesOnlyThatCatalogsMembers()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (phonemizer, services) = setup.Value;

        string[] phones = ["ʁ", "ɣ", "ʁ"];
        // Distinct transcript text per media - identical SRT content across two imports hashes to
        // the same ContentHash (addendum §3's dedup key) and the second "import" would just return
        // the first media instead of creating a second one.
        var inCatalogId = await ImportAndForceAlignAsync(services, phonemizer, "growl", phones);
        var outOfCatalogId = await ImportAndForceAlignAsync(services, phonemizer, "growled", phones);

        var dbFactory = Factory(services);
        var searchService = new PhoneticSearchService(dbFactory, phonemizer, new InMemoryQueryPhonemizationCache());
        var templateService = new TemplateService(dbFactory);
        var catalogService = new CatalogService(dbFactory);

        var catalogId = await catalogService.CreateAsync("Growls only", null);
        await catalogService.SetMemberAsync(catalogId, inCatalogId, true);

        var templateId = await templateService.CreateAsync("Growl", null);
        await templateService.AddVariantAsync(templateId, "Exact", string.Join(' ', phones), PhoneAlphabet.Ipa);
        await templateService.SetTargetCatalogAsync(templateId, catalogId);

        var templateSearchService = new TemplateSearchService(dbFactory, searchService, catalogService);
        var outcome = await templateSearchService.SearchAsync(templateId);

        var matchedMediaIds = outcome.Results.Select(r => r.MediaId).ToHashSet();
        Assert.Contains(inCatalogId, matchedMediaIds);
        Assert.DoesNotContain(outOfCatalogId, matchedMediaIds);

        // The description is provably the scope the search actually used, not a separately
        // re-derived one - see TemplateSearchOutcome's own doc comment for why that matters.
        Assert.Equal("Catalog: Growls only (1 source(s))", outcome.ScopeDescription);
        Assert.Equal([inCatalogId], outcome.SelectedMediaIds);
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
