using System.Diagnostics;
using MemeSearcher.Core.Interfaces;
using MemeSearcher.Core.Search;
using MemeSearcher.Infrastructure.Database;
using MemeSearcher.Infrastructure.Phonetics;
using MemeSearcher.Infrastructure.Processes;
using MemeSearcher.Infrastructure.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace MemeSearcher.Tests.Benchmarks;

/// <summary>
/// Baseline timings for the current search implementations (#8), so #9 and #10 have a "before"
/// to be judged against.
///
/// **Opt-in.** Generating a thousand media through the real ingestion path takes minutes, which
/// has no place in the ordinary suite. Set MEMESEARCHER_BENCHMARKS=1 to run them; without it each
/// benchmark returns immediately, matching how the rest of this suite skips on a missing external
/// tool. They are also tagged Category=Benchmark for `--filter`.
///
/// This milestone must not change production behaviour - a baseline measured against modified code
/// is not a baseline.
/// </summary>
[Trait("Category", "Benchmark")]
public class SearchBenchmarks(ITestOutputHelper output) : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"memesearcher-bench-{Guid.NewGuid():N}.db");
    private readonly string _tempDir = Directory.CreateTempSubdirectory("memesearcher-bench-").FullName;

    private static bool Enabled =>
        Environment.GetEnvironmentVariable("MEMESEARCHER_BENCHMARKS") == "1";

    /// <summary>
    /// Queries chosen to span the plausible range: a short query has more match positions and a
    /// cheaper DP row, a long one the reverse, and cost should be reported for both rather than
    /// for one flattering case.
    /// </summary>
    private static readonly string[] Queries = ["water", "another question", "important sentence together"];

    private async Task<(IPhonemizer Phonemizer, IDbContextFactory<MemeSearcherDbContext> Factory)?> SetUpAsync()
    {
        var locator = new EspeakToolLocator();
        if (!(await locator.LocateAsync()).IsInstalled)
        {
            return null;
        }

        var factory = new ServiceCollection()
            .AddDbContextFactory<MemeSearcherDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"))
            .BuildServiceProvider()
            .GetRequiredService<IDbContextFactory<MemeSearcherDbContext>>();

        await using (var context = await factory.CreateDbContextAsync())
        {
            await context.Database.MigrateAsync();
        }

        return (new EspeakPhonemizer(locator), factory);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(1000)]
    public async Task Baseline(int mediaCount)
    {
        if (!Enabled)
        {
            return;
        }

        var setup = await SetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (phonemizer, factory) = setup.Value;

        var corpus = await SyntheticCorpusGenerator.GenerateAsync(
            factory, phonemizer, _tempDir, mediaCount);

        output.WriteLine(
            $"corpus: {corpus.MediaCount} media, {corpus.SegmentCount} segments, "
            + $"{corpus.WordCount} words, ingested in {corpus.Elapsed.TotalSeconds:F1}s");
        output.WriteLine($"database: {DatabaseSizeMb():F1} MB");

        var single = new PhoneticSearchService(factory, phonemizer, new InMemoryQueryPhonemizationCache());
        var composite = new CompositeSearchService(factory, phonemizer, new InMemoryQueryPhonemizationCache());

        foreach (var query in Queries)
        {
            await MeasureAsync($"single   [{query}]", () =>
                single.SearchAsync(query, "en-US", new SearchScope.AllIndexedMedia()).ContinueWith(t => (object)t.Result));

            await MeasureAsync($"composite[{query}]", () =>
                composite.SearchAsync(query, "en-US", new SearchScope.AllIndexedMedia()).ContinueWith(t => (object)t.Result));
        }
    }

    /// <summary>
    /// Sums the database and its write-ahead log. Measuring the main file alone reports ~0 MB,
    /// because with WAL enabled the freshly written pages are still in the -wal file - a number
    /// that would badly understate the "before" for #9's index-size comparison.
    /// </summary>
    private double DatabaseSizeMb() =>
        new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" }
            .Where(File.Exists)
            .Sum(p => new FileInfo(p).Length) / 1024.0 / 1024.0;

    /// <summary>
    /// One warm-up call then three timed ones, reporting the median. The first call pays for JIT
    /// and SQLite page cache warm-up, which is not what a per-query cost should include; the
    /// median of three keeps a single scheduling hiccup from becoming the headline number.
    /// </summary>
    private async Task MeasureAsync(string label, Func<Task<object>> run)
    {
        await run();

        var timings = new List<double>();
        for (var i = 0; i < 3; i++)
        {
            var before = GC.GetTotalAllocatedBytes();
            var stopwatch = Stopwatch.StartNew();
            await run();
            stopwatch.Stop();
            timings.Add(stopwatch.Elapsed.TotalMilliseconds);

            if (i == 0)
            {
                output.WriteLine($"  {label}: allocated {(GC.GetTotalAllocatedBytes() - before) / 1024.0 / 1024.0:F1} MB");
            }
        }

        timings.Sort();
        output.WriteLine($"  {label}: {timings[1]:F0} ms (median of 3; {timings[0]:F0}-{timings[2]:F0})");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
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
