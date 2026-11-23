using System.Windows;
using Island.Services;

namespace Island.Views;

/// <summary>Scrollable text popup for script output ("ShowInPopup", JSON "popup", "View details").</summary>
public partial class OutputPopupWindow : Window
{
    public OutputPopupWindow(string title, string text)
    {
        InitializeComponent();
        Title = title;
        OutputText.Text = text.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
    }

    private void Copy_Click(object sender, RoutedEventArgs e) => ClipboardHelper.SetText(OutputText.Text);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Wrap_Changed(object sender, RoutedEventArgs e) =>
        OutputText.TextWrapping = WrapBox.IsChecked == true ? TextWrapping.Wrap : TextWrapping.NoWrap;
}
