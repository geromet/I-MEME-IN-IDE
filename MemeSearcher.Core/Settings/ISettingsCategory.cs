namespace MemeSearcher.Core.Settings;

/// <summary>
/// A group of related settings, contributed by whichever component owns them. The WhisperX
/// category lives next to the WhisperX provider, not in the Settings UI - so the UI never needs
/// to know what a compute type is, and adding a category means adding a class and a DI
/// registration.
///
/// Resolved as <c>IEnumerable&lt;ISettingsCategory&gt;</c>, which sidesteps the
/// one-implementation-per-interface DI constraint that #16 exists to fix elsewhere in this app.
/// </summary>
public interface ISettingsCategory
{
    string Name { get; }

    string Description { get; }

    /// <summary>Display order in the Settings UI. Lower sorts first.</summary>
    int Order { get; }

    IReadOnlyList<SettingDefinition> Settings { get; }

    /// <summary>
    /// Cross-setting validation, run against a candidate state. Returns null when valid, or a
    /// message naming what is wrong.
    ///
    /// This exists because the expensive failures here are *combinations*: float16 on a CPU-only
    /// machine is two individually-legal values that together crash deep inside Python with an
    /// unhelpful message, minutes into a transcription. Catching that before the process is
    /// spawned is the point.
    /// </summary>
    string? Validate(ISettingsStore store);
}
