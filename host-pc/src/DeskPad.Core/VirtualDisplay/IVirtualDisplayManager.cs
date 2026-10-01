using DeskPad.Core.Models;

namespace DeskPad.Core.VirtualDisplay;

public interface IVirtualDisplayManager
{
    Task<bool> IsDriverInstalledAsync();
    Task<bool> IsDisplayEnabledAsync();
    Task<bool> InstallDriverAsync();
    Task<bool> UninstallDriverAsync();
    Task<bool> EnableDisplayAsync();
    Task<bool> DisableDisplayAsync();
    Task<bool> ConfigureResolutionAsync(DisplayResolution resolution);
}
