using System.Windows;
using Island.Services;

namespace Island.Views;

public partial class RunLogWindow : Window
{
    public RunLogWindow(AppServices services)
    {
        InitializeComponent();
        LogView.Attach(services);
    }
}
