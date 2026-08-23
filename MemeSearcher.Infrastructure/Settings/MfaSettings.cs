using MemeSearcher.Core.Settings;
using MemeSearcher.Core.Languages;
using MemeSearcher.Infrastructure.Alignment;

namespace MemeSearcher.Infrastructure.Settings;

/// <summary>
/// Which pretrained MFA models to align with, chosen from what is actually installed.
///
/// The model names were previously a hardcoded "english_us_arpa" constant, and MFA does not
/// download models on first use. So a realignment on a machine with no models - the default state
/// after installing MFA - fails inside MFA with a model-not-found error, and a Dutch corpus
/// aligned against an English model is wrong in a quieter way still. Neither is discoverable from
/// inside the app.
///
/// This category therefore does three things: offers only models that exist on this machine, says
/// so plainly when none do, and gives the exact commands to install them.
/// </summary>
public class MfaSettings(MfaModelProbe modelProbe, ISettingsStore? settingsStore = null) : ISettingsCategory
{
    public const string CategoryName = "Forced alignment (MFA)";

    public const string DefaultModel = "english_us_arpa";

    public static readonly SettingDefinition AcousticModel = new(
        Key: "mfa.acousticModel",
        Category: CategoryName,
        DisplayName: "Acoustic model",
        Description: "The pretrained acoustic model MFA aligns with. Must match the language of "
                     + "the media being aligned.",
        Kind: SettingKind.Choice,
        DefaultValue: DefaultModel);

    public static readonly SettingDefinition Dictionary = new(
        Key: "mfa.dictionary",
        Category: CategoryName,
        DisplayName: "Pronunciation dictionary",
        Description: "The pronunciation dictionary MFA aligns with. Normally paired with the "
                     + "acoustic model of the same name.",
        Kind: SettingKind.Choice,
        DefaultValue: DefaultModel);

    public static readonly SettingDefinition Inventory = new(
        Key: "mfa.inventory",
        Category: CategoryName,
        DisplayName: "Installed models",
        Description: "",
        Kind: SettingKind.Info,
        DefaultValue: "");

    public MfaSettings() : this(new MfaModelProbe())
    {
    }

    public string Name => CategoryName;

    public string Description =>
        "Phone-level realignment. MFA does not download models on first use - they have to be "
        + "installed once, per language.";

    public int Order => 30;

    /// <summary>
    /// The live definitions, carrying the choice/default/description providers. Everything that
    /// reads or validates an MFA setting must go through these rather than the bare static fields
    /// - those are declarations without behaviour, and reading one gets the fixed fallback default
    /// instead of the resolved one.
    /// </summary>
    public IReadOnlyList<SettingDefinition> Settings => Built.All;

    public SettingDefinition AcousticModelSetting => Built.Acoustic;

    public SettingDefinition DictionarySetting => Built.Dictionary;

    private (IReadOnlyList<SettingDefinition> All, SettingDefinition Acoustic, SettingDefinition Dictionary)? _built;

    private (IReadOnlyList<SettingDefinition> All, SettingDefinition Acoustic, SettingDefinition Dictionary) Built =>
        _built ??= BuildSettings();

    /// <summary>
    /// Built in the constructor rather than as static fields because the choice and description
    /// providers close over this instance's probe.
    /// </summary>
    private (IReadOnlyList<SettingDefinition> All, SettingDefinition Acoustic, SettingDefinition Dictionary) BuildSettings()
    {
        var inventory = Inventory with { DynamicDescription = DescribeInventory };

        var acoustic = AcousticModel with
        {
            ChoicesProvider = () => ChoicesFor(i => i.AcousticModels),
            DefaultValueProvider = () => ResolveDefaultModel(i => i.AcousticModels),
        };

        var dictionary = Dictionary with
        {
            ChoicesProvider = () => ChoicesFor(i => i.Dictionaries),
            DefaultValueProvider = () => ResolveDefaultModel(i => i.Dictionaries),
        };

        return ([inventory, acoustic, dictionary], acoustic, dictionary);
    }

    /// <summary>
    /// Offers what is installed, and keeps a name that is stored but missing in the list too -
    /// dropping it would silently reassign the user's choice and hide the very problem Validate is
    /// about to report.
    /// </summary>
    private IReadOnlyList<SettingChoice> ChoicesFor(Func<MfaModelInventory, IReadOnlyList<string>> select)
    {
        var installed = select(modelProbe.Discover());

        return installed.Count > 0
            ? installed.Select(m => new SettingChoice(m, m)).ToList()
            : [new SettingChoice(DefaultModel, $"{DefaultModel} (not installed)")];
    }

