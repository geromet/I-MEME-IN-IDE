using System;
using System.Collections.Generic;
using System.Linq;
using MemeSearcher.Core.Search;

namespace MemeSearcher.ViewModels;

/// <summary>One clip's contribution to a composite result, with the media's display title resolved (addendum §16/§22).</summary>
public class CompositeComponentRowViewModel(CompositeMatchComponent component, string mediaTitle)
{
    public string MediaTitle { get; } = mediaTitle;
    public string TimeRangeDisplay { get; } = $"{FormatTimestamp(component.StartSeconds)} - {FormatTimestamp(component.EndSeconds)}";
    public string SourceText { get; } = component.SourceText;
    public string Ipa { get; } = component.Ipa;
    public string ScoreDisplay { get; } = $"{component.Score:P0}";

    private static string FormatTimestamp(double seconds) => TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss\.ff");
}

/// <summary>Display-formatted projection of a CompositeSearchResult (handoff §45, addendum §16/§21-22).</summary>
public class CompositeSearchResultRowViewModel
{
    public string ScoreDisplay { get; }
    public string SourceCountDisplay { get; }
    public IReadOnlyList<CompositeComponentRowViewModel> Components { get; }

    public CompositeSearchResultRowViewModel(CompositeSearchResult result, IReadOnlyDictionary<Guid, string> mediaTitles)
    {
        ScoreDisplay = $"{result.OverallScore:P0}";
        SourceCountDisplay = result.Components.Count == 1
            ? "1 source"
            : $"{result.Components.Count} sources";
        Components = result.Components
            .Select(c => new CompositeComponentRowViewModel(c, mediaTitles.GetValueOrDefault(c.MediaId, c.MediaId.ToString()[..8])))
            .ToList();
    }
}
