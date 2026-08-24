namespace MemeSearcher.Infrastructure.YtDlp;

/// <summary>Rolled-up result of a batch run over a YtDlpImportPlan's New items (#27).</summary>
public record YtDlpImportSummary(int Imported, int Failed)
{
    public int Total => Imported + Failed;
}
