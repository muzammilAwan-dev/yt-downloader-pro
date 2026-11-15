using System.Diagnostics;

namespace YTDLPHost.Services
{
    /// <summary>Starts a <see cref="ShellCommand"/> as a program (never through a shell). Shared by the open/reveal actions and the notifier.</summary>
    public static class ShellCommandRunner
    {
        private static readonly TimeSpan StartWait = TimeSpan.FromSeconds(2);

        /// <summary>Starts the program. One that is still running after two seconds (a file manager that stays open) counts as started.</summary>
        public static bool StartAndCheck(ShellCommand command)
        {
            var psi = new ProcessStartInfo { FileName = command.FileName, UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in command.Arguments) psi.ArgumentList.Add(argument);

            using var process = Process.Start(psi);
            if (process == null) return false;
            return !process.WaitForExit((int)StartWait.TotalMilliseconds) || process.ExitCode == 0;
        }
    }
}
