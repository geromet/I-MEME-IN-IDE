using MemeSearcher.Core.Settings;
using MemeSearcher.Infrastructure.Alignment;
using MemeSearcher.Infrastructure.Settings;
using MemeSearcher.Tests.TestDoubles;

namespace MemeSearcher.Tests.Settings;

public class MfaSettingsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("memesearcher-mfa-models-").FullName;

    private MfaModelProbe ProbeWith(string[] acoustic, string[] dictionaries)
    {
        var acousticDir = Path.Combine(_root, "pretrained_models", "acoustic");
        var dictionaryDir = Path.Combine(_root, "pretrained_models", "dictionary");
        Directory.CreateDirectory(acousticDir);
        Directory.CreateDirectory(dictionaryDir);

        foreach (var name in acoustic)
        {
            File.WriteAllText(Path.Combine(acousticDir, name + ".zip"), "");
        }

        foreach (var name in dictionaries)
        {
            File.WriteAllText(Path.Combine(dictionaryDir, name + ".dict"), "");
        }

        return new MfaModelProbe(_root);
    }

    [Fact]
    public void Discover_ReadsModelNamesFromTheModelDirectory()
    {
        var inventory = ProbeWith(["english_us_arpa", "dutch_mfa"], ["english_us_arpa"]).Discover();

        Assert.Equal(["dutch_mfa", "english_us_arpa"], inventory.AcousticModels);
        Assert.Equal(["english_us_arpa"], inventory.Dictionaries);
        Assert.False(inventory.IsEmpty);
    }

    [Fact]
    public void Discover_ReportsEmptyRatherThanThrowingWhenNothingIsInstalled()
    {
        Assert.True(new MfaModelProbe(Path.Combine(_root, "does-not-exist")).Discover().IsEmpty);
    }

    [Fact]
    public void Choices_OfferWhatIsInstalled()
    {
        var settings = new MfaSettings(ProbeWith(["dutch_mfa", "english_us_arpa"], ["dutch_mfa"]));

        var acoustic = settings.AcousticModelSetting;

        Assert.Equal(["dutch_mfa", "english_us_arpa"], acoustic.EffectiveChoices.Select(c => c.Value));
    }

    /// <summary>
    /// The state a fresh MFA install is in, and the one that produced a realignment that failed
    /// with nothing useful shown. Validation has to name the fix, not just the problem.
    /// </summary>
    [Fact]
    public void Validate_SaysHowToInstallModelsWhenThereAreNone()
    {
        var settings = new MfaSettings(new MfaModelProbe(Path.Combine(_root, "empty")));

        var message = settings.Validate(new InMemorySettingsStore());

        Assert.Contains("No MFA models are installed", message);
    }

    /// <summary>
    /// An explicitly chosen model that is not installed still has to be reported, and the message
    /// has to name the command that fixes it. Note this can no longer happen by *default* - the
    /// default resolves to something installed - so it takes a deliberate choice, or a model
    /// uninstalled after it was chosen.
    /// </summary>
    [Fact]
    public void Validate_NamesTheDownloadCommandForAnExplicitlyChosenMissingModel()
    {
        var settings = new MfaSettings(ProbeWith(["dutch_cv"], ["dutch_cv"]));
        var store = new InMemorySettingsStore();

        store.Set(settings.AcousticModelSetting, "english_us_arpa");
        store.Set(settings.DictionarySetting, "english_us_arpa");

        var message = settings.Validate(store);

        Assert.Contains("mfa model download acoustic english_us_arpa", message);
        Assert.Contains("mfa model download dictionary english_us_arpa", message);
    }

    [Fact]
    public void Validate_IsSilentWhenTheSelectedModelsAreInstalled()
    {
        var settings = new MfaSettings(ProbeWith(["english_us_arpa"], ["english_us_arpa"]));

        Assert.Null(settings.Validate(new InMemorySettingsStore()));
    }

    [Fact]
    public void Choices_KeepDutchAndEnglishDistinct()
    {
        var settings = new MfaSettings(ProbeWith(["dutch_cv", "english_us_arpa"], ["dutch_cv"]));

        Assert.Equal(["dutch_cv"], settings.DictionarySetting.EffectiveChoices.Select(c => c.Value));
    }

    [Fact]
    public void InventoryDescription_ListsInstalledModelsAndWhereTheyLive()
    {
        var settings = new MfaSettings(ProbeWith(["dutch_mfa"], ["dutch_mfa"]));

        var info = settings.Settings.Single(s => s.Key == MfaSettings.Inventory.Key);

        Assert.Equal(SettingKind.Info, info.Kind);
        Assert.Contains("dutch_mfa", info.EffectiveDescription);
        Assert.Contains(_root, info.EffectiveDescription);
    }

    [Fact]
    public void InventoryDescription_GivesTheDownloadCommandsWhenNothingIsInstalled()
    {
        var settings = new MfaSettings(new MfaModelProbe(Path.Combine(_root, "empty")));

        var description = settings.Settings.Single(s => s.Key == MfaSettings.Inventory.Key).EffectiveDescription;

        Assert.Contains("mfa model download acoustic", description);
        Assert.Contains("mfa model download dictionary", description);

        // Model names are not derivable from the language and the app must not pretend otherwise:
        // "dutch_mfa" looks obviously right and does not exist. Point at how to find the real name
        // instead of shipping a guessed mapping that goes stale.
        Assert.Contains("dutch_cv", description);
        Assert.Contains("mfa-models.readthedocs.io", description);
    }

    /// <summary>
    /// The situation that produced a failed realignment with only dutch_cv installed and the
    /// language set to nl: a fixed english_us_arpa default is wrong for everyone until they
    /// install that one specific model.
    /// </summary>
    [Fact]
    public void Default_PrefersAModelMatchingTheConfiguredLanguage()
    {
        var store = new InMemorySettingsStore();
        store.Set(WhisperXSettings.Language, "nl");

        var settings = new MfaSettings(ProbeWith(["dutch_cv", "english_us_arpa"], ["dutch_cv"]), store);
        var acoustic = settings.AcousticModelSetting;

        Assert.Equal("dutch_cv", acoustic.EffectiveDefault);
        Assert.Equal("dutch_cv", store.Get(acoustic));
    }

    [Fact]
    public void Default_IgnoresTheRegionWhenMatchingLanguageToModelName()
    {
        // "English (US)" must match english_us_arpa, not look for "english (us)_".
        var store = new InMemorySettingsStore();
        store.Set(WhisperXSettings.Language, "en-US");

        var settings = new MfaSettings(ProbeWith(["dutch_cv", "english_us_arpa"], ["dutch_cv"]), store);

        Assert.Equal(
            "english_us_arpa",
            settings.AcousticModelSetting.EffectiveDefault);
    }

    [Fact]
    public void Default_FallsBackToTheOnlyInstalledModelWhenNoneMatchTheLanguage()
    {
        var store = new InMemorySettingsStore();
        store.Set(WhisperXSettings.Language, "ja");

        var settings = new MfaSettings(ProbeWith(["dutch_cv"], ["dutch_cv"]), store);

        Assert.Equal(
            "dutch_cv",
            settings.AcousticModelSetting.EffectiveDefault);
    }

    [Fact]
    public void Default_KeepsTheHistoricalConstantWhenNothingIsInstalled()
    {
        var settings = new MfaSettings(new MfaModelProbe(Path.Combine(_root, "empty")), new InMemorySettingsStore());

        Assert.Equal(
            MfaSettings.DefaultModel,
            settings.AcousticModelSetting.EffectiveDefault);
    }

    [Fact]
    public void Default_DoesNotOverrideAnExplicitChoice()
    {
        var store = new InMemorySettingsStore();
        store.Set(WhisperXSettings.Language, "nl");
        var settings = new MfaSettings(ProbeWith(["dutch_cv", "english_us_arpa"], ["dutch_cv"]), store);
        var acoustic = settings.AcousticModelSetting;

        store.Set(acoustic, "english_us_arpa");

        Assert.Equal("english_us_arpa", store.Get(acoustic));
    }

    [Fact]
    public void Validate_IsSilentOnAFreshSetupWhoseOnlyModelsMatchTheLanguage()
    {
        // The end state of the bug: models installed, nothing configured by hand, realign works.
        var store = new InMemorySettingsStore();
        store.Set(WhisperXSettings.Language, "nl");

        var settings = new MfaSettings(ProbeWith(["dutch_cv"], ["dutch_cv"]), store);

        Assert.Null(settings.Validate(store));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }

        GC.SuppressFinalize(this);
    }
}
