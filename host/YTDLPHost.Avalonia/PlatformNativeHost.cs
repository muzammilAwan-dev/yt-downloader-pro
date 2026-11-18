using System.Runtime.Versioning;
using Microsoft.Win32;
using YTDLPHost.Native;
using YTDLPHost.Services;

namespace YTDLPHost.UI;

/// <summary>
/// Step 4.4: links the browsers to this install, on every OS. Windows writes the HKCU registry values, macOS and Linux write manifest files
/// (both rule sets live in Core and are tested there); this class only picks the one that fits the OS and supplies the app folder.
/// </summary>
public static class PlatformNativeHost
{
    private static string AppDir => Path.GetDirectoryName(Environment.ProcessPath) ?? "";

    public static IReadOnlyList<string> EnsureRegistered()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return NativeHostRegistration.EnsureRegistered(AppDir, new RegistryNativeHostRegistry(), File.Exists);
            return NativeHostUnixRegistration.EnsureRegistered(AppPathsFactory.CurrentOs(), AppDir, AppPaths.Current.HomeDir, new DiskNativeHostFiles());
        }
        catch (Exception ex)
        {
            return new[] { "Registration check failed: " + ex.Message };
        }
    }

    public static IReadOnlyList<string> Unregister()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return NativeHostRegistration.Unregister(AppDir, new RegistryNativeHostRegistry());
            return NativeHostUnixRegistration.Unregister(AppPathsFactory.CurrentOs(), AppDir, AppPaths.Current.HomeDir, new DiskNativeHostFiles());
        }
        catch (Exception ex)
        {
            return new[] { "Skipped: unregistering failed: " + ex.Message };
        }
    }

    public static NativeHostReport Check()
    {
        if (OperatingSystem.IsWindows())
            return NativeHostStatus.CheckWindows(AppDir, new RegistryNativeHostRegistry(), File.Exists);
        return NativeHostStatus.CheckUnix(AppPathsFactory.CurrentOs(), AppDir, AppPaths.Current.HomeDir, new DiskNativeHostFiles());
    }

    /// <summary><c>--register-native-host</c> / <c>--unregister-native-host</c>: do the job, log it, return the exit code (0 = done).</summary>
    public static int RunCommand(HostCommand command)
    {
        var log = command == HostCommand.RegisterNativeHost ? EnsureRegistered() : Unregister();
        foreach (var line in log) AppLogger.Log("[NATIVE HOST] " + line);
        return NativeHostRegistration.Failed(log) ? 1 : 0;
    }
}

/// <summary>The Native Messaging registration values under HKCU: no administrator rights needed. Only ever created on Windows.</summary>
[SupportedOSPlatform("windows")]
internal sealed class RegistryNativeHostRegistry : INativeHostRegistry
{
    public string? GetDefaultValue(string subKey)
    {
        using var key = Registry.CurrentUser.OpenSubKey(subKey);
        return key?.GetValue("") as string;
    }

    public void SetDefaultValue(string subKey, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(subKey) ?? throw new IOException("The registry key could not be created: " + subKey);
        key.SetValue("", value);
    }

    public void DeleteKey(string subKey) => Registry.CurrentUser.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
}
