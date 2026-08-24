using Avalonia;
using Avalonia.Controls;

namespace MemeSearcher.Views;

/// <summary>
/// #25: cell sizing is a host concern, not a view-model one - the results list wants a compact
/// strip, the Inspector wants a larger one - so CellWidth/CellHeight/FontSize are styled properties
/// the host sets in markup, while the cell content and coloring come entirely from the bound
/// PhoneCoverageStripViewModel shared by both.
/// </summary>
public partial class PhoneCoverageStripView : UserControl
{
    public static readonly StyledProperty<double> CellWidthProperty =
        AvaloniaProperty.Register<PhoneCoverageStripView, double>(nameof(CellWidth), 20);

    public static readonly StyledProperty<double> CellHeightProperty =
        AvaloniaProperty.Register<PhoneCoverageStripView, double>(nameof(CellHeight), 20);

    public static readonly StyledProperty<double> CellFontSizeProperty =
        AvaloniaProperty.Register<PhoneCoverageStripView, double>(nameof(CellFontSize), 11);

    public double CellWidth
    {
        get => GetValue(CellWidthProperty);
        set => SetValue(CellWidthProperty, value);
    }

    public double CellHeight
    {
        get => GetValue(CellHeightProperty);
        set => SetValue(CellHeightProperty, value);
    }

    public double CellFontSize
    {
        get => GetValue(CellFontSizeProperty);
        set => SetValue(CellFontSizeProperty, value);
    }

    public PhoneCoverageStripView()
    {
        InitializeComponent();
    }
}
