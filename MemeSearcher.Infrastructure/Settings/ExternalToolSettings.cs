using MemeSearcher.Core.Settings;

namespace MemeSearcher.Infrastructure.Settings;

/// <summary>One external tool the app shells out to, and how it is described in Settings.</summary>
public record ExternalToolDescriptor(string Key, string DisplayName, string ExecutableName, string Purpose);

/// <summary>
/// Explicit executable paths and per-tool environment overrides for the external tools this app
/// shells out to.
///
/// This exists because searching PATH is not enough for how these tools are actually installed.
/// MFA's documented installation is a conda environment, and a conda env is only on PATH while it
/// is activated - which a GUI app launched from an IDE or a desktop launcher never inherits. The
/// tool is installed, runs fine in the user's shell, and is invisible to the app. "Not found on
/// PATH" is then a misleading error: nothing is wrong with the installation.
///
/// The environment override exists for the failure right behind that one. A conda env whose Python
/// version matches the user's system Python will import packages from ~/.local/lib/pythonX.Y
/// ahead of its own site-packages, so the env's pinned dependencies get shadowed by whatever the
/// user pip-installed years ago. Setting PYTHONNOUSERSITE=1 fixes it. That is a per-installation
/// fact the app cannot infer, so it has to be settable - and it must not be forced on by default,
/// because a tool genuinely installed with `pip install --user` lives in exactly the directory
/// that setting hides.
/// </summary>
public class ExternalToolSettings : ISettingsCategory
{
    public const string CategoryName = "External tools";

    public static IReadOnlyList<ExternalToolDescriptor> Tools { get; } =
    [
        new("espeak-ng", "espeak-ng", "espeak-ng", "Phonemizes transcripts and search queries."),
        new("whisperx", "WhisperX", "whisperx", "Transcribes and aligns imported media."),
        new("mfa", "Montreal Forced Aligner", "mfa", "Phone-level realignment. Usually installed in a conda environment."),
        new("ffmpeg", "FFmpeg", "ffmpeg", "Extracts result clips."),
        new("ffprobe", "ffprobe", "ffprobe", "Reads media duration on import."),
        new("yt-dlp", "yt-dlp", "yt-dlp", "Enumerates and downloads YouTube channels/playlists (#27)."),
    ];

    private static readonly Dictionary<string, (SettingDefinition Path, SettingDefinition Environment)> Definitions =
        Tools.ToDictionary(t => t.Key, t => (
            Path: new SettingDefinition(
                Key: $"tools.{t.Key}.path",
                Category: CategoryName,
                DisplayName: $"{t.DisplayName} path",
                Description: $"{t.Purpose} Leave empty to search PATH. Set the full path to the "
                             + $"executable when it lives somewhere PATH does not reach - a conda "
                             + $"environment, for example.",
                Kind: SettingKind.Text,
                DefaultValue: ""),
            Environment: new SettingDefinition(
                Key: $"tools.{t.Key}.environment",
                Category: CategoryName,
                DisplayName: $"{t.DisplayName} environment",
                Description: "Environment variables for this tool, as KEY=VALUE separated by "
                             + "semicolons. For a conda-installed Python tool picking up the wrong "
                             + "packages from ~/.local, PYTHONNOUSERSITE=1 is usually the fix.",
                Kind: SettingKind.Text,
                DefaultValue: "")));

    public string Name => CategoryName;

    public string Description =>
        "Where to find the command-line tools this app runs, for installations PATH does not cover.";

    public int Order => 20;

    public IReadOnlyList<SettingDefinition> Settings { get; } =
        Tools.SelectMany(t => new[] { Definitions[t.Key].Path, Definitions[t.Key].Environment }).ToArray();

    public static SettingDefinition PathSetting(string toolKey) => Definitions[toolKey].Path;

    public static SettingDefinition EnvironmentSetting(string toolKey) => Definitions[toolKey].Environment;

    /// <summary>The configured executable path for a tool, or null when it should be found on PATH.</summary>
    public string? GetConfiguredPath(ISettingsStore store, string toolKey)
    {
        if (!Definitions.TryGetValue(toolKey, out var definitions))
        {
            return null;
        }

        var value = store.Get(definitions.Path).Trim();
        return value.Length == 0 ? null : value;
    }

    public IReadOnlyDictionary<string, string> GetEnvironment(ISettingsStore store, string toolKey) =>
        Definitions.TryGetValue(toolKey, out var definitions)
            ? ParseEnvironment(store.Get(definitions.Environment))
            : new Dictionary<string, string>();

    /// <summary>
    /// Parses "KEY=VALUE;KEY2=VALUE2". Malformed entries are skipped rather than throwing - this
    /// is hand-typed text, and refusing to launch a tool because of a stray semicolon would be
    /// worse than ignoring the fragment.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ParseEnvironment(string value)
    {
        var result = new Dictionary<string, string>();

        foreach (var entry in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = entry.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = entry[..separator].Trim();
            if (key.Length > 0)
            {
                result[key] = entry[(separator + 1)..].Trim();
            }
        }

        return result;
    }

    public string? Validate(ISettingsStore store)
    {
        var missing = Tools
            .Select(t => (t.DisplayName, Path: GetConfiguredPath(store, t.Key)))
            .Where(t => t.Path is not null && !File.Exists(t.Path))
            .Select(t => $"{t.DisplayName}: '{t.Path}'")
            .ToList();

        return missing.Count == 0
            ? null
            : "These configured tool paths do not exist: " + string.Join("; ", missing) + ".";
    }
}
