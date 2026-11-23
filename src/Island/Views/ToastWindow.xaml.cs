using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Island.Interop;
using Island.Services;

namespace Island.Views;

public partial class ToastWindow : Window
{
    private readonly DispatcherTimer _timer;

    public ToastWindow(string? title, string message, ToastKind kind, IReadOnlyList<ToastAction> actions, TimeSpan duration)
    {
        InitializeComponent();
        if (!string.IsNullOrWhiteSpace(title))
        {
            TitleText.Text = title;
            TitleText.Visibility = Visibility.Visible;
        }
        MessageText.Text = message;

        (KindIcon.Text, var brushKey) = kind switch
        {
            ToastKind.Error => ("", "ErrorBrush"),
            ToastKind.Success => ("", "SuccessBrush"),
            _ => ("", "AccentBrush"),
        };
        KindIcon.SetResourceReference(ForegroundProperty, brushKey);

        foreach (var a in actions)
        {
            var b = new Button { Content = a.Text, Style = (Style)FindResource("LinkButtonStyle"), Margin = new Thickness(0, 0, 14, 0) };
            b.Click += (_, _) =>
            {
                try { a.Callback(); }
                finally { CloseAnimated(); }
            };
            ActionsPanel.Children.Add(b);
        }
        if (actions.Count > 0) ActionsPanel.Visibility = Visibility.Visible;

        _timer = new DispatcherTimer { Interval = duration };
        _timer.Tick += (_, _) => CloseAnimated();

        SourceInitialized += (_, _) => WindowHelper.MakeFloating(this);
        MouseEnter += (_, _) => _timer.Stop();
        MouseLeave += (_, _) => _timer.Start();
        Opacity = 0;
        Loaded += (_, _) =>
        {
            BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(150)));
            _timer.Start();
        };
    }

    private bool _closing;

    public void CloseAnimated()
    {
        if (_closing) return;
        _closing = true;
        _timer.Stop();
        var anim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(150));
        anim.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, anim);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => CloseAnimated();
}
