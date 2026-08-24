using System.Collections.Generic;

namespace MemeSearcher.ViewModels;

/// <summary>Thin wrapper so PhoneCoverageStripView has a stable x:DataType independent of whichever host view model owns the underlying cell list (#25).</summary>
public class PhoneCoverageStripViewModel(IReadOnlyList<PhoneCoverageCellViewModel> cells)
{
    public IReadOnlyList<PhoneCoverageCellViewModel> Cells { get; } = cells;
}
