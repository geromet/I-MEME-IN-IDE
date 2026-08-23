using Avalonia.Controls;
using MemeSearcher.ViewModels;

namespace MemeSearcher.Views;

public partial class LibraryView : UserControl
{
    public LibraryView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is LibraryViewModel viewModel)
            {
                _ = viewModel.LoadAsync();
            }
        };
    }
}
