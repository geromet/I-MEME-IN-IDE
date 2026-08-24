using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using MemeSearcher.ViewModels;

namespace MemeSearcher.Views;

/// <summary>
/// Scroll-into-view for the transcript viewer (#26) is code-behind, not a binding: Avalonia's
/// ListBox.ScrollIntoView is an imperative method, not a bindable property, so this listens for
/// TranscriptPanelViewModel.ActiveTab and the active tab's own ScrollTarget changing and drives the
/// ListBox directly whenever either one changes.
/// </summary>
public partial class TranscriptPanelView : UserControl
{
    private TranscriptTabViewModel? _subscribedTab;

    public TranscriptPanelView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => OnDataContextChanged();
    }

    private void OnDataContextChanged()
    {
        if (DataContext is not TranscriptPanelViewModel viewModel)
        {
            return;
        }

        viewModel.PropertyChanged += OnPanelPropertyChanged;
        ResubscribeTab(viewModel.ActiveTab);
    }

    private void OnPanelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TranscriptPanelViewModel.ActiveTab) && sender is TranscriptPanelViewModel viewModel)
        {
            ResubscribeTab(viewModel.ActiveTab);
        }
    }

    private void ResubscribeTab(TranscriptTabViewModel? tab)
    {
        if (_subscribedTab is not null)
        {
            _subscribedTab.PropertyChanged -= OnTabPropertyChanged;
        }

        _subscribedTab = tab;

        if (_subscribedTab is not null)
        {
            _subscribedTab.PropertyChanged += OnTabPropertyChanged;
        }

        ScrollToTarget(tab?.ScrollTarget);
    }

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TranscriptTabViewModel.ScrollTarget) && sender is TranscriptTabViewModel tab)
        {
            ScrollToTarget(tab.ScrollTarget);
        }
    }

    private void ScrollToTarget(TranscriptCueViewModel? cue)
    {
        if (cue is null)
        {
            return;
        }

        var cueList = this.FindControl<ListBox>("CueList");
        if (cueList is null)
        {
            return;
        }

        // Deferred: the ListBox has to finish rebinding to the (possibly just-switched) tab's Cues
        // and realize its containers before ScrollIntoView has anything to scroll to.
        Dispatcher.UIThread.Post(() => cueList.ScrollIntoView(cue), DispatcherPriority.Background);
    }
}
