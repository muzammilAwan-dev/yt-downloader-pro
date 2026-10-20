using System.Globalization;
using Xunit;
using YTDLPHost.Models;
using YTDLPHost.Services;

namespace YTDLPHost.Tests;

/// <summary>
/// Characterization tests for the yt-dlp stdout parser (YtDlpRunner.HandleOutputCore).
/// Lines are real yt-dlp output shapes. Phase 1 swaps this regex scraping for
/// --progress-template; these tests are the contract that swap must still satisfy.
/// </summary>
public class YtDlpOutputParserTests
{
    private static readonly string Dir = "downloads";

    private static DownloadTask Run(params string[] lines) => Run(new DownloadTask { CurrentPhase = "Starting..." }, lines);

    private static DownloadTask Run(DownloadTask task, params string[] lines)
    {
        var runner = new YtDlpRunner();
        foreach (var line in lines) runner.HandleOutputCore(line, task);
        return task;
    }

    // ---------------- "already on disk" detection (a skipped download must not look like a real one) ----------------

    private static YtDlpRunner RunnerAfter(params string[] lines)
    {
        var runner = new YtDlpRunner();
        var task = new DownloadTask { CurrentPhase = "Starting..." };
        foreach (var line in lines) runner.HandleOutputCore(line, task);
        return runner;
    }

    [Fact]
    public void A_download_that_only_found_its_file_already_on_disk_is_flagged()
    {
        // The shape of a crop that reused the name of the full download: nothing but "has already been downloaded" and sidecar work.
        var runner = RunnerAfter(
            "[info] abc: Downloading 1 time ranges: 0.0-60.0",
            "[info] Writing video thumbnail 45 to: " + Dir + "/Title_360p.webp",
            "[download] " + Dir + "/Title_360p.mp4 has already been downloaded",
            "[EmbedThumbnail] mutagen: Adding thumbnail to \"" + Dir + "/Title_360p.mp4\"");

        Assert.True(runner.OnlyAlreadyOnDisk);
    }

    [Fact]
    public void A_download_that_wrote_a_new_file_is_not_flagged_even_if_a_leftover_stream_existed()
    {
        var runner = RunnerAfter(
            "[download] Destination: " + Dir + "/Title.f137.mp4",
            "[download] " + Dir + "/Title.f251.webm has already been downloaded");

        Assert.False(runner.OnlyAlreadyOnDisk);
    }

    [Fact]
    public void A_merge_means_something_new_was_made()
    {
        var runner = RunnerAfter(
            "[download] " + Dir + "/Title.f251.webm has already been downloaded",
            "[Merger] Merging formats into \"" + Dir + "/Title.mp4\"");

        Assert.False(runner.OnlyAlreadyOnDisk);
    }

    [Fact]
    public void A_plain_download_and_a_bulk_job_are_never_flagged()
    {
        Assert.False(RunnerAfter("[download] Destination: " + Dir + "/Title.mp4").OnlyAlreadyOnDisk);
        Assert.False(RunnerAfter(
            "[download] Downloading item 1 of 3",
            "[download] " + Dir + "/One.mp4 has already been downloaded").OnlyAlreadyOnDisk); // bulk jobs have their own summary
    }

    [Fact]
    public void Progress_line_fills_percent_speed_eta_and_size()
    {
        var t = Run("[download]  42.5% of ~  10.00MiB at    2.00MiB/s ETA 00:04");

        Assert.Equal(42.5, t.Progress);
        Assert.Equal("2.00MiB/s", t.Speed);
        Assert.Equal("00:04", t.Eta);
        Assert.Equal("~10.00MiB", t.FileSize);
        Assert.Equal(DownloadStatus.Downloading, t.Status);
        Assert.False(t.IsIndeterminate);
    }

    [Fact]
    public void Hundred_percent_line_completes_progress()
    {
        var t = Run("[download] 100% of   10.00MiB in 00:00:05 at 2.00MiB/s");
        Assert.Equal(100.0, t.Progress);
    }

    [Fact]
    public void Destination_lines_track_video_then_audio_and_clean_the_title()
    {
        var video = Path.Combine(Dir, "My_Video_1080p.f137.mp4");
        var audio = Path.Combine(Dir, "My_Video_1080p.f251.webm");

        // One runner for both lines: video-vs-audio is decided from state the runner keeps per download.
        var runner = new YtDlpRunner();
        var t = new DownloadTask { CurrentPhase = "Starting..." };

        runner.HandleOutputCore($"[download] Destination: {video}", t);
        Assert.Equal(video, t.OutputPath);
        Assert.Contains(video, t.TrackedFiles);
        Assert.Equal("My_Video_1080p", t.Title);
        Assert.Equal("Downloading Video...", t.CurrentPhase);

        runner.HandleOutputCore($"[download] Destination: {audio}", t);
        Assert.Contains(audio, t.TrackedFiles);
        Assert.Equal("Downloading Audio...", t.CurrentPhase);
        Assert.Equal("My_Video_1080p", t.Title); // title is only taken from the first file
    }

