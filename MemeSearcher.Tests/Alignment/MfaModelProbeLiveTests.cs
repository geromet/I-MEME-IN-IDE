using MemeSearcher.Infrastructure.Alignment;

namespace MemeSearcher.Tests.Alignment;

/// <summary>
/// Checks the probe against whatever MFA is actually installed on this machine. Asserts nothing
/// about which models exist - that is machine state - only that resolution lands somewhere real
/// and does not throw.
/// </summary>
public class MfaModelProbeLiveTests
{
    [Fact]
    public void Discover_ResolvesARootAndDoesNotThrowOnThisMachine()
    {
        var inventory = new MfaModelProbe().Discover();

        Assert.False(string.IsNullOrWhiteSpace(inventory.ModelsRoot));
        Assert.NotNull(inventory.AcousticModels);
        Assert.NotNull(inventory.Dictionaries);
    }
}
