using MemeSearcher.Core.Settings;
using MemeSearcher.Infrastructure.Processes;
using MemeSearcher.Infrastructure.Settings;
using MemeSearcher.Tests.TestDoubles;

namespace MemeSearcher.Tests.Settings;

public class WhisperXSettingsTests
{
    private sealed class FakeCudaProbe(bool available) : CudaAvailabilityProbe
    {
        public override bool IsCudaAvailable() => available;
    }

    private static (WhisperXSettings Category, ISettingsStore Store) Create(bool cudaAvailable) =>
        (new WhisperXSettings(new FakeCudaProbe(cudaAvailable)), new InMemorySettingsStore());

    [Fact]
    public void ResolveDevice_AutoPicksGpuWhenOneIsPresent()
    {
        var (category, store) = Create(cudaAvailable: true);

        Assert.Equal(WhisperXSettings.Cuda, category.ResolveDevice(store));
    }

    [Fact]
    public void ResolveDevice_AutoFallsBackToCpuWhenNoGpuIsPresent()
    {
        var (category, store) = Create(cudaAvailable: false);

        Assert.Equal(WhisperXSettings.Cpu, category.ResolveDevice(store));
    }

    [Fact]
    public void ResolveDevice_NeverReturnsAuto()
    {
        // whisperx has no "auto" device - passing it through would be an invalid argument, which
        // is the same class of bug as #23.
        foreach (var cuda in new[] { true, false })
        {
            var (category, store) = Create(cuda);
            store.Set(WhisperXSettings.Device, WhisperXSettings.AutoDevice);

            Assert.NotEqual(WhisperXSettings.AutoDevice, category.ResolveDevice(store));
        }
    }

    [Fact]
    public void Validate_RejectsGpuSelectedOnAMachineWithNoGpu()
    {
        var (category, store) = Create(cudaAvailable: false);
        store.Set(WhisperXSettings.Device, WhisperXSettings.Cuda);

        Assert.Contains("no NVIDIA GPU was detected", category.Validate(store));
    }

    [Fact]
    public void Validate_RejectsFloat16OnCpu()
    {
        var (category, store) = Create(cudaAvailable: false);
        store.Set(WhisperXSettings.Device, WhisperXSettings.Cpu);
        store.Set(WhisperXSettings.ComputeType, WhisperXSettings.Float16);

        Assert.Contains("float16 is not supported on CPU", category.Validate(store));
    }

    [Fact]
    public void Validate_AllowsFloat16OnGpu()
    {
        var (category, store) = Create(cudaAvailable: true);
        store.Set(WhisperXSettings.Device, WhisperXSettings.Cuda);
        store.Set(WhisperXSettings.ComputeType, WhisperXSettings.Float16);

        Assert.Null(category.Validate(store));
    }

    [Fact]
    public void Validate_AcceptsTheShippedDefaultsOnACpuOnlyMachine()
    {
        // The out-of-the-box state must be usable on the most constrained plausible machine.
        var (category, store) = Create(cudaAvailable: false);

        Assert.Null(category.Validate(store));
    }

    [Fact]
    public void EveryDefaultValueIsLegalForItsOwnDefinition()
    {
        var (category, _) = Create(cudaAvailable: false);

        Assert.All(category.Settings, s => Assert.True(
            s.IsValidValue(s.DefaultValue), $"'{s.Key}' has a default outside its own choice list."));
    }
}
