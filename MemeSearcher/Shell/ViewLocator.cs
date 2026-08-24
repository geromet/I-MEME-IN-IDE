using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using MemeSearcher.ViewModels;

namespace MemeSearcher.Shell;

/// <summary>
/// Resolves a panel's View by naming convention (MemeSearcher.ViewModels.FooViewModel ->
/// MemeSearcher.Views.FooView), registered app-wide in App.axaml. This is what actually makes #19's
/// "register one class, no shell XAML edit" exit criterion true: without it, every new panel would
/// need its own &lt;DataTemplate&gt; entry hand-added to MainWindow.axaml, which is exactly the
/// "framework plus one special case per panel" the issue says to avoid.
/// </summary>
public class ViewLocator : IDataTemplate
{
    public Control Build(object? data)
    {
        var viewModelName = data!.GetType().FullName!;
        var viewName = viewModelName
            .Replace(".ViewModels.", ".Views.")
            .Replace("ViewModel", "View");
        var viewType = Type.GetType(viewName);

        if (viewType is null)
        {
            return new TextBlock { Text = $"No view registered for {viewModelName} (expected {viewName})" };
        }

        return (Control)Activator.CreateInstance(viewType)!;
    }

    public bool Match(object? data) => data is ViewModelBase;
}
