using System.Diagnostics;
using System.Text.Json;
using MemeSearcher.Core.Interfaces;
using MemeSearcher.Core.Settings;
using MemeSearcher.Infrastructure.Processes;
using MemeSearcher.Infrastructure.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace MemeSearcher.Infrastructure.YtDlp;

/// <summary>
/// Downloads a single video to local disk (#27) - the file MediaIngestionService.ImportAsync then
/// consumes unchanged, since that service only ever operates on a local path already on disk. This
/// is deliberately per-video, not per-playlist: YtDlpImportPlanner has already decided which videos
/// are new, and orchestration downloads them one at a time so a single failure doesn't abort a
/// whole batch (see YtDlpImportFailure).
/// </summary>
public class YtDlpDownloadProvider(
    [FromKeyedServices("yt-dlp")] IExternalToolLocator toolLocator,
    YtDlpSettings settings,
    ISettingsStore settingsStore)
{
    public async Task<YtDlpDownloadResult> DownloadAsync(string videoUrl, CancellationToken cancellationToken = default)
    {
        var status = await toolLocator.LocateAsync(cancellationToken);
        if (!status.IsInstalled)
        {
            throw new InvalidOperationException($"yt-dlp is not available: {status.Error}");
        }

        var downloadDir = settings.ResolveDownloadDirectory(settingsStore);
        Directory.CreateDirectory(downloadDir);

        var startInfo = new ProcessStartInfo(status.ExecutablePath!)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("--no-playlist");
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add(Path.Combine(downloadDir, "%(id)s.%(ext)s"));
        startInfo.ArgumentList.Add("--print-json");

        if (settings.ResolveMediaKind(settingsStore) == Core.Models.YtDlpMediaKind.Audio)
        {
            startInfo.ArgumentList.Add("-x");
            startInfo.ArgumentList.Add("--audio-format");
            startInfo.ArgumentList.Add("mp3");
        }
        else
        {
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add("bestvideo*+bestaudio/best");
            startInfo.ArgumentList.Add("--merge-output-format");
            startInfo.ArgumentList.Add("mp4");
        }

        startInfo.ArgumentList.Add(videoUrl);

        using var process = Process.Start(startInfo.ApplyToolEnvironment(status))
            ?? throw new InvalidOperationException($"Failed to start '{status.ExecutablePath}'.");

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

        return ParseResult(stdout, downloadDir);
    }

    /// <summary>
    /// Public and static for the same reason as YtDlpPlaylistEnumerationService.ParseEntries: testable
    /// against captured real --print-json output with no live download involved. The final file path
    /// is resolved by globbing "<id>.*" in the download directory rather than trusting a JSON field,
    /// since postprocessing (audio extraction, video+audio merge) changes the extension in ways that
    /// differ across yt-dlp versions and this glob is correct regardless of which one ran.
    /// </summary>
    public static YtDlpDownloadResult ParseResult(string stdout, string downloadDir)
    {
        var lastLine = stdout.Split('\n').LastOrDefault(l => l.Trim().Length > 0)
            ?? throw new InvalidOperationException("yt-dlp produced no JSON output.");

        using var document = JsonDocument.Parse(lastLine);
        var root = document.RootElement;

        var id = root.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("yt-dlp's output JSON had no 'id' field.");
        var title = GetOptionalString(root, "title") ?? id;
        var channel = GetOptionalString(root, "channel") ?? GetOptionalString(root, "uploader");
        var uploadDate = GetOptionalString(root, "upload_date") is { Length: 8 } raw
            ? DateOnly.ParseExact(raw, "yyyyMMdd")
            : (DateOnly?)null;

        var filePath = Directory.EnumerateFiles(downloadDir, $"{id}.*").FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"yt-dlp reported success but no output file '{id}.*' was found in '{downloadDir}'.");

        return new YtDlpDownloadResult(filePath, id, title, channel, uploadDate);
    }

    private static string? GetOptionalString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}
