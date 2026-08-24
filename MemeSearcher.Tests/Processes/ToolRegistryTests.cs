using MemeSearcher.Core.Interfaces;
using MemeSearcher.Core.Settings;
using MemeSearcher.Infrastructure.Ffmpeg;
using MemeSearcher.Infrastructure.Phonetics;
using MemeSearcher.Infrastructure.Processes;
using MemeSearcher.Infrastructure.Settings;
using MemeSearcher.Infrastructure.Transcription;
using MemeSearcher.Tests.TestDoubles;
using Microsoft.Extensions.DependencyInjection;

namespace MemeSearcher.Tests.Processes;

/// <summary>
/// #16: the registry itself is a plain holder, so these tests exercise the thing it exists to
/// enable - asking "what tools does this app need, and which are missing?" as a single call instead
/// of five hand-maintained ones. Uses real locators (ffprobe is genuinely installed in this
/// environment; espeak-ng's own tests elsewhere already establish it usually is too), not fakes,
/// since the point is proving LocateAllAsync actually calls through to each one.
/// </summary>
public class ToolRegistryTests
{
    [Fact]
    public async Task LocateAllAsync_ReportsEveryLocatorKeyedByItsToolName()
    {
        var ffprobe = new FFprobeToolLocator();
        var ffmpeg = new FFmpegToolLocator();
        IToolRegistry registry = new ToolRegistry([ffprobe, ffmpeg]);

        var statuses = await registry.LocateAllAsync();

        Assert.Equal(2, statuses.Count);
        Assert.True(statuses.ContainsKey(ffprobe.ToolName));
        Assert.True(statuses.ContainsKey(ffmpeg.ToolName));
        // ffprobe ships with the system FFmpeg package this environment has installed (established
        // by FFprobeToolLocatorTests) - a real, not faked, positive result.
        Assert.True(statuses[ffprobe.ToolName].IsInstalled);
    }

    [Fact]
    public void Locators_ReturnsExactlyWhatWasConstructedWith()
    {
        var locator = new FFprobeToolLocator();
        IToolRegistry registry = new ToolRegistry([locator]);

        var only = Assert.Single(registry.Locators);
        Assert.Same(locator, only);
    }

    /// <summary>
    /// #16's actual point, proven against a real DI container rather than the plain holder above:
    /// every locator is keyed against IExternalToolLocator (App.axaml.cs's real registration
    /// pattern), the registry built from GetKeyedServices(KeyedService.AnyKey) contains all of them
    /// with no hand-maintained list to fall out of sync, and a consumer's own
    /// [FromKeyedServices("...")] constructor parameter resolves through auto-wiring rather than
    /// needing its concrete locator type registered separately - the exact "consumers depend on
    /// concrete locator types, so the interface earns nothing" complaint #16 opened with.
    /// </summary>
    [Fact]
    public void KeyedRegistration_WiresEveryLocatorIntoTheRegistryAndAutoWiresAConsumer()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISettingsStore>(new InMemorySettingsStore());
        services.AddSingleton<ExternalToolSettings>();

        services.AddKeyedSingleton<IExternalToolLocator>("espeak-ng", (sp, _) => new EspeakToolLocator(
            sp.GetRequiredService<ISettingsStore>(), sp.GetRequiredService<ExternalToolSettings>()));
        services.AddKeyedSingleton<IExternalToolLocator>("whisperx", (sp, _) => new WhisperXToolLocator(
            sp.GetRequiredService<ISettingsStore>(), sp.GetRequiredService<ExternalToolSettings>()));
        services.AddKeyedSingleton<IExternalToolLocator>("mfa", (sp, _) => new MfaToolLocator(
            sp.GetRequiredService<ISettingsStore>(), sp.GetRequiredService<ExternalToolSettings>()));
        services.AddKeyedSingleton<IExternalToolLocator>("ffprobe", (sp, _) => new FFprobeToolLocator(
            sp.GetRequiredService<ISettingsStore>(), sp.GetRequiredService<ExternalToolSettings>()));
        services.AddKeyedSingleton<IExternalToolLocator>("ffmpeg", (sp, _) => new FFmpegToolLocator(
            sp.GetRequiredService<ISettingsStore>(), sp.GetRequiredService<ExternalToolSettings>()));

        services.AddSingleton<IToolRegistry>(sp => new ToolRegistry(
            [.. sp.GetKeyedServices<IExternalToolLocator>(KeyedService.AnyKey)]));

        services.AddSingleton<IPhonemizer, EspeakPhonemizer>();

        using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<IToolRegistry>();
        var toolNames = registry.Locators.Select(l => l.ToolName).ToList();
        Assert.Equal(5, toolNames.Distinct().Count());
        Assert.Contains("espeak-ng", toolNames);
        Assert.Contains("whisperx", toolNames);
        Assert.Contains("mfa", toolNames);
        Assert.Contains("ffprobe", toolNames);
        Assert.Contains("ffmpeg", toolNames);

        // Auto-wired via [FromKeyedServices("espeak-ng")] on EspeakPhonemizer's own constructor
        // parameter, not a concrete EspeakToolLocator registration - proves the interface is what's
        // actually satisfying the dependency now.
        var phonemizer = provider.GetRequiredService<IPhonemizer>();
        Assert.IsType<EspeakPhonemizer>(phonemizer);
    }
}
