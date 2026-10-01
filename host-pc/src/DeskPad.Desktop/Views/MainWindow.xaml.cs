using System.ComponentModel;
using System.Windows;
using DeskPad.Desktop.ViewModels;

namespace DeskPad.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        StateChanged += OnStateChanged;
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        // Simge durumuna kucultulunce pencereyi tamamen gizle ve sistem tepsisine in.
        if (WindowState == WindowState.Minimized)
        {
            Hide();
            WindowState = WindowState.Normal;
        }
    }

    private void OnClosing(object sender, CancelEventArgs e)
    {
        // Cancel the close and minimize to tray instead
        e.Cancel = true;
        this.Hide();
    }
}
