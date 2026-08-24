using Avalonia.Controls;
using MemeSearcher.ViewModels;

namespace MemeSearcher.Views;

public partial class TemplatesView : UserControl
{
    public TemplatesView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is TemplatesViewModel viewModel)
            {
                _ = viewModel.LoadAsync();
            }
        };
    }
}
