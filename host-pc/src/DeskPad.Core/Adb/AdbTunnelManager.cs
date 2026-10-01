using System.Diagnostics;
using System.Text.RegularExpressions;
using DeskPad.Core.Models;

namespace DeskPad.Core.Adb;

public class AdbTunnelManager
{
    private readonly string _adbPath;

    public AdbTunnelManager(string? customAdbPath = null)
    {
        _adbPath = customAdbPath ?? FindAdbPath();
    }

    private static string FindAdbPath()
    {
        string[] candidates = [
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "adb", "adb.exe"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "tools", "adb", "adb.exe"),
            @"c:\laragon\www\deskpad\tools\adb\adb.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk", "platform-tools", "adb.exe")
        ];

        foreach (var path in candidates)
        {
            var full = Path.GetFullPath(path);
            if (File.Exists(full))
            {
                return full;
            }
        }

        // Kurulumda platform-tools arsivi acildigi icin ozyinelemeli ara
        // (orn. tools\platform-tools\adb.exe)
        var found = Paths.FindFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools"), "adb.exe");
        if (found != null) return found;

        return "adb"; // Fallback to PATH
    }

    public async Task<List<TabletDeviceInfo>> GetConnectedDevicesAsync()
    {
        var devices = new List<TabletDeviceInfo>();
        var (exitCode, output) = await RunAdbAsync("devices -l");
        if (exitCode != 0) return devices;

        var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (line.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.StartsWith("*", StringComparison.OrdinalIgnoreCase)) continue;

            var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[1] == "device")
            {
                string serial = parts[0];
                string model = "Android Device";

                var modelMatch = Regex.Match(line, @"model:(\S+)");
                if (modelMatch.Success) model = modelMatch.Groups[1].Value.Replace('_', ' ');

                devices.Add(new TabletDeviceInfo
                {
                    SerialNumber = serial,
                    ModelName = model,
                    IsConnected = true
                });
            }
        }

        return devices;
    }

    public async Task<bool> SetupReverseTunnelAsync(int port = 2828, string? serial = null)
    {
        string targetArg = string.IsNullOrWhiteSpace(serial) ? "" : $"-s {serial} ";
        var (exitCode, output) = await RunAdbAsync($"{targetArg}reverse tcp:{port} tcp:{port}");
        return exitCode == 0;
    }

    public async Task<DisplayResolution?> GetDeviceDisplayResolutionAsync(string? serial = null)
    {
        string targetArg = string.IsNullOrWhiteSpace(serial) ? "" : $"-s {serial} ";
        var (exitCode, output) = await RunAdbAsync($"{targetArg}shell wm size");
        if (exitCode != 0) return null;

        // Example output: "Physical size: 2560x1600" or "Override size: 1920x1080"
        var match = Regex.Match(output, @"(\d{3,4})x(\d{3,4})");
        if (match.Success &&
            int.TryParse(match.Groups[1].Value, out int w) &&
            int.TryParse(match.Groups[2].Value, out int h))
        {
            // Landscape orientation: larger dimension should be width
            int width = Math.Max(w, h);
            int height = Math.Min(w, h);
            return new DisplayResolution(width, height, 60);
        }

        return null;
    }

    public async Task<bool> InstallApkAsync(string apkPath, string? serial = null)
    {
        if (!File.Exists(apkPath)) return false;
        string targetArg = string.IsNullOrWhiteSpace(serial) ? "" : $"-s {serial} ";
        var (exitCode, output) = await RunAdbAsync($"{targetArg}install -r \"{apkPath}\"");
        return exitCode == 0 && output.Contains("Success", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<(int ExitCode, string Output)> RunAdbAsync(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _adbPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(psi);
            if (process == null) return (-1, "Failed to start adb");

            string stdout = await process.StandardOutput.ReadToEndAsync();
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            return (process.ExitCode, stdout + "\n" + stderr);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }
}
