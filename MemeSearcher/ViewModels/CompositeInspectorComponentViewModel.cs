using System.Collections.Generic;
using System.Linq;

namespace MemeSearcher.ViewModels;

/// <summary>
/// Presentation-only projection for one composite result component in the shared Inspector (#35).
/// It reuses the component row's existing media/provenance fields and the same PhoneBlockViewModel
/// primitive as single-result inspection; waveform state is temporary presentation data only.
/// </summary>
public sealed class CompositeInspectorComponentViewModel
{
    public CompositeInspectorComponentViewModel(CompositeComponentRowViewModel component, int ordinal)
    {
        OrdinalDisplay = $"COMPONENT {ordinal}";
        MediaTitle = component.MediaTitle;
        MediaPath = component.MediaPath;
        StartSeconds = component.StartSeconds;
        EndSeconds = component.EndSeconds;
        SourceText = component.SourceText;
        ScoreDisplay = component.ScoreDisplay;
        TimeRangeDisplay = component.TimeRangeDisplay;
        QueryCoverageDisplay = component.QueryCoverageDisplay;
        Phones = component.MatchedPhoneDetails
            .Select(phone => new CompositeInspectorPhoneViewModel(new PhoneBlockViewModel(phone), component.MediaPath, component.MediaTitle))
            .ToList();

        AlignmentSummary = Phones.Count == 0
            ? "No phone timing available for this component."
            : Phones.All(p => p.Block.IsAligned)
                ? "Precisely aligned (real per-phone timing)."
                : Phones.Any(p => p.Block.IsAligned)
                    ? "Partially aligned - some phones are estimated."
                    : "Estimated timing - no phone-level alignment has run for this source.";
    }

    public string OrdinalDisplay { get; }
    public string MediaTitle { get; }
    public string? MediaPath { get; }
    public double? StartSeconds { get; }
    public double? EndSeconds { get; }
    public string SourceText { get; }
    public string ScoreDisplay { get; }
    public string TimeRangeDisplay { get; }
    public string QueryCoverageDisplay { get; }
    public string AlignmentSummary { get; }
    public IReadOnlyList<CompositeInspectorPhoneViewModel> Phones { get; }
    public WaveformStripViewModel Waveform { get; } = new();
}

public sealed class CompositeInspectorPhoneViewModel(PhoneBlockViewModel block, string? mediaPath, string mediaTitle)
{
    public PhoneBlockViewModel Block { get; } = block;
    public string? MediaPath { get; } = mediaPath;
    public string MediaTitle { get; } = mediaTitle;
}
