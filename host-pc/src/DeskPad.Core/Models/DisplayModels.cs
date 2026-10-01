namespace DeskPad.Core.Models;

public record DisplayResolution(int Width, int Height, int RefreshRate = 60)
{
    public override string ToString() => $"{Width}x{Height} @ {RefreshRate}Hz";
}

public class TabletDeviceInfo
{
    public string SerialNumber { get; set; } = string.Empty;
    public string ModelName { get; set; } = "Android Device";
    public DisplayResolution? NativeResolution { get; set; }
    public bool IsConnected { get; set; }
    public bool IsTunnelActive { get; set; }
}

public class StreamConfiguration
{
    public int TargetWidth { get; set; } = 1920;
    public int TargetHeight { get; set; } = 1080;
    public int TargetFps { get; set; } = 60;
    public int BitrateKbps { get; set; } = 25000; // 25 Mbps for crystal clear 1080p/2K
    public string VideoCodec { get; set; } = "h264"; // h264 or hevc
    public int DisplayIndex { get; set; } = 1; // 0 is primary, 1 is usually the virtual 2nd display
    public bool AutoResolution { get; set; } = true; // true: use tablet's supported size

    // Yakalanacak monitör bölgesi (sanal masaüstü koordinatları). Width<=0 => tüm masaüstü.
    public int CaptureX { get; set; } = -1;
    public int CaptureY { get; set; } = -1;
    public int CaptureWidth { get; set; } = -1;
    public int CaptureHeight { get; set; } = -1;
    public string? CaptureMonitorDevice { get; set; }
}

public enum StreamState
{
    Stopped,
    Starting,
    WaitingForClient,
    Streaming,
    Paused,
    Error
}

public enum ConnectionType
{
    Unknown,
    Usb,
    WiFi
}

public class AppSettings
{
    public string EncoderPreference { get; set; } = "Auto";
    public bool AutoStart { get; set; } = true;
    public string Theme { get; set; } = "Dark";
    public int BitrateKbps { get; set; } = 25000;
    public int DiscoveryPort { get; set; } = 2829;
    public int TargetWidth { get; set; } = 1920;
    public int TargetHeight { get; set; } = 1080;
    public int TargetFps { get; set; } = 60;
    public bool AutoResolution { get; set; } = true;
    public bool StartMinimized { get; set; } = false;
    public bool EnableVirtualDisplayOnStart { get; set; } = false;
    public bool HideVirtualDisplayWhenIdle { get; set; } = true; // Tablet bagli degilken sanal ekrani kapat
    public string? CaptureMonitorDevice { get; set; } = ""; // "" => otomatik (sanal/ikincil ekran)
}

public enum ConnectionState
{
    Disconnected,
    Searching,
    Connecting,
    Connected,
    Streaming
}
