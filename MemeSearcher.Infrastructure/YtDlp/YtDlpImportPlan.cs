namespace MemeSearcher.Infrastructure.YtDlp;

/// <summary>Why a video isn't a candidate for download, or that it is (#27) - what the review list the exit criteria ask for ("412 videos found, 0 imported") is actually built from.</summary>
public enum YtDlpImportPlanStatus
{
    New,
    AlreadyImported,
    PreviouslyFailed,
}

public record YtDlpImportPlanItem(YtDlpVideoEntry Entry, YtDlpImportPlanStatus Status);

/// <summary>
/// One channel/playlist enumeration, classified against what's already in the corpus (#27). Built
/// once per "check this URL" action, before anything downloads - the counts here are exactly the
/// "N found, M already imported, K new" breakdown the issue's own example wants to show the user
/// before committing them to hours of downloading.
/// </summary>
public record YtDlpImportPlan(IReadOnlyList<YtDlpImportPlanItem> Items)
{
    public int TotalCount => Items.Count;

    public int NewCount => Items.Count(i => i.Status == YtDlpImportPlanStatus.New);

    public int AlreadyImportedCount => Items.Count(i => i.Status == YtDlpImportPlanStatus.AlreadyImported);

    public int PreviouslyFailedCount => Items.Count(i => i.Status == YtDlpImportPlanStatus.PreviouslyFailed);
}
