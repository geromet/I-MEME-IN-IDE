namespace MemeSearcher.Core.Interfaces;

/// <summary>
/// Where a tool is and whether it runs. <see cref="Environment"/> carries per-tool environment
/// overrides from settings, which callers must apply when spawning the process - without them a
/// tool can be found and still fail, which is a confusing pair of symptoms to debug separately.
/// </summary>
public record ExternalToolStatus(
    bool IsInstalled,
    string? ExecutablePath,
    string? Version,
    string? Error,
    IReadOnlyDictionary<string, string>? Environment = null);

public interface IExternalToolLocator
{
    string ToolName { get; }

    Task<ExternalToolStatus> LocateAsync(CancellationToken cancellationToken = default);
}
