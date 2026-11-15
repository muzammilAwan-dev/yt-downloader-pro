using System.Text;

namespace YTDLPHost.Services
{
    /// <summary>
    /// Phase 3, step 3.6: writes a file that holds secrets (the session cookies handed over by the browser) so that only its owner can read it.
    /// On macOS and Linux the file is CREATED with mode 0600 (no moment where it exists with wider rights; the temp folder there is shared by all users).
    /// On Windows the per-user temp folder already protects it, so nothing extra is done. The text is written as UTF-8 without a byte-order mark.
    /// </summary>
    public static class PrivateFile
    {
        public static void WriteAllText(string path, string content)
        {
            var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

            using (var stream = new FileStream(path, options))
            {
                var bytes = new UTF8Encoding(false).GetBytes(content);
                stream.Write(bytes, 0, bytes.Length);
            }

            // UnixCreateMode only applies to a file that did not exist; an older file of the same name must end up private too.
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
