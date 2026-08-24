using MemeSearcher.Infrastructure.Database;
using MemeSearcher.Infrastructure.Library;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MemeSearcher.Tests.Catalogs;

/// <summary>
/// Milestone 17 (#20): CatalogsViewModel.ApplyToSearchAsync sets LibraryService.ActiveCatalogLabel,
/// and SearchViewModel reads the same property to build "Catalog: name (n)" - this only works if
/// every consumer resolves the *same* LibraryService instance. LibraryService is registered
/// AddScoped (App.axaml.cs), which is only safe here because App never wraps ViewModel resolution
/// in Services.CreateScope() (the one CreateScope() call is for the startup migration and is
/// disposed immediately) - every panel/tab is resolved straight off the root provider, and a
/// Scoped service resolved directly from the root provider (no created scope) behaves like a
/// singleton for that provider's lifetime. This test locks in that assumption against App.axaml.cs's
/// actual registration shape, independent of the real App class (which exposes no test seam).
/// </summary>
public class LibraryServiceSharingTests
{
    [Fact]
    public void ScopedLibraryService_ResolvedTwiceFromTheRootProvider_IsTheSameInstance()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<MemeSearcherDbContext>(o => o.UseSqlite("Data Source=:memory:"));
        services.AddScoped<LibraryService>();

        // Mirrors App.axaml.cs: SearchViewModel-style consumers are minted via a factory delegate
        // captured against the root IServiceProvider, exactly like `Func<SearchViewModel>` there.
        services.AddSingleton<Func<LibraryService>>(sp => sp.GetRequiredService<LibraryService>);
        services.AddSingleton<CatalogsPanelStandIn>();

        using var provider = services.BuildServiceProvider();

        var fromPanel = provider.GetRequiredService<CatalogsPanelStandIn>().Library;
        var factory = provider.GetRequiredService<Func<LibraryService>>();
        var fromTabOne = factory();
        var fromTabTwo = factory();

        Assert.Same(fromPanel, fromTabOne);
        Assert.Same(fromTabOne, fromTabTwo);
    }

    private class CatalogsPanelStandIn(LibraryService library)
    {
        public LibraryService Library { get; } = library;
    }
}
