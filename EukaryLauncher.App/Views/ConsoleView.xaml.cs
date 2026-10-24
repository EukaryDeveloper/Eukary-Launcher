using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using EukaryLauncher.App.ViewModels;

namespace EukaryLauncher.App.Views;

public partial class ConsoleView : UserControl
{
    public ConsoleView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ConsoleViewModel old)
            old.Entries.CollectionChanged -= OnEntriesChanged;

        if (e.NewValue is ConsoleViewModel vm)
            vm.Entries.CollectionChanged += OnEntriesChanged;
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        Dispatcher.BeginInvoke(() => LogScroller.ScrollToBottom(), DispatcherPriority.Background);
}
