using System;
using System.IO;

namespace MemeSearcher.Infrastructure.Database;

public static class DatabasePathProvider
{
    public static string GetDefaultDatabasePath()
    {
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MemeSearcher");

        Directory.CreateDirectory(appDataDir);

        return Path.Combine(appDataDir, "memesearcher.db");
    }

    public static string GetDefaultConnectionString() => $"Data Source={GetDefaultDatabasePath()}";
}
