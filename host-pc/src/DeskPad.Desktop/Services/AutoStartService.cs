using System.IO;
using Microsoft.Win32;

namespace DeskPad.Desktop.Services;

public class AutoStartService
{
    private const string AppName = "DeskPad";

    public static void SetAutoStart(bool enable)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            if (key != null)
            {
                if (enable)
                {
                    string path = Environment.ProcessPath ?? string.Empty;
                    if (!string.IsNullOrEmpty(path))
                    {
                        key.SetValue(AppName, $"\"{path}\" --minimized");
                    }
                }
                else
                {
                    key.DeleteValue(AppName, false);
                }
            }
        }
        catch { }
    }

    public static bool IsAutoStartEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
            if (key != null)
            {
                object? value = key.GetValue(AppName);
                return value != null;
            }
        }
        catch { }
        
        return false;
    }
}
