using System.Net;
using System.Net.Sockets;
using System.Text;
using MemeSearcher.Core.Models;
using MemeSearcher.Infrastructure.Processes;
using MemeSearcher.Infrastructure.Settings;
using MemeSearcher.Infrastructure.YtDlp;
using MemeSearcher.Tests.TestDoubles;

namespace MemeSearcher.Tests.YtDlp;

/// <summary>
/// Hermetic acceptance proof for the real yt-dlp + ffmpeg process boundary used by #27.
/// CI installs both tools. Media is served only from loopback, so the test exercises the actual
/// executable/argument/post-processing path without depending on YouTube or any external network.
/// </summary>
public sealed class YtDlpDownloadProviderRealToolTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ytdlp-realtool-{Guid.NewGuid():N}");

    [Fact]
    public async Task DownloadAsync_RealYtDlpAndFfmpeg_ProducesReportedMp3WithoutShellInterpretation()
    {
        Directory.CreateDirectory(_dir);
        await using var server = new LoopbackMediaServer(CreateSilentWav());

        var locator = new YtDlpToolLocator();
        var located = await locator.LocateAsync();
        Assert.True(located.IsInstalled, located.Error);

        var store = new InMemorySettingsStore();
        store.Set(YtDlpSettings.DownloadLocation, _dir);
        store.Set(YtDlpSettings.MediaKind, YtDlpSettings.AudioValue);
        var provider = new YtDlpDownloadProvider(locator, new YtDlpSettings(), store);

        // If the URL were ever concatenated into a shell command, the semicolon and ${IFS}
        // expansion would create this marker. ArgumentList + UseShellExecute=false must keep the
        // entire value opaque and hand it to yt-dlp as one URL argument instead.
        var marker = Path.Combine(Path.GetTempPath(), $"ytdlp-shell-marker-{Guid.NewGuid():N}");
        File.Delete(marker);
        var url = $"{server.Url}?x=1;touch${{IFS}}{marker}";

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var result = await provider.DownloadAsync(url, timeout.Token);

        Assert.Equal(YtDlpMediaKind.Audio, result.MediaKind);
        Assert.Equal(".mp3", Path.GetExtension(result.FilePath), ignoreCase: true);
        Assert.True(File.Exists(result.FilePath));
        Assert.True(new FileInfo(result.FilePath).Length > 0);
        Assert.False(File.Exists(marker), "The untrusted URL was interpreted by a shell.");
    }

    private static byte[] CreateSilentWav()
    {
        const int sampleRate = 8_000;
        const short channels = 1;
        const short bitsPerSample = 16;
        const int sampleCount = sampleRate / 10;
        var pcm = new byte[sampleCount * channels * (bitsPerSample / 8)];

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + pcm.Length);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1); // PCM
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * (bitsPerSample / 8));
        writer.Write((short)(channels * (bitsPerSample / 8)));
        writer.Write(bitsPerSample);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(pcm.Length);
        writer.Write(pcm);
        writer.Flush();
        return stream.ToArray();
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private sealed class LoopbackMediaServer : IAsyncDisposable
    {
        private readonly byte[] _content;
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _serveTask;

        public LoopbackMediaServer(byte[] content)
        {
            _content = content;
            _listener.Start();
            var endpoint = (IPEndPoint)_listener.LocalEndpoint;
            Url = $"http://127.0.0.1:{endpoint.Port}/fixture.wav";
            _serveTask = ServeAsync();
        }

        public string Url { get; }

        private async Task ServeAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    await ServeClientAsync(client, _stop.Token);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
            }
            catch (ObjectDisposedException) when (_stop.IsCancellationRequested)
            {
            }
        }

        private async Task ServeClientAsync(TcpClient client, CancellationToken cancellationToken)
        {
            using (client)
            await using (var stream = client.GetStream())
            using (var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, leaveOpen: true))
            {
                string? requestLine = await reader.ReadLineAsync(cancellationToken);
                if (requestLine is null)
                {
                    return;
                }

                string? line;
                do
                {
                    line = await reader.ReadLineAsync(cancellationToken);
                } while (!string.IsNullOrEmpty(line));

                var isHead = requestLine.StartsWith("HEAD ", StringComparison.Ordinal);
                var headers = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK\r\n" +
                    "Content-Type: audio/wav\r\n" +
                    $"Content-Length: {_content.Length}\r\n" +
                    "Accept-Ranges: bytes\r\n" +
                    "Connection: close\r\n\r\n");
                await stream.WriteAsync(headers, cancellationToken);
                if (!isHead)
                {
                    await stream.WriteAsync(_content, cancellationToken);
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Stop();
            try
            {
                await _serveTask;
            }
            finally
            {
                _stop.Dispose();
            }
        }
    }
}
