using Avalonia.Controls;
using Avalonia.Interactivity;
using MemeSearcher.ViewModels;

namespace MemeSearcher.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(MainWindowViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    /// <summary>
    /// Milestone 12: Settings is no longer a tab (the tabbed document area is search results now),
    /// so it opens as its own window instead - #19 will make it a registered Tool. Opening a window
    /// is a view concern, not something MainWindowViewModel's command should own.
    /// </summary>
    private void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        new SettingsWindow { DataContext = viewModel.Settings }.Show(this);
    }
}
