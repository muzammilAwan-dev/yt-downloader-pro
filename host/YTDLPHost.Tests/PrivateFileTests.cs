using System.Text;
using Xunit;
using YTDLPHost.Services;

namespace YTDLPHost.Tests;

/// <summary>Step 3.6: cookie files are written so that only their owner can read them (Unix), as UTF-8 without a byte-order mark (everywhere).</summary>
public class PrivateFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ytdlp-private-file-tests-" + Guid.NewGuid());

    public PrivateFileTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    [Fact]
    public void The_text_is_written_as_utf8_without_a_byte_order_mark()
    {
        string path = Path.Combine(_dir, "cookies.txt");

        PrivateFile.WriteAllText(path, "# Netscape HTTP Cookie File\n.example.com\tTRUE\t/\tTRUE\t0\tsid\täöü");

        Assert.Equal(new UTF8Encoding(false).GetBytes("# Netscape HTTP Cookie File\n.example.com\tTRUE\t/\tTRUE\t0\tsid\täöü"), File.ReadAllBytes(path));
    }

    [Fact]
    public void On_unix_a_new_file_is_readable_and_writable_by_its_owner_only()
    {
        if (OperatingSystem.IsWindows()) return;   // file modes exist only on Unix; the Linux CI job runs this
        string path = Path.Combine(_dir, "cookies.txt");

        PrivateFile.WriteAllText(path, "secret");

        Assert.Equal(OwnerOnly, File.GetUnixFileMode(path));
    }

    [Fact]
    public void On_unix_an_older_file_of_the_same_name_is_made_private_and_replaced()
    {
        if (OperatingSystem.IsWindows()) return;
        string path = Path.Combine(_dir, "cookies.txt");
        File.WriteAllText(path, "old and long content");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        PrivateFile.WriteAllText(path, "new");

        Assert.Equal("new", File.ReadAllText(path));
        Assert.Equal(OwnerOnly, File.GetUnixFileMode(path));
    }
}
