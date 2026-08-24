using Avalonia.Controls;
using MemeSearcher.ViewModels;

namespace MemeSearcher.Views;

public partial class CatalogsView : UserControl
{
    public CatalogsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is CatalogsViewModel viewModel)
            {
                _ = viewModel.LoadAsync();
            }
        };
    }
}
