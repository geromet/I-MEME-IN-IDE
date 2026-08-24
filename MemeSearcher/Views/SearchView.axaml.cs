using Avalonia.Controls;
using MemeSearcher.ViewModels;

namespace MemeSearcher.Views;

public partial class SearchView : UserControl
{
    public SearchView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is SearchViewModel viewModel)
            {
                _ = viewModel.LoadRecentSearchesAsync();
                _ = viewModel.RefreshScopeSummaryAsync();
            }
        };
    }
}
