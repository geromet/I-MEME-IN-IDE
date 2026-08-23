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
///
/// **Recorded baseline (2026-08-23, this machine, query "water", median of 3), also posted to
/// issue #8**, kept here because #9 changes the code these numbers describe and a "before" is
/// worthless once it can no longer be reproduced against the code it was measured on:
///
///   media | single (ms) | composite (ms) | db size
///   ------|-------------|-----------------|--------
///     10  |         177 |              89 |   2.5 MB
///    100  |      14,700 |             701 |  10.7 MB
///    400  |     278,764 |           3,150 |  29.9 MB
///
/// `single` (PhoneticSearchService) scales worse than quadratically (100->400 is 4x the data,
/// ~19x the time); `composite` stays close to linear (~4.5x). This is why #9 scopes candidate
/// generation to PhoneticSearchService only - composite doesn't have this problem yet, and #10
/// owns its own algorithm.
///
/// **#9 result, v1 (2026-08-24)**: the first version narrowed the DP inside each media's stream
/// but still loaded and rebuilt *every* scoped media's full transcript graph up front - measured
/// as a wash at 10 media and a ~5% regression at 100 (see issue #9's history for those numbers).
/// A misunderstanding of the design: the actual win available is not touching a non-candidate
/// media's transcript *at all*, not narrowing the DP inside media that get loaded regardless.
///
/// **#9 result, v2 (2026-08-24, same machine, median of 3), also posted to issue #9** - postings
/// looked up in one batched, NGram-filtered query *before* any transcript is loaded, so a media
/// with no candidate for the query is never loaded, never rebuilt, never scanned:
///
///   media | query                       | single before | single after | allocated before | allocated after
///   ------|------------------------------|----------------|---------------|-------------------|------------------
///    10   | water                        |          176ms |         144ms |            15.4MB |           18.8MB
///    10   | another question             |          189ms |         160ms |            16.0MB |           21.6MB
///    10   | important sentence together  |          213ms |         177ms |            16.6MB |           22.4MB
///   100   | water                        |       14,214ms |      13,701ms |           131.8MB |          110.4MB
///   100   | another question             |       14,137ms |      14,735ms |           137.9MB |          143.9MB
///   100   | important sentence together  |       14,379ms |      14,627ms |           143.4MB |          158.5MB
///
/// Mixed, and worth stating honestly rather than as a clean win: allocation - the more reliable
/// signal for "was a media actually skipped" than wall-clock time on a shared machine - drops for
/// the single-word query at 100 media (fewer trigrams, more selective) but *rises* for both
/// multi-word queries, meaning few or no media were skippable for them on this corpus.
/// `SyntheticCorpusGenerator` gives all 100 media the same 85-word vocabulary (#8), so a query
/// built from several of those words has a real chance of matching *something* in nearly every
/// media - the skip optimisation isn't disproven here, it's largely unmeasurable on a corpus this
/// homogeneous. A corpus with vocabulary diversity across media (a separate generator change, not
/// implemented here) would be needed to measure it properly. Left wired into PhoneticSearchService
/// per the team's call either way - the correctness exit criteria (persistence, reproducible
/// reindex, measured recall) hold regardless of the speed result.
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
    [InlineData(400)]
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

        output.WriteLine("-- before #9's index (candidate generation unavailable - full-stream scan) --");
        foreach (var query in Queries)
        {
            await MeasureAsync($"single   [{query}]", () =>
                single.SearchAsync(query, "en-US", new SearchScope.AllIndexedMedia()).ContinueWith(t => (object)t.Result));

            await MeasureAsync($"composite[{query}]", () =>
                composite.SearchAsync(query, "en-US", new SearchScope.AllIndexedMedia()).ContinueWith(t => (object)t.Result));
        }

        // #9: same corpus, same database, only the index is new - isolates candidate generation's
        // effect from any other variable (a second independently-generated corpus, a different
        // machine state, ...). composite is not re-measured: #9 deliberately does not wire
        // candidate generation into CompositeSearchService (that's #10's problem), so there is
        // nothing new to measure for it here.
        var indexStopwatch = Stopwatch.StartNew();
        var reindexSummary = await new PhoneNGramIndexService(factory).ReindexAllAsync();
        indexStopwatch.Stop();

        output.WriteLine(
            $"index: {reindexSummary.PostingCount} postings across {reindexSummary.MediaCount} media, "
            + $"built in {indexStopwatch.Elapsed.TotalSeconds:F1}s");
        output.WriteLine($"database (with index): {DatabaseSizeMb():F1} MB");

        output.WriteLine("-- after #9's index (candidate generation available) --");
        foreach (var query in Queries)
        {
            await MeasureAsync($"single   [{query}]", () =>
                single.SearchAsync(query, "en-US", new SearchScope.AllIndexedMedia()).ContinueWith(t => (object)t.Result));
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
