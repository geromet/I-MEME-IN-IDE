using System.Collections.Concurrent;
using MemeSearcher.Core.Interfaces;

namespace MemeSearcher.Infrastructure.Search;

/// <summary>
/// A process-lifetime, bounded LRU cache keyed on (normalized query text, language). Bounded
/// because query text is arbitrary user input - an unbounded cache would be a slow memory leak
/// over a long-running session of varied searches.
/// </summary>
public class InMemoryQueryPhonemizationCache(int capacity = 200) : IQueryPhonemizationCache
{
    private readonly ConcurrentDictionary<(string Text, string Language), Task<PhonemizationResult>> _entries = new();
    private readonly ConcurrentQueue<(string Text, string Language)> _order = new();
    private readonly Lock _evictionLock = new();

    public async Task<PhonemizationResult> GetOrAddAsync(
        string queryText,
        string language,
        Func<CancellationToken, Task<PhonemizationResult>> factory,
        CancellationToken cancellationToken = default)
    {
        var key = (Text: Normalize(queryText), Language: language);

        if (_entries.TryGetValue(key, out var cached))
        {
            return await cached;
        }

        var task = factory(cancellationToken);

        if (_entries.TryAdd(key, task))
        {
            _order.Enqueue(key);
            EvictIfOverCapacity();
        }

        try
        {
            return await task;
        }
        catch
        {
            // Don't keep a failed phonemization cached - the next attempt should retry for real.
            _entries.TryRemove(key, out _);
            throw;
        }
    }

    private void EvictIfOverCapacity()
    {
        if (_entries.Count <= capacity)
        {
            return;
        }

        lock (_evictionLock)
        {
            while (_entries.Count > capacity && _order.TryDequeue(out var oldest))
            {
                _entries.TryRemove(oldest, out _);
            }
        }
    }

    private static string Normalize(string text) => text.Trim().ToLowerInvariant();
}
