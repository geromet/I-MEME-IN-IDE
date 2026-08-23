using MemeSearcher.Core.Interfaces;
using MemeSearcher.Core.Phonetics;
using MemeSearcher.Core.Search;
using MemeSearcher.Infrastructure.Database;
using MemeSearcher.Infrastructure.Ffmpeg;
using MemeSearcher.Infrastructure.Library;
using MemeSearcher.Infrastructure.Phonetics;
using MemeSearcher.Infrastructure.Processes;
using MemeSearcher.Infrastructure.Transcription;
using MemeSearcher.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MemeSearcher.Tests.Search;

/// <summary>
/// End-to-end proof of the #18 fix, through the real database and the real matcher: import,
/// realign with an ARPABET provider, then search with an IPA query and confirm the search actually
/// reads the aligned phones.
///
/// Before this change the whole path was dead. PhoneStreamBuilder read only Word.PhonemeSequence,
/// the search services never even loaded Phones, and so realignment wrote per-phone symbols and
/// timings that no query could ever reach.
/// </summary>
public class AlignedPhoneSearchTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"memesearcher-aligned-{Guid.NewGuid():N}.db");
    private readonly string _tempDir = Directory.CreateTempSubdirectory("memesearcher-aligned-").FullName;

    private static readonly AlignmentResult HelloWorldArpabet = new(
        [new AlignedWord("hello", 1.0, 1.7), new AlignedWord("world", 1.7, 3.0)],
        [
            new AlignedPhone("HH", 1.0, 1.2), new AlignedPhone("AH0", 1.2, 1.4),
            new AlignedPhone("L", 1.4, 1.55), new AlignedPhone("OW1", 1.55, 1.7),
            new AlignedPhone("W", 1.7, 1.9), new AlignedPhone("ER1", 1.9, 2.4),
            new AlignedPhone("L", 2.4, 2.7), new AlignedPhone("D", 2.7, 3.0),
        ]);

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

    private MediaIngestionService NewService(
        MemeSearcherDbContext context, IPhonemizer phonemizer, IAlignmentProvider? aligner = null) =>
        new(context, TranscriptParserFactory.CreateDefault(), phonemizer,
            new UnusedTranscriptionProvider(), new MediaMetadataProbe(new FFprobeToolLocator()), aligner);

    private async Task<Guid> ImportAndRealignAsync(
        IServiceProvider services, IPhonemizer phonemizer, IAlignmentProvider aligner)
    {
        var mediaPath = Path.Combine(_tempDir, "clip.mp4");
        await File.WriteAllTextAsync(mediaPath, "placeholder - never decoded, the aligner is faked");
        var srtPath = Path.Combine(_tempDir, "clip.srt");
        await File.WriteAllTextAsync(srtPath, """
            1
            00:00:01,000 --> 00:00:03,000
            hello world

            """);

        await using (var importContext = await Factory(services).CreateDbContextAsync())
        {
            await NewService(importContext, phonemizer)
                .ImportAsync(new MediaIngestionRequest(mediaPath, srtPath, "en-US"));
        }

        await using var context = await Factory(services).CreateDbContextAsync();
        var media = await context.Media.SingleAsync();
        await NewService(context, phonemizer, aligner).RealignAsync(media.Id);
        return media.Id;
    }

    [Fact]
    public async Task RealignAsync_TagsPhonesWithTheProvidersDeclaredAlphabet()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (phonemizer, services) = setup.Value;
        var mediaId = await ImportAndRealignAsync(
            services, phonemizer, new FakeAlignmentProvider(HelloWorldArpabet));

        await using var context = await Factory(services).CreateDbContextAsync();
        var phones = await context.Phones.ToListAsync();

        Assert.NotEmpty(phones);
        Assert.All(phones, p => Assert.Equal(PhoneAlphabet.Arpabet, p.Alphabet));

        // The same Word simultaneously holds espeak's IPA - which is the situation that made the
        // untagged state unrecoverable.
        var words = await context.Words.ToListAsync();
        Assert.All(words, w => Assert.Equal(PhoneAlphabet.Ipa, w.PhonemeAlphabet));
    }

    [Fact]
    public async Task SearchAsync_MatchesAnArpabetAlignedCorpusWithAnIpaQuery()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (phonemizer, services) = setup.Value;
        await ImportAndRealignAsync(services, phonemizer, new FakeAlignmentProvider(HelloWorldArpabet));

        var searchService = new Infrastructure.Search.PhoneticSearchService(
            Factory(services), phonemizer, new Infrastructure.Search.InMemoryQueryPhonemizationCache());

        var results = await searchService.SearchAsync("hello", "en-US", new SearchScope.AllIndexedMedia());

        Assert.NotEmpty(results);
    }

    /// <summary>
    /// Proves search reads the Phone table rather than falling back to Word.PhonemeSequence, by
    /// making the two disagree on purpose.
    ///
    /// espeak predicts "hello" as h ə l oʊ. This aligner reports the speaker actually said
    /// HH EH1 L OW1 - "h ɛ l oʊ", with ɛ where the prediction has ə. Whichever vowel comes back in
    /// the match decides which source the stream was built from, so a fallback cannot pass this.
    /// That distinction is the point of the whole feature (handoff §49: do not pretend eSpeak
    /// output is an acoustic transcription of the speaker) - previously the app stored the actual
    /// and searched only the prediction.
    /// </summary>
    [Fact]
    public async Task SearchAsync_MatchesTheAlignedPronunciationNotThePredictedOne()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (phonemizer, services) = setup.Value;

        var alignedDifferently = new AlignmentResult(
            [new AlignedWord("hello", 1.0, 1.7), new AlignedWord("world", 1.7, 3.0)],
            [
                new AlignedPhone("HH", 1.0, 1.2), new AlignedPhone("EH1", 1.2, 1.4),
                new AlignedPhone("L", 1.4, 1.55), new AlignedPhone("OW1", 1.55, 1.7),
                new AlignedPhone("W", 1.7, 1.9), new AlignedPhone("ER1", 1.9, 2.4),
                new AlignedPhone("L", 2.4, 2.7), new AlignedPhone("D", 2.7, 3.0),
            ]);

        await ImportAndRealignAsync(services, phonemizer, new FakeAlignmentProvider(alignedDifferently));

        var searchService = new Infrastructure.Search.PhoneticSearchService(
            Factory(services), phonemizer, new Infrastructure.Search.InMemoryQueryPhonemizationCache());

        var results = await searchService.SearchAsync("hello", "en-US", new SearchScope.AllIndexedMedia());

        var match = Assert.Single(results);

        Assert.Contains("ɛ", match.MatchPhonemes);
        Assert.DoesNotContain("ə", match.MatchPhonemes);
    }

    [Fact]
    public async Task SearchAsync_ResolvesMatchesToTheAlignersTiming()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (phonemizer, services) = setup.Value;
        await ImportAndRealignAsync(services, phonemizer, new FakeAlignmentProvider(HelloWorldArpabet));

        var searchService = new Infrastructure.Search.PhoneticSearchService(
            Factory(services), phonemizer, new Infrastructure.Search.InMemoryQueryPhonemizationCache());

        var results = await searchService.SearchAsync("world", "en-US", new SearchScope.AllIndexedMedia());

        var match = Assert.Single(results);
        Assert.Equal(1.7, match.StartSeconds!.Value, precision: 2);
        Assert.Equal(3.0, match.EndSeconds!.Value, precision: 2);
    }

    /// <summary>
    /// A provider whose declaration contradicts its own output must fail loudly. Mis-tagged phones
    /// convert to the wrong canonical symbols and the corpus quietly stops matching - the exact
    /// silent failure #18 exists to close.
    /// </summary>
    [Fact]
    public async Task RealignAsync_RefusesAProviderWhoseDeclaredAlphabetContradictsItsOutput()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (phonemizer, services) = setup.Value;

        // Declares IPA, emits unmistakable ARPABET (stress digits).
        var liar = new FakeAlignmentProvider(HelloWorldArpabet, PhoneAlphabet.Ipa);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ImportAndRealignAsync(services, phonemizer, liar));

        Assert.Contains("declares Ipa", ex.Message);
        Assert.Contains("Arpabet", ex.Message);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
            File.Delete(_dbPath);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }

        GC.SuppressFinalize(this);
    }
}
