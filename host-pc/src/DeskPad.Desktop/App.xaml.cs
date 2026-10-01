using System.Windows;
using H.NotifyIcon;
using DeskPad.Desktop.Services;
using DeskPad.Desktop.Views;

namespace DeskPad.Desktop;

public partial class App : Application
{
    private TaskbarIcon? _trayIcon;
    private Views.MainWindow? _mainWindow;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        
        _trayIcon = (TaskbarIcon)FindResource("TrayIcon");
        _trayIcon.ForceCreate(); // Kaynak olarak tanimli oldugu icin tepsi ikonunu acikca olustur
        _trayIcon.Visibility = Visibility.Visible;

        _mainWindow = new Views.MainWindow();
        
        bool isMinimized = e.Args.Contains("--minimized") || SettingsStore.Load().StartMinimized;
        if (!isMinimized)
        {
            _mainWindow.Show();
        }
    }

    private void OnTrayDoubleClick(object sender, RoutedEventArgs e)
    {
        ShowMainWindow();
    }

    private void OnShowControlPanel(object sender, RoutedEventArgs e)
    {
        ShowMainWindow();
    }

    private void ShowMainWindow()
    {
        if (_mainWindow == null)
        {
            _mainWindow = new Views.MainWindow();
        }

        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;

        _mainWindow.Activate();
    }

    private void OnExitApplication(object sender, RoutedEventArgs e)
    {
        _trayIcon?.Dispose();
        Shutdown();
    }

    private void OnExit(object sender, ExitEventArgs e)
    {
        _trayIcon?.Dispose();
    }
}
