using System.Diagnostics;
using System.Windows;
using DeskPad.Core.Adb;
using DeskPad.Core.Capture;
using DeskPad.Core.Models;
using DeskPad.Core.Monitors;
using DeskPad.Core.Network;
using DeskPad.Core.VirtualDisplay;

namespace DeskPad.Desktop.Services;

public class ConnectionOrchestrator
{
    private readonly AdbTunnelManager _adbManager;
    private readonly TcpStreamServer _tcpServer;
    private readonly UdpDiscoveryServer _udpServer;
    private readonly ScreenStreamEngine _streamEngine;
    private readonly WindowsVirtualDisplayManager _vdd;
    
    private StreamConfiguration _config;

    public StreamConfiguration Config => _config;

    /// <summary>Tablet bagli degilken sanal ekrani kapat (varsayilan: acik).</summary>
    public bool HideVirtualDisplayWhenIdle { get; set; } = true;

    public bool IsUsbConnected { get; private set; }
    public bool IsWifiConnected { get; private set; }
    public bool IsStreaming => _streamEngine.IsStreaming;
    public string? ConnectedDeviceName { get; private set; }
    public string? ActiveEncoder { get; private set; }

    public event Action? OnStatusUpdated;
    public event Action<string>? OnLog;

    public ConnectionOrchestrator()
    {
        _adbManager = new AdbTunnelManager();
        _tcpServer = new TcpStreamServer(2828);
        _udpServer = new UdpDiscoveryServer(() => IsStreaming, 2829);
        _streamEngine = new ScreenStreamEngine(_tcpServer);
        _vdd = new WindowsVirtualDisplayManager();
        
        _config = new StreamConfiguration();

        _tcpServer.OnClientHandshakeReceived += HandleHandshake;
        _tcpServer.OnClientDisconnected += OnClientDisconnected;
        _streamEngine.OnStreamStarted += () => OnStatusUpdated?.Invoke();
        _streamEngine.OnStreamStopped += () => OnStatusUpdated?.Invoke();
        _streamEngine.OnLog += LogEngine;

        // Uygulama kapanirken sanal ekrani kapat
        try
        {
            var app = Application.Current;
            if (app != null)
            {
                app.Exit += (_, _) =>
                {
                    if (HideVirtualDisplayWhenIdle)
                    {
                        try { _vdd.DisableDisplayAsync().Wait(4000); } catch { }
                    }
                };
            }
        }
        catch { }
    }

    private void OnClientDisconnected()
    {
        StopStreaming();
        OnStatusUpdated?.Invoke();
        _ = HideVirtualDisplayAsync();
    }

    private async Task HideVirtualDisplayAsync()
    {
        if (!HideVirtualDisplayWhenIdle) return;

        // Baska bagli istemci yoksa sanal ekrani kapat
        if (_tcpServer.IsClientConnected) return;

        try { await _vdd.DisableDisplayAsync(); } catch { }
    }

    private async Task EnsureVirtualDisplayAsync()
    {
        if (!HideVirtualDisplayWhenIdle) return;

        try
        {
            await _vdd.EnableDisplayAsync();
            await WaitForSecondaryMonitorAsync(6000);
        }
        catch { }
    }

