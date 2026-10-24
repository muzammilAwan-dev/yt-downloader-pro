using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace YTDLPHost.Native
{
    /// <summary>
    /// Builds the messages the host sends to the browser (version 1), already UTF-8 encoded and ready for
    /// <see cref="NativeFrame.WriteAsync"/>. Fields that are null are left out. Texts that may contain
    /// attacker-influenced content are cut to a safe length.
    /// </summary>
    public static class NativeResponses
    {
        private const int MaxReasonChars = 300;

        // The text only ever goes to the browser's extension code, never into HTML, so the relaxed encoder is safe and keeps
        // apostrophes and non-ASCII letters readable instead of turning them into \uXXXX.
        private static readonly JsonSerializerOptions Options = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static byte[] Hello(string id, string hostVersion) =>
            Serialize(new { v = NativeProtocol.Version, id, type = "hello", hostVersion, protocol = NativeProtocol.Version });

        public static byte[] Accepted(string id) =>
            Serialize(new { v = NativeProtocol.Version, id, type = "accepted" });

        /// <summary>A refusal. The id is null when the request was too broken to read one.</summary>
        public static byte[] Rejected(string? id, NativeParseError error) =>
            Serialize(new { v = NativeProtocol.Version, id, type = "rejected", code = error.CodeName, reason = Cut(error.Message), field = error.Field });

        public static byte[] Progress(string id, double percent, string speed, string eta, string phase, string title) =>
            Serialize(new { v = NativeProtocol.Version, id, type = "progress", percent = Math.Round(percent, 1), speed, eta, phase, title = Cut(title) });

        public static byte[] Done(string id, string? note) =>
            Serialize(new { v = NativeProtocol.Version, id, type = "done", note = note == null ? null : Cut(note) });

        public static byte[] Error(string id, string reason) =>
            Serialize(new { v = NativeProtocol.Version, id, type = "error", reason = Cut(reason) });

        private static byte[] Serialize<T>(T message) => JsonSerializer.SerializeToUtf8Bytes(message, Options);

        private static string Cut(string text) => text.Length <= MaxReasonChars ? text : text[..MaxReasonChars] + "...";
    }
}
