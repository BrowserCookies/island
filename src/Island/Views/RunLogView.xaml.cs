using System.Windows;
using System.Windows.Controls;
using Island.Core.Scripts;
using Island.Services;

namespace Island.Views;

public partial class RunLogView : UserControl
{
    private AppServices? _services;

    private bool _subscribed;

    public RunLogView()
    {
        InitializeComponent();
        // TabControl unloads/reloads tab content when switching tabs, so (re)subscribe on Loaded.
        Loaded += (_, _) => Subscribe();
        Unloaded += (_, _) =>
        {
            if (_services is not null && _subscribed) _services.RunLog.Changed -= OnLogChanged;
            _subscribed = false;
        };
    }

    public void Attach(AppServices services)
    {
        _services = services;
        if (IsLoaded) Subscribe();
    }

    private void Subscribe()
    {
        if (_services is null) return;
        if (!_subscribed) _services.RunLog.Changed += OnLogChanged;
        _subscribed = true;
        Refresh();
    }

    private void OnLogChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(Refresh);

    private void Refresh()
    {
        if (_services is null) return;
        var selected = (RunsList.SelectedItem as RunLogEntry)?.Time;
        RunsList.ItemsSource = _services.RunLog.Entries;
        if (selected is not null)
            RunsList.SelectedItem = _services.RunLog.Entries.FirstOrDefault(x => x.Time == selected);
        if (RunsList.SelectedItem is null && RunsList.Items.Count > 0) RunsList.SelectedIndex = 0;
        if (RunsList.Items.Count == 0) DetailsText.Text = "No script runs yet.";
    }

    private void RunsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        DetailsText.Text = (RunsList.SelectedItem as RunLogEntry)?.FullText.Replace("\n", Environment.NewLine) ?? "";
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(Window.GetWindow(this)!, "Clear the script run log?", "Island", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK)
            _services?.RunLog.Clear();
    }

    private void Popup_Click(object sender, RoutedEventArgs e)
    {
        if (RunsList.SelectedItem is RunLogEntry entry)
            _services?.Host.ShowOutputPopup($"{entry.ButtonLabel} — {entry.Time:HH:mm:ss}", entry.FullText);
    }
}