    /// <summary>
    /// Picks a sensible model when the user has not chosen one.
    ///
    /// A fixed "english_us_arpa" default is actively unhelpful: it is wrong for anyone not working
    /// in English, and it is wrong for *everyone* until they install that specific model - so the
    /// first realignment on any fresh setup fails, even when the machine has exactly one usable
    /// model sitting there.
    ///
    /// Preference order: a model matching the configured language, then the only installed model
    /// if there is just one, then the historical constant so behaviour is unchanged when nothing
    /// is installed and there is nothing better to say.
    /// </summary>
    private string ResolveDefaultModel(Func<MfaModelInventory, IReadOnlyList<string>> select)
    {
        var installed = select(modelProbe.Discover());

        if (installed.Count == 0)
        {
            return DefaultModel;
        }

        var hint = LanguageHint();
        var matching = installed.FirstOrDefault(m => m.StartsWith(hint + "_", StringComparison.OrdinalIgnoreCase));

        return matching ?? (installed.Count == 1 ? installed[0] : DefaultModel);
    }

    /// <summary>
    /// The configured language as a model-name prefix: "nl" -> "dutch", matching the naming MFA
    /// uses (dutch_cv, german_mfa). Derived from the catalogue's display name rather than a second
    /// hand-maintained mapping - which is exactly the kind of table that goes stale and produces a
    /// confident wrong answer like "dutch_mfa", a name that does not exist.
    /// </summary>
    private string LanguageHint()
    {
        var languageId = settingsStore?.Get(WhisperXSettings.Language) ?? LanguageCatalog.Default.Id;
        var displayName = LanguageCatalog.TryGet(languageId, out var option)
            ? option.DisplayName
            : LanguageCatalog.Default.DisplayName;

        var withoutRegion = displayName.Split('(')[0].Trim();
        return withoutRegion.ToLowerInvariant();
    }

    private string DescribeInventory()
    {
        var inventory = modelProbe.Discover();

        if (inventory.IsEmpty)
        {
            return "No MFA models are installed, so realignment will fail. Install a matched "
                   + "acoustic model and dictionary for your language:\n\n"
                   + $"    mfa model download acoustic {DefaultModel}\n"
                   + $"    mfa model download dictionary {DefaultModel}\n\n"
                   + "Model names are not predictable from the language - Dutch is dutch_cv, not "
                   + "dutch_mfa, and the _cv and _mfa families cover different languages. Asking "
                   + "for a name that does not exist makes MFA print the full available list, "
                   + "which is the quickest way to find the right one. Full catalogue: "
                   + "https://mfa-models.readthedocs.io/\n\n"
                   + $"Models are stored under {inventory.ModelsRoot}.";
        }

        return $"Acoustic: {Describe(inventory.AcousticModels)}\n"
               + $"Dictionaries: {Describe(inventory.Dictionaries)}\n\n"
               + $"Stored under {inventory.ModelsRoot}. Install more with "
               + "`mfa model download acoustic <name>` and `mfa model download dictionary <name>`.";

        static string Describe(IReadOnlyList<string> models) =>
            models.Count > 0 ? string.Join(", ", models) : "none installed";
    }

    public string? Validate(ISettingsStore store)
    {
        var inventory = modelProbe.Discover();

        if (inventory.IsEmpty)
        {
            return "No MFA models are installed - realignment will fail until at least one acoustic "
                   + "model and one dictionary are downloaded. See \"Installed models\" below for the "
                   + "commands.";
        }

        var problems = new List<string>();
        var acoustic = store.Get(AcousticModelSetting);
        var dictionary = store.Get(DictionarySetting);

        if (!inventory.AcousticModels.Contains(acoustic))
        {
            problems.Add($"acoustic model '{acoustic}' is not installed (mfa model download acoustic {acoustic})");
        }

        if (!inventory.Dictionaries.Contains(dictionary))
        {
            problems.Add($"dictionary '{dictionary}' is not installed (mfa model download dictionary {dictionary})");
        }

        return problems.Count == 0 ? null : "MFA: " + string.Join("; ", problems) + ".";
    }
}
