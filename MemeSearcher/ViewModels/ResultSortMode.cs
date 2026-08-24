namespace MemeSearcher.ViewModels;

/// <summary>
/// #25 exit criterion 2: "coverage is a first-class sort/filter axis alongside score." A normalized
/// score conflates "matched a little, well" with "matched a lot, roughly" - Coverage sorts by how
/// much of the query is genuinely covered first, falling back to Score to break ties between
/// equally-covered results.
/// </summary>
public enum ResultSortMode
{
    Score,
    Coverage,
}
