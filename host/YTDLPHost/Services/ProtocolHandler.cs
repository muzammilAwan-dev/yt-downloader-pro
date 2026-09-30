using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;

namespace YTDLPHost.Services
{
    public static class ProtocolHandler
    {
        private const string ProtocolKey = @"Software\Classes\ytdlp";
        private const string ProtocolName = "URL:YT Downloader Protocol";
        private const string PipeName = "YTDLPHost_Pipe";

        // FIX: setup.iss registers the protocol under HKLM at install time (admin
        // rights are available then). This class previously only ever read/wrote
        // HKCU, so IsRegistered() always returned false right after a fresh install,
        // causing the app to silently create a duplicate HKCU entry on first launch.
        // The two hives could then drift out of sync - e.g. "Unregister" only ever
        // removed the HKCU copy while the HKLM one (from the installer) kept working.
        // setup.iss now writes matching entries to both hives, and this class checks
        // both here so a fresh install is recognized immediately.

        public static bool IsRegistered()
        {
            return IsRegisteredIn(Registry.CurrentUser) || IsRegisteredIn(Registry.LocalMachine);
        }

        private static bool IsRegisteredIn(RegistryKey root)
        {
            try
            {
                using var key = root.OpenSubKey(ProtocolKey + @"\shell\open\command");
                if (key == null)
                    return false;

                var value = key.GetValue("") as string;
                if (string.IsNullOrEmpty(value))
                    return false;

                var exePath = GetExecutablePath();
                return value.Contains(exePath, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Self-heal / repair path. Writes to HKCU only: this requires no elevation
        /// and takes precedence over HKLM for the current user, so it safely wins
        /// even if the HKLM entry from the installer is missing or stale (e.g. after
        /// the app was moved). The installer owns the HKLM entry; this method never
        /// touches it.
        /// </summary>
        public static void Register()
        {
            try
            {
                if (IsRegistered())
                    return;

                var exePath = GetExecutablePath();

                using var protocolKey = Registry.CurrentUser.CreateSubKey(ProtocolKey);
                protocolKey?.SetValue("", ProtocolName);
                protocolKey?.SetValue("URL Protocol", "");

                using var commandKey = Registry.CurrentUser.CreateSubKey(ProtocolKey + @"\shell\open\command");
                commandKey?.SetValue("", $"\"{exePath}\" \"%1\"");

                AppLogger.Log("[PROTOCOL] Registered ytdlp:// handler under HKCU.");
            }
            catch (UnauthorizedAccessException)
            {
                System.Windows.MessageBox.Show(
                    "Failed to register the ytdlp:// protocol. Please run the application as Administrator once to register the protocol handler.",
                    "Protocol Registration Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to register protocol handler: {ex.Message}",
                    "Protocol Registration Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Removes only the HKCU entry this app manages. The HKLM entry written by
        /// the installer is intentionally left alone - removing it would require
        /// admin elevation this process doesn't have, and it's cleaned up correctly
        /// by the uninstaller (setup.iss) instead.
        /// </summary>
        public static void Unregister()
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(ProtocolKey, throwOnMissingSubKey: false);
                Debug.WriteLine("Protocol handler unregistered from HKCU.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to unregister protocol: {ex.Message}");
            }
        }

        private static string GetExecutablePath()
        {
            var path = Environment.ProcessPath;

            if (string.IsNullOrEmpty(path))
            {
                path = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            }

            return path;
        }
    }
}