using System.Collections.Generic;

namespace MemeSearcher.ViewModels;

/// <summary>
/// #25 exit criterion 4: "many hits are the same word from different timestamps - grouping by
/// covered span is more useful than a flat ranked list." One group is every result that covers the
/// exact same [QueryStart, QueryEnd) slice of the query, regardless of what word each one actually
/// transcribes to - that's the point: "maken" and "laten" both covering positions [0, 2) of "moeten"
/// belong together precisely because they're competing candidates for the same slice.
/// </summary>
public class ResultGroupViewModel(string label, IReadOnlyList<SearchResultRowViewModel> members)
{
    public string Label { get; } = label;

    public IReadOnlyList<SearchResultRowViewModel> Members { get; } = members;

    public int Count { get; } = members.Count;
}
