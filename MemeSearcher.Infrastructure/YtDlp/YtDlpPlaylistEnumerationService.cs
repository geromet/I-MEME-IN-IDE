using System.Diagnostics;
using System.Text.Json;
using MemeSearcher.Core.Interfaces;
using MemeSearcher.Infrastructure.Processes;
using Microsoft.Extensions.DependencyInjection;

namespace MemeSearcher.Infrastructure.YtDlp;

/// <summary>
/// Enumerates a channel or playlist URL to its videos without downloading anything (#27's own
/// exit criterion: "a channel/playlist URL enumerates to a reviewable list before anything
/// downloads"). Uses `yt-dlp --flat-playlist --dump-json`, which is fast precisely because it
/// skips per-video extraction - each stdout line is one JSON object, one per video.
///
/// ParseEntries is a separate, pure, publicly testable method so the JSON-shape logic can be
/// verified against real captured yt-dlp output offline, without this service's tests depending on
/// live network access to YouTube (which would be flaky for reasons that have nothing to do with
/// this code - YouTube's own bot detection, video availability changing, yt-dlp itself breaking
/// against a site change).
/// </summary>
public class YtDlpPlaylistEnumerationService([FromKeyedServices("yt-dlp")] IExternalToolLocator toolLocator)
{
    public async Task<IReadOnlyList<YtDlpVideoEntry>> EnumerateAsync(string url, CancellationToken cancellationToken = default)
    {
        var status = await toolLocator.LocateAsync(cancellationToken);
        if (!status.IsInstalled)
        {
            throw new InvalidOperationException($"yt-dlp is not available: {status.Error}");
        }

        var startInfo = new ProcessStartInfo(status.ExecutablePath!)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("--flat-playlist");
        startInfo.ArgumentList.Add("--dump-json");
        startInfo.ArgumentList.Add(url);

        using var process = Process.Start(startInfo.ApplyToolEnvironment(status))
            ?? throw new InvalidOperationException($"Failed to start '{status.ExecutablePath}'.");

        // Both streams drained concurrently with waiting for exit - a redirected pipe nobody reads
        // fills its OS buffer and the process blocks forever on write() (the exact deadlock #33
        // found in MfaAlignmentProvider's identical pattern).
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await ProcessRunner.WaitForExitAndKillOnCancelAsync(process, cancellationToken);
        var stdout = await stdoutTask;

        if (process.ExitCode != 0)
        {
            var stderr = await stderrTask;
            var lastLine = stderr.Split('\n').LastOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "no output";
            throw new InvalidOperationException($"yt-dlp exited with code {process.ExitCode}: {lastLine}");
        }

        return ParseEntries(stdout);
    }

    /// <summary>
    /// Parses `--flat-playlist --dump-json` output (one JSON object per line). A line that fails to
    /// parse, or is missing "id"/"title", is skipped rather than failing the whole enumeration - one
    /// malformed row (yt-dlp mixing in a warning on stdout, say) shouldn't lose every other video in
    /// a large channel.
    /// </summary>
    public static IReadOnlyList<YtDlpVideoEntry> ParseEntries(string stdout)
    {
        var entries = new List<YtDlpVideoEntry>();

        foreach (var line in stdout.Split('\n'))
        {
            if (line.Trim().Length == 0)
            {
                continue;
            }

            YtDlpVideoEntry? entry;
            try
            {
                entry = ParseLine(line);
            }
            catch (JsonException)
            {
                continue;
            }

            if (entry is not null)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    private static YtDlpVideoEntry? ParseLine(string line)
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;

        if (!root.TryGetProperty("id", out var idProperty) || idProperty.GetString() is not { Length: > 0 } id)
        {
            return null;
        }

        if (!root.TryGetProperty("title", out var titleProperty) || titleProperty.GetString() is not { Length: > 0 } title)
        {
            return null;
        }

        // "playlist_channel" is populated for both a channel URL and a playlist URL; the top-level
        // "channel" field only exists for playlist URLs (a channel URL's own rows carry no "channel"
        // key at all) - verified against real yt-dlp output, not assumed.
        var channel = GetOptionalString(root, "playlist_channel") ?? GetOptionalString(root, "channel");

        var url = GetOptionalString(root, "url") ?? GetOptionalString(root, "webpage_url");
        if (url is null)
        {
            return null;
        }

        return new YtDlpVideoEntry(id, title, channel, url);
    }

    private static string? GetOptionalString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}
