using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MemeSearcher.Infrastructure.Database;

/// <summary>
/// Lets `dotnet ef migrations` run against this class library directly, without a startup project.
/// Not used at application runtime - see DI registration in the UI project for the real connection string.
/// </summary>
public class MemeSearcherDbContextFactory : IDesignTimeDbContextFactory<MemeSearcherDbContext>
{
    public MemeSearcherDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<MemeSearcherDbContext>();
        optionsBuilder.UseSqlite("Data Source=memesearcher.design.db");

        return new MemeSearcherDbContext(optionsBuilder.Options);
    }
}
