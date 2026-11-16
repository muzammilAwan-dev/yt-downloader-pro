using System.Diagnostics;

namespace YTDLPHost.Services
{
    /// <summary>
    /// Phase 4, step 4.2b: open and reveal on Windows for apps that do not use WPF (the Avalonia app). Same behaviour as the WPF app's own class:
    /// Explorer selects the file, the shell opens folders and files. Never throws: a failure is logged.
    /// </summary>
    public sealed class ShellExecuteActions : IShellActions
    {
        public void RevealFile(string path) =>
            Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"/select,\"{path}\"", UseShellExecute = true });

        public void OpenFolder(string path) =>
            Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });

        public void OpenFile(string path) =>
            Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });

        private static void Start(ProcessStartInfo psi)
        {
            try { Process.Start(psi)?.Dispose(); }
            catch (Exception ex) { AppLogger.Log($"[SHELL ERROR] Could not open '{psi.FileName} {psi.Arguments}': {ex.Message}"); }
        }
    }
}