    private static async Task WaitForSecondaryMonitorAsync(int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (DisplayController.GetMonitors().Any(m => !m.IsPrimary)) return;
            await Task.Delay(300);
        }
    }

    private void LogEngine(string msg)
    {
        OnLog?.Invoke(msg);
        if (msg.Contains("GPU Encoder bulundu:"))
        {
            ActiveEncoder = msg.Split(':')[1].Trim();
            OnStatusUpdated?.Invoke();
        }
    }

    private async void HandleHandshake(ClientHandshakeInfo handshake)
    {
        ConnectedDeviceName = handshake.DeviceModel;
        
        // Auto adjust (only when AutoResolution is enabled; otherwise use manual settings)
        if (_config.AutoResolution && handshake.Width > 0 && handshake.Height > 0)
        {
            _config.TargetWidth = handshake.Width;
            _config.TargetHeight = handshake.Height;
            _config.TargetFps = handshake.RefreshRate > 0 ? handshake.RefreshRate : 60;
        }

        await _tcpServer.SendConfigAckAsync(new ServerConfigInfo
        {
            Width = _config.TargetWidth,
            Height = _config.TargetHeight,
            Fps = _config.TargetFps,
            Codec = _config.VideoCodec
        });

        // Determine if WiFi or USB. For simplicity, assume if ADB devices exist, it might be USB.
        var devices = await _adbManager.GetConnectedDevicesAsync();
        if (devices.Count > 0)
        {
            IsUsbConnected = true;
            IsWifiConnected = false;
        }
        else
        {
            IsWifiConnected = true;
            IsUsbConnected = false;
        }

        OnStatusUpdated?.Invoke();
        // Tablet baglandi: sanal ekrani ac ve gorunmesini bekle, sonra yakala
        await EnsureVirtualDisplayAsync();
        ResolveCaptureMonitor();
        await _streamEngine.StartStreamingAsync(_config);
    }

    /// <summary>
    /// Yakalanacak monitörü belirler. Ayar bos ise ve birden fazla ekran varsa
    /// sanal/ikincil ekrani (birincil olmayan) otomatik secer.
    /// </summary>
    private void ResolveCaptureMonitor()
    {
        var monitors = DisplayController.GetMonitors();
        if (monitors.Count == 0)
        {
            _config.CaptureWidth = -1;
            return;
        }

        MonitorInfo? chosen = null;
        if (!string.IsNullOrEmpty(_config.CaptureMonitorDevice))
            chosen = monitors.FirstOrDefault(m => m.DeviceName == _config.CaptureMonitorDevice);

        if (chosen == null && monitors.Count > 1)
            chosen = monitors.FirstOrDefault(m => !m.IsPrimary);

        chosen ??= monitors[0];

        var (x, y, w, h) = DisplayController.ToVirtualScreenRect(chosen);
        _config.CaptureMonitorDevice = chosen.DeviceName;
        _config.CaptureX = x;
        _config.CaptureY = y;
        _config.CaptureWidth = w;
        _config.CaptureHeight = h;
    }

    public void Start()
    {
        _tcpServer.Start();
        _udpServer.Start();

        // Baslangicta tablet yok: sanal ekrani gizle
        _ = HideVirtualDisplayAsync();
        
        // USB Monitor loop
        _ = Task.Run(async () =>
        {
            string? lastSerial = null;
            while (true)
            {
                try
                {
                    var devices = await _adbManager.GetConnectedDevicesAsync();
                    if (devices.Count > 0)
                    {
                        var dev = devices[0];
                        if (dev.SerialNumber != lastSerial)
                        {
                            lastSerial = dev.SerialNumber;
                            await _adbManager.SetupReverseTunnelAsync(2828, dev.SerialNumber);
                        }
                    }
                    else
                    {
                        lastSerial = null;
                        IsUsbConnected = false;
                    }
                }
                catch { }
                await Task.Delay(3000);
            }
        });
    }

    public void StartStreaming()
    {
        if (!_streamEngine.IsStreaming && _tcpServer.IsClientConnected)
        {
            ResolveCaptureMonitor();
            _ = _streamEngine.StartStreamingAsync(_config);
        }
    }

    public void StopStreaming()
    {
        if (_streamEngine.IsStreaming)
        {
            _streamEngine.StopStreaming();
        }
    }

    public void ApplySettings(AppSettings settings)
    {
        _config.BitrateKbps = settings.BitrateKbps;
        _config.AutoResolution = settings.AutoResolution;
        _config.TargetFps = settings.TargetFps;
        _config.CaptureMonitorDevice = settings.CaptureMonitorDevice;
        HideVirtualDisplayWhenIdle = settings.HideVirtualDisplayWhenIdle;
        if (!settings.AutoResolution)
        {
            _config.TargetWidth = settings.TargetWidth;
            _config.TargetHeight = settings.TargetHeight;
        }
        OnStatusUpdated?.Invoke();
    }
}
