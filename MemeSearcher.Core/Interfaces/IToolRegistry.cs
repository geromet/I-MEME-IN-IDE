using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MemeSearcher.Core.Interfaces;

/// <summary>
/// Every external tool locator the app knows about, in one place (#16). IExternalToolLocator has
/// five implementations, but only one could ever be registered against the interface itself under
/// the old unkeyed DI shape - resolution picks the last registration - so App.axaml.cs used to work
/// around it by registering espeak as IExternalToolLocator and every other locator as its own
/// concrete type, with every consumer depending on that concrete type instead of the interface.
/// Keyed DI (App.axaml.cs's ConfigureServices) fixes that at the registration site: every locator
/// is now keyed under its own ToolName against IExternalToolLocator, and a consumer asks for the one
/// it needs via [FromKeyedServices("...")] on its own constructor parameter. This registry is the
/// other half - built from every keyed registration via GetKeyedServices(KeyedService.AnyKey), so
/// there is finally one answer to "what tools does this app need, and which are missing?" instead of
/// a hand-maintained list a forgotten line could silently fall out of.
/// </summary>
public interface IToolRegistry
{
    IReadOnlyList<IExternalToolLocator> Locators { get; }
}

public static class ToolRegistryExtensions
{
    /// <summary>
    /// Locates every registered tool concurrently and reports back by name (#16's own "check all
    /// external tools" diagnostic) - what a first-run or Settings-page tool-status view would call.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, ExternalToolStatus>> LocateAllAsync(
        this IToolRegistry registry, CancellationToken cancellationToken = default)
    {
        var statuses = await Task.WhenAll(registry.Locators.Select(async locator =>
            (locator.ToolName, Status: await locator.LocateAsync(cancellationToken))));

        return statuses.ToDictionary(s => s.ToolName, s => s.Status);
    }
}
