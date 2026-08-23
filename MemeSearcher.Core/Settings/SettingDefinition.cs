namespace MemeSearcher.Core.Settings;

/// <summary>
/// How a setting should be presented and validated. The UI renders from this rather than from a
/// hand-written control per setting, which is what makes "add a setting" a registration rather
/// than an edit to the Settings window (#24).
/// </summary>
public enum SettingKind
{
    Text,
    Choice,
    Toggle,
}

/// <summary>One selectable value of a <see cref="SettingKind.Choice"/> setting.</summary>
public record SettingChoice(string Value, string DisplayName);

/// <summary>
/// The declaration of a single setting: its identity, how to show it, and what it means when
/// nothing has been chosen.
///
/// Values are carried as strings deliberately. The store persists to JSON, settings arrive from
/// external tools as command-line arguments, and the set of types in play (a model name, a device
/// name, a language id) is almost entirely closed-choice strings already. Typed accessors live on
/// the category that owns the setting, so the one place that knows a setting is really a bool is
/// the code that also knows what it means.
/// </summary>
/// <param name="Key">Stable persisted identifier, "category.name" shaped. Renaming one orphans the stored value.</param>
/// <param name="Category">Name of the <see cref="ISettingsCategory"/> that declares it.</param>
/// <param name="DefaultValue">Used when nothing is stored. Must be a valid value per <see cref="Choices"/>.</param>
/// <param name="Choices">Allowed values for <see cref="SettingKind.Choice"/>; null otherwise.</param>
public record SettingDefinition(
    string Key,
    string Category,
    string DisplayName,
    string Description,
    SettingKind Kind,
    string DefaultValue,
    IReadOnlyList<SettingChoice>? Choices = null)
{
    /// <summary>
    /// Whether a value is acceptable for this setting in isolation. Cross-setting rules (a device
    /// and a compute type that are individually fine but invalid together) belong in
    /// <see cref="ISettingsCategory.Validate"/>, not here.
    /// </summary>
    public bool IsValidValue(string value) =>
        Kind != SettingKind.Choice || Choices is null || Choices.Any(c => c.Value == value);
}
