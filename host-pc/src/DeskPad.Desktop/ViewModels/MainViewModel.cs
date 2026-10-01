using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using DeskPad.Core.Models;
using DeskPad.Core.VirtualDisplay;
using DeskPad.Desktop.Services;
using DeskPad.Desktop.Views;

namespace DeskPad.Desktop.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    public string AppTitle { get; } = $"DeskPad v{GetAppVersion()}";

    private static string GetAppVersion()
    {
        try
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            var info = asm.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info)) return info.Split('+')[0];
            return asm.GetName().Version?.ToString(3) ?? "0.0.1";
        }
        catch
        {
            return "0.0.1";
        }
    }

    private string _deviceName = "Tablet Bekleniyor...";
    private string _resolution = "1920x1080 @ 60 FPS";
    private string _connectionStatusText = "● Bağlantı Bekleniyor";
    private string _connectionTypeText = "[Yok]";
    private string _encoderName = "Algılanıyor...";
    private string _latencyText = "0 ms";
    private string _toggleStreamButtonText = "▶ Başlat";
    private bool _isVirtualDisplayEnabled;

    public string DeviceName { get => _deviceName; set { _deviceName = value; OnPropertyChanged(); } }
    public string Resolution { get => _resolution; set { _resolution = value; OnPropertyChanged(); } }
    public string ConnectionStatusText { get => _connectionStatusText; set { _connectionStatusText = value; OnPropertyChanged(); } }
    public string ConnectionTypeText { get => _connectionTypeText; set { _connectionTypeText = value; OnPropertyChanged(); } }
    public string EncoderName { get => _encoderName; set { _encoderName = value; OnPropertyChanged(); } }
    public string LatencyText { get => _latencyText; set { _latencyText = value; OnPropertyChanged(); } }
    public string ToggleStreamButtonText { get => _toggleStreamButtonText; set { _toggleStreamButtonText = value; OnPropertyChanged(); } }
    public bool IsVirtualDisplayEnabled { get => _isVirtualDisplayEnabled; set { _isVirtualDisplayEnabled = value; OnPropertyChanged(); } }

    private string _lastLogMessage = "Sistem hazır.";
    public string LastLogMessage { get => _lastLogMessage; set { _lastLogMessage = value; OnPropertyChanged(); } }

    public ICommand ToggleStreamCommand { get; }
    public ICommand ToggleVirtualDisplayCommand { get; }
    public ICommand OpenSettingsCommand { get; }

    private readonly ConnectionOrchestrator _orchestrator;
    private readonly IVirtualDisplayManager _virtualDisplayManager;
    private readonly AppSettings _settings;

    public MainViewModel()
    {
        _virtualDisplayManager = new WindowsVirtualDisplayManager();
        _orchestrator = new ConnectionOrchestrator();
        _settings = SettingsStore.Load();
        _orchestrator.ApplySettings(_settings);

        ToggleStreamCommand = new RelayCommand(OnToggleStream);
        ToggleVirtualDisplayCommand = new RelayCommand(OnToggleVirtualDisplay);
        OpenSettingsCommand = new RelayCommand(OnOpenSettings);

        _orchestrator.OnStatusUpdated += UpdateStatus;
        _orchestrator.OnLog += msg => System.Windows.Application.Current.Dispatcher.Invoke(() => LastLogMessage = msg);
        
        CheckInitialVirtualDisplayStatus();
        _orchestrator.Start();
    }

    private void OnOpenSettings()
    {
        var window = new SettingsWindow(_settings)
        {
            Owner = Application.Current.MainWindow
        };

        if (window.ShowDialog() == true)
        {
            _orchestrator.ApplySettings(_settings);
            LastLogMessage = $"Ayarlar kaydedildi: {(_settings.AutoResolution ? "Otomatik" : $"{_settings.TargetWidth}x{_settings.TargetHeight}")} @ {_settings.TargetFps} FPS, {_settings.BitrateKbps} kbps";
        }
    }

    private async void CheckInitialVirtualDisplayStatus()
    {
        IsVirtualDisplayEnabled = await _virtualDisplayManager.IsDisplayEnabledAsync();
    }

    private void UpdateStatus()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            DeviceName = _orchestrator.ConnectedDeviceName ?? "Tablet Bekleniyor...";
            ConnectionStatusText = _orchestrator.IsStreaming ? "● Aktarım Yapılıyor" : "● Bağlantı Bekleniyor";
            ConnectionTypeText = _orchestrator.IsWifiConnected ? "[WiFi]" : (_orchestrator.IsUsbConnected ? "[USB]" : "[Yok]");
            EncoderName = _orchestrator.ActiveEncoder ?? "Hazır";
            
            ToggleStreamButtonText = _orchestrator.IsStreaming ? "■ Durdur" : "▶ Başlat";
        });
    }

    private void OnToggleStream()
    {
        if (_orchestrator.IsStreaming)
            _orchestrator.StopStreaming();
        else
            _orchestrator.StartStreaming();
    }

    private async void OnToggleVirtualDisplay()
    {
        if (IsVirtualDisplayEnabled)
        {
            await _virtualDisplayManager.EnableDisplayAsync();
        }
        else
        {
            await _virtualDisplayManager.DisableDisplayAsync();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public class RelayCommand : ICommand
{
    private readonly Action _execute;
    public RelayCommand(Action execute) => _execute = execute;
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => _execute();
    public event EventHandler? CanExecuteChanged;
}
