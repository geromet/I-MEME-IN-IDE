using MemeSearcher.Core.Interfaces;
using MemeSearcher.Infrastructure.Search;

namespace MemeSearcher.Tests.Search;

public class InMemoryQueryPhonemizationCacheTests
{
    private static PhonemizationResult FakeResult(string text) =>
        new(text, $"ipa-{text}", [new PhonemizedWord(text, $"ipa-{text}", [text])]);

    [Fact]
    public async Task GetOrAddAsync_CallsFactoryOnlyOnceForTheSameQueryAndLanguage()
    {
        var cache = new InMemoryQueryPhonemizationCache();
        var callCount = 0;

        Task<PhonemizationResult> Factory(CancellationToken _)
        {
            callCount++;
            return Task.FromResult(FakeResult("among us"));
        }

        var first = await cache.GetOrAddAsync("among us", "en-US", Factory);
        var second = await cache.GetOrAddAsync("among us", "en-US", Factory);

        Assert.Equal(1, callCount);
        Assert.Equal(first.Ipa, second.Ipa);
    }

    [Fact]
    public async Task GetOrAddAsync_TreatsDifferentLanguagesAsDifferentCacheEntries()
    {
        var cache = new InMemoryQueryPhonemizationCache();
        var callCount = 0;

        Task<PhonemizationResult> Factory(CancellationToken _)
        {
            callCount++;
            return Task.FromResult(FakeResult("hello"));
        }

        await cache.GetOrAddAsync("hello", "en-US", Factory);
        await cache.GetOrAddAsync("hello", "en-GB", Factory);

        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task GetOrAddAsync_TreatsWhitespaceAndCaseDifferencesAsTheSameQuery()
    {
        var cache = new InMemoryQueryPhonemizationCache();
        var callCount = 0;

        Task<PhonemizationResult> Factory(CancellationToken _)
        {
            callCount++;
            return Task.FromResult(FakeResult("hello"));
        }

        await cache.GetOrAddAsync("Hello", "en-US", Factory);
        await cache.GetOrAddAsync("  hello  ", "en-US", Factory);

        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task GetOrAddAsync_DoesNotCacheAFailedPhonemization()
    {
        var cache = new InMemoryQueryPhonemizationCache();
        var callCount = 0;

        Task<PhonemizationResult> FailingFactory(CancellationToken _)
        {
            callCount++;
            throw new InvalidOperationException("phonemizer unavailable");
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetOrAddAsync("hello", "en-US", FailingFactory));
        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetOrAddAsync("hello", "en-US", FailingFactory));

        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task GetOrAddAsync_EvictsTheOldestEntryOnceOverCapacity()
    {
        var cache = new InMemoryQueryPhonemizationCache(capacity: 2);

        await cache.GetOrAddAsync("one", "en-US", _ => Task.FromResult(FakeResult("one")));
        await cache.GetOrAddAsync("two", "en-US", _ => Task.FromResult(FakeResult("two")));
        await cache.GetOrAddAsync("three", "en-US", _ => Task.FromResult(FakeResult("three")));

        var callCount = 0;
        await cache.GetOrAddAsync("one", "en-US", _ =>
        {
            callCount++;
            return Task.FromResult(FakeResult("one"));
        });

        Assert.Equal(1, callCount); // "one" was evicted, so it had to be recomputed.
    }
}
