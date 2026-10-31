using System.IO.Pipes;

namespace YTDLPHost.Native
{
    /// <summary>
    /// Where the app and the shim meet. Both sides build the name and the options here so they can never disagree. This file is also
    /// compiled into the shim (see YTDLPHost.Shim.csproj), so it must stay free of anything but the BCL.
    /// </summary>
    public static class NativePipe
    {
        /// <summary>Server side: asynchronous, and only the same user (account and elevation level on Windows) may connect.</summary>
        public const PipeOptions ServerOptions = PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly;

        /// <summary>Client side: asynchronous, and only a server of the same user is accepted.</summary>
        public const PipeOptions ClientOptions = PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly;

        /// <summary>A per-user name, so two logged-in users never share a pipe. Only letters, digits and _ - . are kept.</summary>
        public static string NameForCurrentUser()
        {
            string user = new string(Environment.UserName.Where(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.').ToArray());
            if (user.Length > 32) user = user[..32];
            if (user.Length == 0) user = "user";
            return "YTDLPHost_Native_" + user;
        }
    }
}
