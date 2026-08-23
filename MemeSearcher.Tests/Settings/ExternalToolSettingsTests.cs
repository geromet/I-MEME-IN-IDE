using MemeSearcher.Infrastructure.Settings;
using MemeSearcher.Tests.TestDoubles;

namespace MemeSearcher.Tests.Settings;

public class ExternalToolSettingsTests
{
    [Fact]
    public void ParseEnvironment_ReadsSemicolonSeparatedPairs()
    {
        var result = ExternalToolSettings.ParseEnvironment("PYTHONNOUSERSITE=1; FOO=bar");

        Assert.Equal("1", result["PYTHONNOUSERSITE"]);
        Assert.Equal("bar", result["FOO"]);
    }

    [Fact]
    public void ParseEnvironment_KeepsEqualsSignsInsideValues()
    {
        // Paths and flags legitimately contain '=', so only the first separator counts.
        var result = ExternalToolSettings.ParseEnvironment("OPTS=--a=1 --b=2");

        Assert.Equal("--a=1 --b=2", result["OPTS"]);
    }

    [Fact]
    public void ParseEnvironment_SkipsMalformedEntriesRatherThanThrowing()
    {
        // Hand-typed text: refusing to launch a tool over a stray semicolon would be worse than
        // ignoring the fragment.
        var result = ExternalToolSettings.ParseEnvironment("PYTHONNOUSERSITE=1;;garbage;=novalue;");

        Assert.Single(result);
        Assert.Equal("1", result["PYTHONNOUSERSITE"]);
    }

    [Fact]
    public void ParseEnvironment_ReturnsEmptyForEmptyInput()
    {
        Assert.Empty(ExternalToolSettings.ParseEnvironment(""));
    }

    [Fact]
    public void GetConfiguredPath_TreatsBlankAsUnset()
    {
        var store = new InMemorySettingsStore();
        var settings = new ExternalToolSettings();

        Assert.Null(settings.GetConfiguredPath(store, "mfa"));

        store.Set(ExternalToolSettings.PathSetting("mfa"), "   ");
        Assert.Null(settings.GetConfiguredPath(store, "mfa"));
    }

    [Fact]
    public void Validate_ReportsAConfiguredPathThatDoesNotExist()
    {
        var store = new InMemorySettingsStore();
        store.Set(ExternalToolSettings.PathSetting("mfa"), "/nonexistent/mfa");

        Assert.Contains("/nonexistent/mfa", new ExternalToolSettings().Validate(store));
    }

    [Fact]
    public void Validate_IsSilentWhenNothingIsConfigured()
    {
        Assert.Null(new ExternalToolSettings().Validate(new InMemorySettingsStore()));
    }

    [Fact]
    public void EverySettingKeyIsUnique()
    {
        var settings = new ExternalToolSettings().Settings;

        Assert.Equal(settings.Count, settings.Select(s => s.Key).Distinct().Count());
        Assert.Equal(ExternalToolSettings.Tools.Count * 2, settings.Count);
    }
}
