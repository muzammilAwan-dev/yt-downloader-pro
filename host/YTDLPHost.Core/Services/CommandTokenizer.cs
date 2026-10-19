using System.Text;

namespace YTDLPHost.Services
{
    /// <summary>
    /// Splits a command line into arguments. This is the ONE tokenizer in the app: <see cref="CommandValidator"/> uses it
    /// to check a command and <see cref="YtDlpCommandBuilder"/> uses it to build the argument list yt-dlp is started with,
    /// so what was validated is exactly what runs. Rules: spaces and tabs separate arguments, a double quote starts or ends
    /// a quoted part (the quotes themselves are dropped), and there is no escape character (the validator rejects a
    /// backslash before a quote, because Windows would read that differently).
    /// </summary>
    public static class CommandTokenizer
    {
        /// <summary>Returns the arguments, or null if a quote is never closed.</summary>
        public static List<string>? Tokenize(string command)
        {
            var tokens = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;
            bool hasToken = false;

            foreach (var ch in command)
            {
                if (ch == '"')
                {
                    inQuotes = !inQuotes;
                    hasToken = true;
                }
                else if ((ch == ' ' || ch == '\t') && !inQuotes)
                {
                    if (hasToken) { tokens.Add(current.ToString()); current.Clear(); hasToken = false; }
                }
                else
                {
                    current.Append(ch);
                    hasToken = true;
                }
            }

            if (inQuotes) return null;
            if (hasToken) tokens.Add(current.ToString());
            return tokens;
        }
    }
}
