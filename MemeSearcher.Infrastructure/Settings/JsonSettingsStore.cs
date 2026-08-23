using System.Text.Json;
using MemeSearcher.Core.Settings;

namespace MemeSearcher.Infrastructure.Settings;

/// <summary>
/// Settings persisted as a flat JSON object next to the database.
///
/// A file rather than a table in the existing SQLite database, deliberately: settings include the
/// things that determine whether the app can start and work at all (tool behaviour, device
/// selection). When one of those is wrong the app may be exactly the thing that cannot be used to
/// fix it, and a text file can be corrected in any editor. The database has no such escape hatch.
///
/// Writes are whole-file and synchronous. The file is a handful of short strings and is written
/// only when a human changes a setting, so nothing here needs to be cleverer than that.
/// </summary>
public class JsonSettingsStore : ISettingsStore
{
    private readonly string _path;
    private readonly Lock _gate = new();
    private Dictionary<string, string> _values;

    public event EventHandler<SettingChangedEventArgs>? Changed;

    public JsonSettingsStore(string path)
    {
        _path = path;
        _values = Load(path);
    }

    public static JsonSettingsStore CreateDefault() => new(GetDefaultPath());

    public static string GetDefaultPath()
    {
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MemeSearcher");

        Directory.CreateDirectory(appDataDir);

        return Path.Combine(appDataDir, "settings.json");
    }

    public string Get(SettingDefinition definition)
    {
        lock (_gate)
        {
            // A stored value that is no longer legal (the choice list changed between versions, or
            // the file was hand-edited) falls back to the default rather than propagating. The
            // alternative is handing an unusable value to an external tool, which is the class of
            // failure this whole milestone is cleaning up.
            if (_values.TryGetValue(definition.Key, out var value) && definition.IsValidValue(value))
            {
                return value;
            }
        }

        return definition.DefaultValue;
    }

    public void Set(SettingDefinition definition, string value)
    {
        if (!definition.IsValidValue(value))
        {
            throw new ArgumentException(
                $"'{value}' is not a valid value for setting '{definition.Key}'.", nameof(value));
        }

        string oldValue;
        lock (_gate)
        {
            oldValue = _values.TryGetValue(definition.Key, out var stored) && definition.IsValidValue(stored)
                ? stored
                : definition.DefaultValue;

            if (oldValue == value)
            {
                return;
            }

            _values[definition.Key] = value;
            Save();
        }

        Changed?.Invoke(this, new SettingChangedEventArgs(definition, oldValue, value));
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(_values, new JsonSerializerOptions { WriteIndented = true });

        // Write-then-replace so an interrupted write cannot leave a truncated settings file that
        // the app then refuses to start with.
        var tempPath = _path + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _path, overwrite: true);
    }

    private static Dictionary<string, string> Load(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // A corrupt or unreadable settings file must not stop the app from starting - every
            // setting has a default, so an empty store is a working store.
            return [];
        }
    }
}
