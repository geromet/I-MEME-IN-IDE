namespace MemeSearcher.Core.Settings;

/// <summary>
/// The registered categories, ordered for display. Injected by the Settings UI so that UI has no
/// compile-time knowledge of any particular setting.
/// </summary>
public class SettingsRegistry(IEnumerable<ISettingsCategory> categories)
{
    public IReadOnlyList<ISettingsCategory> Categories { get; } =
        categories.OrderBy(c => c.Order).ThenBy(c => c.Name).ToArray();

    public IEnumerable<SettingDefinition> AllSettings => Categories.SelectMany(c => c.Settings);

    /// <summary>
    /// Validates every category against the current store. Used before starting work that depends
    /// on settings, so a bad combination surfaces at the point of use even if the user never
    /// opened the Settings window.
    /// </summary>
    public IReadOnlyList<string> Validate(ISettingsStore store) =>
        Categories.Select(c => c.Validate(store)).OfType<string>().ToArray();
}
