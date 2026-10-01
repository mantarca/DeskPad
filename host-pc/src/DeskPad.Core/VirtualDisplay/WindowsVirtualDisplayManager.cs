using System.Diagnostics;
using System.Xml.Linq;
using DeskPad.Core.Models;

namespace DeskPad.Core.VirtualDisplay;

public class WindowsVirtualDisplayManager : IVirtualDisplayManager
{
    private readonly string _devconPath;
    private readonly string _infPath;
    private readonly string _settingsXmlPath;
    private const string HardwareId = "Root\\MttVDD";

    public WindowsVirtualDisplayManager(string? driversBasePath = null)
    {
        string baseDir = driversBasePath ?? FindDefaultDriverPath();
        _devconPath = Path.Combine(baseDir, "Dependencies", "devcon.exe");
        _infPath = Path.Combine(baseDir, "SignedDrivers", "x86", "VDD", "MttVDD.inf");
        _settingsXmlPath = Path.Combine(baseDir, "SignedDrivers", "x86", "VDD", "vdd_settings.xml");
    }

    private static string FindDefaultDriverPath()
    {
        // Try multiple locations: current app dir, project dir, or Laragon path
        string[] candidates = [
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "drivers", "vdd"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "drivers", "vdd"),
            @"c:\laragon\www\deskpad\drivers\vdd"
        ];

        foreach (var path in candidates)
        {
            var fullPath = Path.GetFullPath(path);
            if (File.Exists(Path.Combine(fullPath, "Dependencies", "devcon.exe")))
            {
                return fullPath;
            }
        }

        return @"c:\laragon\www\deskpad\drivers\vdd";
    }

    public async Task<bool> IsDriverInstalledAsync()
    {
        var (exitCode, output) = await RunDevconAsync("status " + HardwareId);
        return !output.Contains("No matching devices found", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<bool> IsDisplayEnabledAsync()
    {
        var (exitCode, output) = await RunDevconAsync("status " + HardwareId);
        return output.Contains("Driver is running", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<bool> InstallDriverAsync()
    {
        if (!File.Exists(_devconPath) || !File.Exists(_infPath))
        {
            throw new FileNotFoundException($"Driver installation files missing. Checked: {_devconPath}, {_infPath}");
        }

        var (exitCode, output) = await RunDevconAsync($"install \"{_infPath}\" {HardwareId}");
        return exitCode == 0 || output.Contains("Drivers installed successfully", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<bool> UninstallDriverAsync()
    {
        var (exitCode, output) = await RunDevconAsync($"remove {HardwareId}");
        return exitCode == 0 || output.Contains("device(s) were removed", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<bool> EnableDisplayAsync()
    {
        var (exitCode, output) = await RunDevconAsync($"enable {HardwareId}");
        return exitCode == 0 || output.Contains("Enabled", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<bool> DisableDisplayAsync()
    {
        var (exitCode, output) = await RunDevconAsync($"disable {HardwareId}");
        return exitCode == 0 || output.Contains("Disabled", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<bool> ConfigureResolutionAsync(DisplayResolution resolution)
    {
        if (!File.Exists(_settingsXmlPath))
            return false;

        try
        {
            var doc = XDocument.Load(_settingsXmlPath);
            var resolutionsElem = doc.Root?.Element("resolutions");
            if (resolutionsElem != null)
            {
                // Check if resolution already exists
                var existing = resolutionsElem.Elements("resolution").FirstOrDefault(r =>
                    (int?)r.Element("width") == resolution.Width &&
                    (int?)r.Element("height") == resolution.Height &&
                    (int?)r.Element("refresh_rate") == resolution.RefreshRate);

                if (existing == null)
                {
                    resolutionsElem.Add(new XElement("resolution",
                        new XElement("width", resolution.Width),
                        new XElement("height", resolution.Height),
                        new XElement("refresh_rate", resolution.RefreshRate)
                    ));
                    doc.Save(_settingsXmlPath);
                }
            }

            // Also copy settings to C:\VirtualDisplayDriver\vdd_settings.xml if present (default IddCx location)
            string sysDir = @"C:\VirtualDisplayDriver";
            if (Directory.Exists(sysDir))
            {
                File.Copy(_settingsXmlPath, Path.Combine(sysDir, "vdd_settings.xml"), true);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<(int ExitCode, string Output)> RunDevconAsync(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _devconPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null) return (-1, "Failed to start devcon.exe");

        string stdout = await process.StandardOutput.ReadToEndAsync();
        string stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, stdout + "\n" + stderr);
    }
}
