using System.Text.Json;
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
/// #36 end-to-end proof that authored template phones can drive the real multi-file matcher. The
/// corpus is imported into real SQLite and real espeak-ng is used for normal ingestion; only the
/// composite search service receives a throwing phonemizer, proving the template path uses the new
/// phone-token overload rather than silently turning authored sounds back into text.
/// </summary>
public sealed class TemplateCompositeSearchTests : IDisposable
{
    private sealed class ThrowingPhonemizer : IPhonemizer
    {
        public string ProviderName => "must-not-run";
        public IReadOnlyCollection<string> SupportedLanguages => ["en-US"];
        public PhoneAlphabet Alphabet => PhoneAlphabet.Ipa;

        public Task<PhonemizationResult> PhonemizeAsync(
            string text, string language, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Composite template execution must bypass text phonemization.");
    }

    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(), $"memesearcher-template-composite-{Guid.NewGuid():N}.db");
    private readonly string _tempDir = Directory.CreateTempSubdirectory(
        "memesearcher-template-composite-").FullName;

    [Fact]
    public async Task AuthoredPhones_FindACompositeOnlyMatch_WithoutInvokingTextPhonemizer()
    {
        var locator = new EspeakToolLocator();
        if (!(await locator.LocateAsync()).IsInstalled)
        {
            return;
        }

        var services = new ServiceCollection()
            .AddDbContextFactory<MemeSearcherDbContext>(options => options.UseSqlite($"Data Source={_dbPath}"))
            .BuildServiceProvider();
        var factory = services.GetRequiredService<IDbContextFactory<MemeSearcherDbContext>>();

        await using (var context = await factory.CreateDbContextAsync())
        {
            await context.Database.MigrateAsync();
        }

        var ingestionPhonemizer = new EspeakPhonemizer(locator);
        var firstPhones = new[] { "ʁ", "ɣ" };
        var secondPhones = new[] { "ɣ", "ʁ" };
        var firstMedia = await ImportAndForceAlignAsync(factory, ingestionPhonemizer, "growl", firstPhones);
        var secondMedia = await ImportAndForceAlignAsync(factory, ingestionPhonemizer, "growled", secondPhones);

        var queryCache = new InMemoryQueryPhonemizationCache();
        var singleSearch = new PhoneticSearchService(factory, ingestionPhonemizer, queryCache);
        var compositeSearch = new CompositeSearchService(factory, new ThrowingPhonemizer(), queryCache);
        var templateService = new TemplateService(factory);
        var catalogService = new CatalogService(factory);
        var templateSearch = new TemplateSearchService(factory, singleSearch, catalogService, compositeSearch);

        var templateId = await templateService.CreateAsync("Split growl", null);
        await templateService.AddVariantAsync(
            templateId,
            "Across two clips",
            string.Join(' ', firstPhones.Concat(secondPhones)),
            PhoneAlphabet.Ipa);

        // Make dropping half the authored query prohibitively expensive while leaving the
        // intentional source transition free. This gives the negative control a crisp boundary:
        // neither individual file can satisfy the template, but the two-file stream can.
        var options = new PhoneticSearchOptions
        {
            InsertionCost = 10,
            DeletionCost = 10,
            SubstitutionMaxCost = 1,
            WordBoundaryCost = 0,
            CrossFileTransitionCost = 0,
            MinimumScore = 0.95,
            MaxResults = 10,
            MaxSourceFiles = 2,
            MinPhonemesPerSource = 2,
            UseCandidateOrdering = false,
        };
        await templateService.SetSearchOptionsAsync(templateId, JsonSerializer.Serialize(options));

        var explicitScope = new SearchScope.SelectedMedia([firstMedia, secondMedia]);

        var singleOutcome = await templateSearch.SearchAsync(templateId, explicitScope);
        Assert.Empty(singleOutcome.Results);

        var compositeOutcome = await templateSearch.SearchCompositeAsync(templateId, explicitScope);
        var best = Assert.Single(compositeOutcome.Results);

        Assert.Equal([firstMedia, secondMedia], best.Components.Select(component => component.MediaId));
        Assert.Equal(firstPhones.Concat(secondPhones), best.QueryPhonemes);
        Assert.Equal("2 source(s)", compositeOutcome.ScopeDescription);
        Assert.Equal([firstMedia, secondMedia], compositeOutcome.SelectedMediaIds);
    }

    private async Task<Guid> ImportAndForceAlignAsync(
        IDbContextFactory<MemeSearcherDbContext> factory,
        IPhonemizer phonemizer,
        string word,
        IReadOnlyList<string> actualPhones)
    {
        var mediaPath = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.mp4");
        await File.WriteAllTextAsync(mediaPath, $"fixture-{Guid.NewGuid():N}");
        var srtPath = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.srt");
        await File.WriteAllTextAsync(srtPath, $"""
            1
            00:00:01,000 --> 00:00:03,000
            {word}

            """);

        Guid mediaId;
        await using (var context = await factory.CreateDbContextAsync())
        {
            var imported = await new MediaIngestionService(
                context,
                TranscriptParserFactory.CreateDefault(),
                phonemizer,
                new UnusedTranscriptionProvider(),
                new MediaMetadataProbe(new FFprobeToolLocator()))
                .ImportAsync(new MediaIngestionRequest(mediaPath, srtPath, "en-US"));
            mediaId = imported.Media.Id;
        }

        var phoneDuration = 2.0 / actualPhones.Count;
        var alignedPhones = actualPhones
            .Select((symbol, index) => new AlignedPhone(
                symbol,
                1.0 + index * phoneDuration,
                1.0 + (index + 1) * phoneDuration))
            .ToList();
        var alignment = new AlignmentResult(
            [new AlignedWord(word, 1.0, 3.0)], alignedPhones);

        await using var realignContext = await factory.CreateDbContextAsync();
        await new MediaIngestionService(
            realignContext,
            TranscriptParserFactory.CreateDefault(),
            phonemizer,
            new UnusedTranscriptionProvider(),
            new MediaMetadataProbe(new FFprobeToolLocator()),
            new FakeAlignmentProvider(alignment, PhoneAlphabet.Ipa))
            .RealignAsync(mediaId);

        return mediaId;
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

        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
