using System.Runtime.InteropServices;

namespace DeskPad.Core.Monitors;

public record MonitorInfo(
    string DeviceName,
    string DisplayLabel,
    int X,
    int Y,
    int Width,
    int Height,
    bool IsPrimary)
{
    public override string ToString() =>
        $"{DisplayLabel} {(IsPrimary ? "(Birincil)" : "")} - {Width}x{Height} @ ({X},{Y})";
}

public static class DisplayController
{
    #region Win32

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(string? deviceName, ref DEVMODE devMode, IntPtr hwnd, uint flags, IntPtr lParam);

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int ENUM_CURRENT_SETTINGS = -1;
    private const int ENUM_REGISTRY_SETTINGS = -2;
    private const uint CDS_UPDATEREGISTRY = 0x01;
    private const int DM_DISPLAYORIENTATION = 0x00000080;
    private const int DM_PELSWIDTH = 0x00080000;
    private const int DM_PELSHEIGHT = 0x00100000;
    private const int DM_DISPLAYFREQUENCY = 0x00400000;
    private const int DMDO_DEFAULT = 0;
    private const int DMDO_90 = 1;
    private const int DMDO_180 = 2;
    private const int DMDO_270 = 3;

    #endregion

    public static List<MonitorInfo> GetMonitors()
    {
        var list = new List<MonitorInfo>();
        int index = 0;

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data) =>
        {
            var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfo(hMonitor, ref mi))
            {
                index++;
                bool primary = (mi.dwFlags & 1) != 0; // MONITORINFOF_PRIMARY

                // Virtual ekran (VDD) icin daha anlamli isim
                string label = $"Ekran {index}";
                list.Add(new MonitorInfo(
                    mi.szDevice ?? $"\\\\.\\DISPLAY{index}",
                    label,
                    mi.rcMonitor.Left,
                    mi.rcMonitor.Top,
                    mi.rcMonitor.Right - mi.rcMonitor.Left,
                    mi.rcMonitor.Bottom - mi.rcMonitor.Top,
                    primary));
            }
            return true;
        }, IntPtr.Zero);

        return list;
    }

    /// <summary>Monitör bölgesini, gdigrab'in kullandigi sanal masaüstü koordinatlarina cevirir.</summary>
    public static (int x, int y, int w, int h) ToVirtualScreenRect(MonitorInfo monitor)
    {
        int vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
        return (monitor.X - vx, monitor.Y - vy, monitor.Width, monitor.Height);
    }

    /// <summary>Verilen monitörü 90 derece döndürür (yatay<->dikey).</summary>
    public static bool Rotate90(string deviceName)
    {
        try
        {
            var dm = new DEVMODE
            {
                dmDeviceName = "",
                dmFormName = "",
                dmSize = (short)Marshal.SizeOf<DEVMODE>()
            };

            if (!EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref dm))
                return false;

            int newOrientation = dm.dmDisplayOrientation == DMDO_DEFAULT ? DMDO_90 : DMDO_DEFAULT;

            if (newOrientation == DMDO_90 || newOrientation == DMDO_270)
            {
                // Dikey: genislik ve yuksekligi degistir
                if (dm.dmDisplayOrientation == DMDO_DEFAULT)
                {
                    (dm.dmPelsWidth, dm.dmPelsHeight) = (dm.dmPelsHeight, dm.dmPelsWidth);
                }
            }
            else
            {
                // Yataya don: tekrar degistir
                if (dm.dmDisplayOrientation != DMDO_DEFAULT)
                {
                    (dm.dmPelsWidth, dm.dmPelsHeight) = (dm.dmPelsHeight, dm.dmPelsWidth);
                }
            }

            dm.dmDisplayOrientation = newOrientation;
            dm.dmFields = DM_DISPLAYORIENTATION | DM_PELSWIDTH | DM_PELSHEIGHT;

            int result = ChangeDisplaySettingsEx(deviceName, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
            return result == 0; // DISP_CHANGE_SUCCESSFUL
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Monitörün çözünürlüğünü ayarlar. Desteklenmiyorsa false döner.</summary>
    public static bool SetResolution(string deviceName, int width, int height, int refreshRate = 60)
    {
        try
        {
            var dm = new DEVMODE { dmDeviceName = "", dmFormName = "", dmSize = (short)Marshal.SizeOf<DEVMODE>() };
            if (!EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref dm))
                return false;

            dm.dmPelsWidth = width;
            dm.dmPelsHeight = height;
            dm.dmDisplayFrequency = refreshRate;
            dm.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY;

            return ChangeDisplaySettingsEx(deviceName, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero) == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Belirli bir monitörü (bu oturumda) kapatır; genişlik/yuksekligi 0 yaparak ayırır.</summary>
    public static bool DisableMonitor(string deviceName)
    {
        try
        {
            var dm = new DEVMODE { dmDeviceName = "", dmFormName = "", dmSize = (short)Marshal.SizeOf<DEVMODE>() };
            if (!EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref dm))
                return false;

            dm.dmPelsWidth = 0;
            dm.dmPelsHeight = 0;
            dm.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT;

            return ChangeDisplaySettingsEx(deviceName, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero) == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Daha önce kapatılmış monitörü geri açar (kayıtlı/verilen mod ile).</summary>
    public static bool EnableMonitor(string deviceName, int width = 0, int height = 0, int refreshRate = 60)
    {
        try
        {
            var dm = new DEVMODE { dmDeviceName = "", dmFormName = "", dmSize = (short)Marshal.SizeOf<DEVMODE>() };

            if (!EnumDisplaySettings(deviceName, ENUM_REGISTRY_SETTINGS, ref dm) || dm.dmPelsWidth <= 0)
            {
                if (!EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref dm))
                    return false;
            }

            if (width > 0 && height > 0)
            {
                dm.dmPelsWidth = width;
                dm.dmPelsHeight = height;
                dm.dmDisplayFrequency = refreshRate;
            }
            else if (dm.dmPelsWidth <= 0 || dm.dmPelsHeight <= 0)
            {
                dm.dmPelsWidth = 1920;
                dm.dmPelsHeight = 1080;
                dm.dmDisplayFrequency = 60;
            }

            dm.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY;
            return ChangeDisplaySettingsEx(deviceName, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero) == 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsPortrait(string deviceName)
    {
        try
        {
            var dm = new DEVMODE { dmDeviceName = "", dmFormName = "", dmSize = (short)Marshal.SizeOf<DEVMODE>() };
            if (!EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref dm)) return false;
            return dm.dmDisplayOrientation == DMDO_90 || dm.dmDisplayOrientation == DMDO_270;
        }
        catch
        {
            return false;
        }
    }
}
