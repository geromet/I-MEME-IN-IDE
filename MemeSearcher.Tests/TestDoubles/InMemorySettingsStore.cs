using MemeSearcher.Core.Settings;

namespace MemeSearcher.Tests.TestDoubles;

/// <summary>
/// Settings store with no file behind it. Tests that care about persistence use JsonSettingsStore
/// against a temp path; everything else just needs somewhere for values to live.
/// </summary>
public class InMemorySettingsStore : ISettingsStore
{
    private readonly Dictionary<string, string> _values = [];

    public event EventHandler<SettingChangedEventArgs>? Changed;

    public string Get(SettingDefinition definition) =>
        _values.TryGetValue(definition.Key, out var value) && definition.IsValidValue(value)
            ? value
            : definition.EffectiveDefault;

    public void Set(SettingDefinition definition, string value)
    {
        var oldValue = Get(definition);
        _values[definition.Key] = value;

        if (oldValue != value)
        {
            Changed?.Invoke(this, new SettingChangedEventArgs(definition, oldValue, value));
        }
    }
}
