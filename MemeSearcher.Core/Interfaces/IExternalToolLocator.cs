namespace MemeSearcher.Core.Interfaces;

public record ExternalToolStatus(bool IsInstalled, string? ExecutablePath, string? Version, string? Error);

public interface IExternalToolLocator
{
    string ToolName { get; }

    Task<ExternalToolStatus> LocateAsync(CancellationToken cancellationToken = default);
}
