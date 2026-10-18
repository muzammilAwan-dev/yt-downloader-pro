using Xunit;
using YTDLPHost.Models;
using YTDLPHost.Services;

namespace YTDLPHost.Tests;

/// <summary>
/// Characterization tests for the cleanup code moved out of MainViewModel (step 1.5e): they pin down what
/// a finished or cancelled download deletes, and above all what it must never delete.
/// </summary>
public class DownloadCleanerTests : IDisposable
{
    private readonly string _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "cleaner-" + Guid.NewGuid())).FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string Make(string name)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, "x");
        return path;
    }

    private static DownloadTask TaskTracking(string outputPath, params string[] tracked)
    {
        var task = new DownloadTask { Command = "yt-dlp x", OutputPath = outputPath };
        foreach (var file in tracked) task.TrackedFiles.Add(file);
        return task;
    }

    // ---------------- after a successful download ----------------

    [Fact]
    public void A_finished_download_removes_stream_leftovers_but_keeps_the_final_file()
    {
        var video = Make("Title.f137.mp4");
        var audio = Make("Title.f251.webm");
        var final = Make("Title.mp4");

        DownloadCleaner.CleanupPartialFiles(TaskTracking(final, video, audio, final));

        Assert.False(File.Exists(video));
        Assert.False(File.Exists(audio));
        Assert.True(File.Exists(final));
    }

    // ---------------- after a cancel ----------------

    [Fact]
    public void Cancelling_removes_partial_files_and_fragments_but_not_other_files()
    {
        var tracked = Make("Title.f137.mp4");
        var part = Make("Title.f137.mp4.part");
        var resume = Make("Title.f137.mp4.ytdl");
        var otherStreamPart = Make("Title.f251.webm.part");
        var fragment = Make("Title.f251.webm.part-Frag3");
        var finishedSameName = Make("Title.mp4");
        var unrelated = Make("Other.mp4");

        DownloadCleaner.CleanupPartialFiles(TaskTracking(tracked, tracked), forceDeleteAll: true);

        Assert.False(File.Exists(tracked));
        Assert.False(File.Exists(part));
        Assert.False(File.Exists(resume));
        Assert.False(File.Exists(otherStreamPart));
        Assert.False(File.Exists(fragment));
        Assert.True(File.Exists(finishedSameName)); // complete files are never swept
        Assert.True(File.Exists(unrelated));
    }

    [Fact]
    public void Cancelling_a_bulk_job_keeps_files_that_are_already_complete()
    {
        var complete = Make("Song.mp4");
        var completePart = Make("Song.mp4.part");
        var intermediate = Make("Song.f137.mp4");
        var neighbour = Make("Song (remix).mp4");
        var task = TaskTracking(complete, complete, intermediate);
        task.PlaylistInfo = "Item 2/5";

        DownloadCleaner.CleanupPartialFiles(task, forceDeleteAll: true);

        Assert.True(File.Exists(complete));        // finished item of the job: kept
        Assert.False(File.Exists(completePart));   // its unfinished companion: removed
        Assert.False(File.Exists(intermediate));   // per-stream leftovers are never a deliverable
        Assert.True(File.Exists(neighbour));
    }

    [Fact]
    public void Files_already_settled_by_a_bulk_job_are_never_deleted()
    {
        var settledFile = Make("Keep.f137.mp4");
        var task = TaskTracking(settledFile, settledFile);
        task.SettledFiles.Add(settledFile);

        DownloadCleaner.CleanupPartialFiles(task, forceDeleteAll: true);

        Assert.True(File.Exists(settledFile));
    }

    // ---------------- stray thumbnails ----------------

    [Fact]
    public void Leftover_thumbnails_are_removed_but_the_media_and_subtitles_stay()
    {
        var media = Make("Title.mp4");
        var subtitles = Make("Title.srt");
        var images = new[] { Make("Title.webp"), Make("Title.jpg"), Make("Title.jpeg"), Make("Title.png") };

        DownloadCleaner.ScrubStrayThumbnails(TaskTracking(media));

        Assert.True(File.Exists(media));
        Assert.True(File.Exists(subtitles));
        Assert.All(images, image => Assert.False(File.Exists(image)));
    }

    [Fact]
    public void A_picture_download_keeps_its_own_file_but_loses_other_image_formats()
    {
        var picture = Make("Pic.jpg");
        var otherFormat = Make("Pic.webp");

        DownloadCleaner.ScrubStrayThumbnails(TaskTracking(picture));

        Assert.True(File.Exists(picture));
        Assert.False(File.Exists(otherFormat));
    }

    // ---------------- cookie file ----------------

    [Fact]
    public void The_temporary_cookie_file_is_deleted_and_missing_or_blank_paths_are_ignored()
    {
        var cookies = Make("cookies.txt");

        DownloadCleaner.CleanupCookieFile(new DownloadTask { CookieFilePath = cookies });
        Assert.False(File.Exists(cookies));

        var ex = Record.Exception(() =>
        {
            DownloadCleaner.CleanupCookieFile(new DownloadTask { CookieFilePath = cookies }); // already gone
            DownloadCleaner.CleanupCookieFile(new DownloadTask { CookieFilePath = "" });
            DownloadCleaner.ScrubStrayThumbnails(new DownloadTask { OutputPath = "" });
        });
        Assert.Null(ex);
    }
}