    [Theory]
    [InlineData("clip.en.vtt", "Downloading Subtitles...")]
    [InlineData("clip.webp", "Downloading Thumbnail...")]
    [InlineData("clip.m4a", "Downloading Audio...")]
    public void Destination_phase_depends_on_file_type(string fileName, string expectedPhase)
    {
        var t = Run($"[download] Destination: {Path.Combine(Dir, fileName)}");
        Assert.Equal(expectedPhase, t.CurrentPhase);
    }

    [Fact]
    public void Playlist_item_line_resets_per_item_state()
    {
        var t = new DownloadTask { CurrentPhase = "Downloading Video...", Progress = 55, Speed = "1MiB/s" };
        Run(t, "[download] Downloading item 3 of 10");

        Assert.Equal("Item 3/10", t.PlaylistInfo);
        Assert.Equal(0.0, t.Progress);
        Assert.Equal("Fetching Title...", t.Title);
        Assert.Equal("Starting...", t.CurrentPhase);
        Assert.Equal("", t.Speed);
    }

    [Fact]
    public void Merger_line_records_the_final_merged_file_as_the_output()
    {
        // Per-stream files are deleted after the merge; only the "Merging formats into" line names the file that is kept.
        var t = Run(
            "[download] Destination: " + Dir + "/Title.f137.mp4",
            "[download] Destination: " + Dir + "/Title.f251.webm",
            "[Merger] Merging formats into \"" + Dir + "/Title.mp4\"");

        Assert.Equal(Dir + "/Title.mp4", t.OutputPath);
        Assert.Contains(Dir + "/Title.mp4", t.TrackedFiles);
    }

    [Fact]
    public void Merger_line_switches_to_indeterminate_finalizing()
    {
        var t = new DownloadTask { CurrentPhase = "Downloading Video...", Speed = "1MiB/s", Eta = "00:01" };
        Run(t, "[Merger] Merging formats into \"x.mp4\"");

        Assert.Equal("Merging & Finalizing...", t.CurrentPhase);
        Assert.True(t.IsIndeterminate);
        Assert.Equal("", t.Speed);
        Assert.Equal("", t.Eta);
    }

    [Fact]
    public void ExtractAudio_destination_sets_output_path()
    {
        var t = Run("[ExtractAudio] Destination: a.mp3");

        Assert.Equal("Converting Audio...", t.CurrentPhase);
        Assert.Equal("a.mp3", t.OutputPath);
        Assert.Contains("a.mp3", t.TrackedFiles);
    }

    [Theory]
    [InlineData("[SponsorBlock] Fetching SponsorBlock segments", "Removing Sponsors...")]
    [InlineData("[Metadata] Adding metadata to \"x.mp4\"", "Adding Metadata...")]
    public void Post_processing_phases(string line, string expectedPhase)
    {
        Assert.Equal(expectedPhase, Run(line).CurrentPhase);
    }

    [Fact]
    public void Already_downloaded_marks_complete()
    {
        var t = Run("[download] x.mp4 has already been downloaded");

        Assert.Equal(100.0, t.Progress);
        Assert.Equal("x.mp4", t.OutputPath);
    }

    [Fact]
    public void Extraction_line_moves_from_starting_to_extracting()
    {
        var t = Run("[youtube] Extracting URL: https://www.youtube.com/watch?v=abc");

        Assert.Equal("Extracting Info...", t.CurrentPhase);
        Assert.Equal(DownloadStatus.Downloading, t.Status);
    }

    [Theory]
    [InlineData("\u001b[0K")]
    [InlineData("[download] %")]
    [InlineData("[download] 12.3% of")]
    [InlineData("Destination:")]
    [InlineData("[download] Downloading item x of y")]
    [InlineData("total garbage \0 \t ~~~")]
    public void Unexpected_lines_never_throw(string line)
    {
        Assert.Null(Record.Exception(() => Run(line)));
    }

    [Fact]
    public void Progress_parsing_is_culture_invariant()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var t = Run("[download]  42.5% of ~  10.00MiB at    2.00MiB/s ETA 00:04");
            Assert.Equal(42.5, t.Progress);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void Extension_matching_is_culture_invariant()
    {
        // Under tr-TR "TIFF".ToLower() is "t\u0131ff" (dotless i), which used to miss the image list.
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            var t = Run($"[download] Destination: {Path.Combine(Dir, "clip.TIFF")}");
            Assert.Equal("Downloading Thumbnail...", t.CurrentPhase);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
