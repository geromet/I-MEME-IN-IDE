namespace MemeSearcher.Infrastructure.Alignment;

/// <summary>What MFA has installed on this machine, and where it keeps it.</summary>
public record MfaModelInventory(
    string ModelsRoot,
    IReadOnlyList<string> AcousticModels,
    IReadOnlyList<string> Dictionaries)
{
    public bool IsEmpty => AcousticModels.Count == 0 && Dictionaries.Count == 0;
}

/// <summary>
/// Discovers the pretrained models MFA has downloaded, by reading its model directory.
///
/// Reads the filesystem rather than parsing `mfa model list`. The on-disk layout
/// (&lt;root&gt;/pretrained_models/{acoustic,dictionary}/&lt;name&gt;.&lt;ext&gt;) is what MFA
/// itself reads and is stable across versions, whereas the CLI prints rich-formatted output -
/// boxes, colour codes - that is presentation, not an interface, and changes without notice.
/// Spawning a process per settings-page render would also be a poor trade for a directory listing.
///
/// The root follows MFA's own resolution order: MFA_ROOT_DIR if set, otherwise the
/// temporary_directory from its global config, otherwise ~/Documents/MFA.
/// </summary>
/// <param name="rootOverride">
/// Explicit models root, bypassing environment/config resolution. Exists so tests can point at a
/// temp directory without mutating process-wide environment variables, which leak across xUnit's
/// parallel test classes.
/// </param>
public class MfaModelProbe(string? rootOverride = null)
{
    private const string AcousticDirectory = "acoustic";
    private const string DictionaryDirectory = "dictionary";

    public MfaModelInventory Discover()
    {
        var root = rootOverride ?? ResolveRoot();
        var pretrained = Path.Combine(root, "pretrained_models");

        return new MfaModelInventory(
            root,
            ListModels(Path.Combine(pretrained, AcousticDirectory)),
            ListModels(Path.Combine(pretrained, DictionaryDirectory)));
    }

    public static string ResolveRoot()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("MFA_ROOT_DIR");
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment;
        }

        var defaultRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "MFA");

        return ReadConfiguredRoot(Path.Combine(defaultRoot, "global_config.yaml")) ?? defaultRoot;
    }

    /// <summary>
    /// Pulls temporary_directory out of MFA's global config. Deliberately a single-key scan rather
    /// than a YAML dependency - one flat scalar is all that is needed, and a parser would be a
    /// dependency added for one line.
    /// </summary>
    private static string? ReadConfiguredRoot(string configPath)
    {
        if (!File.Exists(configPath))
        {
            return null;
        }

        try
        {
            foreach (var line in File.ReadLines(configPath))
            {
                if (!line.StartsWith("temporary_directory:", StringComparison.Ordinal))
                {
                    continue;
                }

                var value = line["temporary_directory:".Length..].Trim().Trim('"', '\'');
                return value.Length > 0 ? value : null;
            }
        }
        catch (IOException)
        {
            // Unreadable config is not fatal - fall back to the default location.
        }

        return null;
    }

    private static IReadOnlyList<string> ListModels(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateFiles(directory)
                .Select(Path.GetFileNameWithoutExtension)
                .OfType<string>()
                .Where(name => name.Length > 0)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (IOException)
        {
            return [];
        }
    }
}
