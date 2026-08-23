using MemeSearcher.Core.Languages;
using MemeSearcher.Core.Settings;
using MemeSearcher.Infrastructure.Processes;

namespace MemeSearcher.Infrastructure.Settings;

/// <summary>
/// The WhisperX settings category (#24). Every setting here replaces something that was
/// previously hardcoded or - worse - never passed at all, leaving whisperx to apply its own
/// default silently.
///
/// Lives next to the WhisperX provider rather than in the Settings UI project: the UI renders
/// whatever categories are registered and has no compile-time knowledge of what a compute type
/// is.
/// </summary>
public class WhisperXSettings(CudaAvailabilityProbe cudaProbe) : ISettingsCategory
{
    public const string Cpu = "cpu";
    public const string Cuda = "cuda";
    public const string AutoDevice = "auto";
    public const string DefaultComputeType = "default";
    public const string Float16 = "float16";

    public static readonly SettingDefinition Language = new(
        Key: "whisperx.language",
        Category: CategoryName,
        DisplayName: "Language",
        Description: "Language spoken in imported media. Also used to phonemize search queries, "
                     + "so a corpus and the searches against it must agree.",
        Kind: SettingKind.Choice,
        DefaultValue: LanguageCatalog.Default.Id,
        Choices: LanguageCatalog.All.Select(o => new SettingChoice(o.Id, o.DisplayName)).ToArray());

    public static readonly SettingDefinition Model = new(
        Key: "whisperx.model",
        Category: CategoryName,
        DisplayName: "Model",
        Description: "Larger models transcribe more accurately and much more slowly. This choice "
                     + "dominates both quality and runtime more than anything else here.",
        Kind: SettingKind.Choice,
        // whisperx's own default. Not the best model - the best default, since a first import
        // that takes twenty minutes reads as a hang.
        DefaultValue: "small",
        Choices:
        [
            new("tiny", "Tiny (fastest, least accurate)"),
            new("base", "Base"),
            new("small", "Small (default)"),
            new("medium", "Medium"),
            new("large-v2", "Large v2"),
            new("large-v3", "Large v3 (slowest, most accurate)"),
        ]);

    public static readonly SettingDefinition Device = new(
        Key: "whisperx.device",
        Category: CategoryName,
        DisplayName: "Device",
        Description: "Hardware to run transcription on. Auto picks the GPU when one is detected.",
        Kind: SettingKind.Choice,
        // Auto, not cpu: whisperx's own --device default is `cuda`, and the app previously never
        // passed --device at all - so a CPU-only machine silently inherited a GPU default and
        // failed inside Python. Resolving the device here makes that explicit either way.
        DefaultValue: AutoDevice,
        Choices:
        [
            new(AutoDevice, "Auto-detect"),
            new(Cpu, "CPU"),
            new(Cuda, "GPU (CUDA)"),
        ]);

    public static readonly SettingDefinition ComputeType = new(
        Key: "whisperx.compute_type",
        Category: CategoryName,
        DisplayName: "Compute type",
        Description: "Numeric precision. 'Default' follows the device: float16 on GPU, float32 on CPU.",
        Kind: SettingKind.Choice,
        // Replaces a hardcoded float32 whose own comment asked for this setting to exist. float32
        // was chosen because float16 fails outright on CPU - but it also throws away most of the
        // speed of a GPU that supports float16. 'default' gets both cases right.
        DefaultValue: DefaultComputeType,
        Choices:
        [
            new(DefaultComputeType, "Default (follow device)"),
            new("float32", "float32 (CPU-safe)"),
            new(Float16, "float16 (GPU only, faster)"),
            new("int8", "int8 (smallest, lowest quality)"),
        ]);

    public const string CategoryName = "WhisperX";

    public string Name => CategoryName;

    public string Description => "Speech-to-text transcription of imported media.";

    public int Order => 10;

    public IReadOnlyList<SettingDefinition> Settings => [Language, Model, Device, ComputeType];

    /// <summary>
    /// Resolves <see cref="AutoDevice"/> to a concrete device. Callers about to spawn whisperx
    /// must use this rather than passing "auto" through - whisperx has no such value.
    /// </summary>
    public string ResolveDevice(ISettingsStore store)
    {
        var configured = store.Get(Device);
        return configured == AutoDevice
            ? (cudaProbe.IsCudaAvailable() ? Cuda : Cpu)
            : configured;
    }

    public string? Validate(ISettingsStore store)
    {
        var configured = store.Get(Device);
        var resolved = ResolveDevice(store);
        var computeType = store.Get(ComputeType);

        if (configured == Cuda && !cudaProbe.IsCudaAvailable())
        {
            return "Device is set to GPU (CUDA) but no NVIDIA GPU was detected. Transcription will "
                   + "fail inside whisperx. Choose CPU or Auto-detect.";
        }

        if (computeType == Float16 && resolved == Cpu)
        {
            return "Compute type float16 is not supported on CPU. Choose 'Default (follow device)' "
                   + "or float32.";
        }

        return null;
    }
}
