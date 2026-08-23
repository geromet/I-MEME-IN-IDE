namespace MemeSearcher.Core.Search;

public abstract record SearchScope
{
    public sealed record AllIndexedMedia : SearchScope;

    public sealed record SelectedMedia(IReadOnlyCollection<Guid> MediaIds) : SearchScope;

    public sealed record SingleMedia(Guid MediaId) : SearchScope;
}
