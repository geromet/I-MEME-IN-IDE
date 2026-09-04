using MemeSearcher.Core.Models;

namespace MemeSearcher.Core.Search;

/// <summary>
/// Temporary query-time narrowing layered on top of the corpus' persistent
/// <see cref="Media.IsSelectedForSearch"/> selection. These facets never mutate Media rows; callers
/// first resolve the persisted selection and then intersect it with this predicate.
/// </summary>
public sealed record MediaSearchFacets
{
    public static MediaSearchFacets Empty { get; } = new();

    public IReadOnlyCollection<string> Channels { get; init; } = [];
    public bool IncludeUnknownChannel { get; init; }

    public IReadOnlyCollection<string> Languages { get; init; } = [];

    public IReadOnlyCollection<YtDlpMediaKind> MediaKinds { get; init; } = [];
    public bool IncludeNonYtDlpMedia { get; init; }

    public DateOnly? UploadedOnOrAfter { get; init; }
    public DateOnly? UploadedOnOrBefore { get; init; }

    public bool IsEmpty =>
        Channels.Count == 0 &&
        !IncludeUnknownChannel &&
        Languages.Count == 0 &&
        MediaKinds.Count == 0 &&
        !IncludeNonYtDlpMedia &&
        UploadedOnOrAfter is null &&
        UploadedOnOrBefore is null;

    public bool Matches(Media media)
    {
        if (Channels.Count > 0 || IncludeUnknownChannel)
        {
            if (string.IsNullOrWhiteSpace(media.Channel))
            {
                if (!IncludeUnknownChannel)
                {
                    return false;
                }
            }
            else if (!Channels.Any(channel =>
                         string.Equals(channel, media.Channel, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
        }

        if (Languages.Count > 0 &&
            !Languages.Any(language =>
                string.Equals(language, media.Language, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (MediaKinds.Count > 0 || IncludeNonYtDlpMedia)
        {
            if (media.YtDlpMediaKind is null)
            {
                if (!IncludeNonYtDlpMedia)
                {
                    return false;
                }
            }
            else if (!MediaKinds.Contains(media.YtDlpMediaKind.Value))
            {
                return false;
            }
        }

        if (UploadedOnOrAfter is not null &&
            (media.UploadDate is null || media.UploadDate.Value < UploadedOnOrAfter.Value))
        {
            return false;
        }

        if (UploadedOnOrBefore is not null &&
            (media.UploadDate is null || media.UploadDate.Value > UploadedOnOrBefore.Value))
        {
            return false;
        }

        return true;
    }
}
