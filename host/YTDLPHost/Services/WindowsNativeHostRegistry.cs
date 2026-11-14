using System.IO;
using Microsoft.Win32;
using YTDLPHost.Native;

namespace YTDLPHost.Services
{
    /// <summary>The Native Messaging registration values under HKCU: no administrator rights needed, one user at a time.</summary>
    public sealed class WindowsNativeHostRegistry : INativeHostRegistry
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
}
