namespace MemeSearcher.Core.Settings;

public record SettingChangedEventArgs(SettingDefinition Definition, string OldValue, string NewValue);

/// <summary>
/// Persistent key/value storage for settings, plus change notification.
///
/// Scope is app-global. Per-media overrides are deliberately not modelled here: what a given file
/// was ingested with is *provenance*, recorded on the media row itself, and it must not change
/// when the user later changes a setting. Conflating the two would make already-ingested data
/// mutate retroactively.
/// </summary>
public interface ISettingsStore
{
    /// <summary>Returns the stored value, or the definition's default if nothing valid is stored.</summary>
    string Get(SettingDefinition definition);

    void Set(SettingDefinition definition, string value);

    event EventHandler<SettingChangedEventArgs>? Changed;
}

public static class SettingsStoreExtensions
{
    public static bool GetBool(this ISettingsStore store, SettingDefinition definition) =>
        bool.TryParse(store.Get(definition), out var value) && value;

    public static void SetBool(this ISettingsStore store, SettingDefinition definition, bool value) =>
        store.Set(definition, value ? "true" : "false");
}
