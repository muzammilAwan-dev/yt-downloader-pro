using Xunit;
using YTDLPHost.Models;
using YTDLPHost.Services;

namespace YTDLPHost.Tests;

/// <summary>The "open folder" / "play" decisions that moved out of MainViewModel (step 1.5d). The shell itself is faked.</summary>
public class DownloadOpenerTests : IDisposable
{
    private sealed class FakeShell : IShellActions
    {
        public List<string> Calls { get; } = new();
        public void RevealFile(string path) => Calls.Add("reveal:" + path);
        public void OpenFolder(string path) => Calls.Add("folder:" + path);
        public void OpenFile(string path) => Calls.Add("open:" + path);
    }

    private readonly string _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "dlopener-" + Guid.NewGuid())).FullName;
    private readonly FakeShell _shell = new();
    private readonly DownloadOpener _opener;

    public DownloadOpenerTests()
    {
        _opener = new DownloadOpener(_shell) { SaveDirectoryFromCommand = _ => Path.Combine(_dir, "no-such-save-folder") };
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string MakeFile(string name)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public void Shows_the_finished_file_selected_when_it_exists()
    {
        var file = MakeFile("video.mp4");

        _opener.ShowInFolder(new DownloadTask { Command = "yt-dlp x", OutputPath = file });

        Assert.Equal(new[] { "reveal:" + file }, _shell.Calls);
    }

    [Fact]
    public void Opens_the_files_folder_when_the_file_is_gone()
    {
        var gone = Path.Combine(_dir, "deleted.mp4");

        _opener.ShowInFolder(new DownloadTask { Command = "yt-dlp x", OutputPath = gone });

        Assert.Equal(new[] { "folder:" + _dir }, _shell.Calls);
    }

    [Fact]
    public void Opens_the_save_folder_from_the_command_when_there_is_no_output_path()
    {
        var saveDir = Directory.CreateDirectory(Path.Combine(_dir, "save")).FullName;
        _opener.SaveDirectoryFromCommand = command =>
        {
            Assert.Equal("yt-dlp -o save-here", command); // the command is what the resolver receives
            return saveDir;
        };

        _opener.ShowInFolder(new DownloadTask { Command = "yt-dlp -o save-here", OutputPath = "" });

        Assert.Equal(new[] { "folder:" + saveDir }, _shell.Calls);
    }

    [Fact]
    public void Does_nothing_when_neither_the_file_nor_any_folder_exists()
    {
        var missingDir = Path.Combine(_dir, "never-created");

        _opener.ShowInFolder(new DownloadTask { Command = "yt-dlp x", OutputPath = Path.Combine(missingDir, "a.mp4") });

        Assert.Empty(_shell.Calls);
    }

    [Fact]
    public void Play_opens_the_file_with_its_default_app()
    {
        var file = MakeFile("movie.mkv");

        _opener.Play(new DownloadTask { Command = "yt-dlp x", OutputPath = file });

        Assert.Equal(new[] { "open:" + file }, _shell.Calls);
    }

    [Fact]
    public void Play_falls_back_to_the_folder_when_the_file_is_gone()
    {
        var gone = Path.Combine(_dir, "deleted.mkv");

        _opener.Play(new DownloadTask { Command = "yt-dlp x", OutputPath = gone });

        Assert.Equal(new[] { "folder:" + _dir }, _shell.Calls);
    }
}
