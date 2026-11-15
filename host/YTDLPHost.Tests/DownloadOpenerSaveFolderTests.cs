using Xunit;
using YTDLPHost.Services;

namespace YTDLPHost.Tests;

/// <summary>Step 3.6: the save folder named by a command, with "~" and forward slashes working on every OS. Uses its own home folder, never the global AppPaths.</summary>
public class DownloadOpenerSaveFolderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ytdlp-savedir-tests-" + Guid.NewGuid());
    private string Home => Path.Combine(_root, "home");

    public DownloadOpenerSaveFolderTests() => Directory.CreateDirectory(Home);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private string Folder(string template) =>
        DownloadOpener.ExtractSaveDirectory($"yt-dlp -o \"{template}\" \"https://example.com/v\"", Home);

    [Fact]
    public void A_tilde_folder_is_taken_from_the_home_folder_and_created()
    {
        string dir = Folder("~/Videos/%(title)s.mp4");

        Assert.Equal(Path.Combine(Home, "Videos"), dir);
        Assert.True(Directory.Exists(dir));
    }

    [Fact]
    public void A_template_directly_under_tilde_means_the_home_folder_itself()
    {
        Assert.Equal(Home, Folder("~/%(title)s.mp4"));
    }

    [Fact]
    public void An_absolute_folder_is_used_as_it_is()
    {
        string wanted = Path.Combine(_root, "out", "clips");

        Assert.Equal(wanted, Folder(wanted.Replace('\\', '/') + "/%(title)s.mp4"));
        Assert.True(Directory.Exists(wanted));
    }

    [Fact]
    public void Without_an_output_template_the_home_folder_is_used()
    {
        Assert.Equal(Home, DownloadOpener.ExtractSaveDirectory("yt-dlp \"https://example.com/v\"", Home));
    }
}
