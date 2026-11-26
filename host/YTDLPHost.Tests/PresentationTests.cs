using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using YTDLPHost.Models;
using YTDLPHost.Services;
using YTDLPHost.ViewModels;

namespace YTDLPHost.Tests;

public class DownloadCardTextTests
{
    private static DownloadItemViewModel Card(Action<DownloadTask>? setup = null)
    {
        var task = new DownloadTask { Command = "yt-dlp x", Title = "My_Video_Title_1080p", Status = DownloadStatus.Downloading, CurrentPhase = "Downloading video" };
        setup?.Invoke(task);
        return new DownloadItemViewModel(task);
    }

    [Theory]
    [InlineData("My_Video_Title_1080p", "My Video Title")]
    [InlineData("Plain title", "Plain title")]
    [InlineData("A  lot   of   space", "A lot of space")]
    [InlineData("2160p", "2160p")] // never reduce a title to nothing
    public void Title_reads_like_a_person_would_write_it(string raw, string expected) =>
        Assert.Equal(expected, Card(t => t.Title = raw).DisplayTitle);

    [Fact]
    public void Fetching_placeholder_only_while_actually_fetching()
    {
        Assert.Equal("Fetching video...", Card(t => t.Title = "Unknown").DisplayTitle);
        Assert.Equal("Untitled download", Card(t => { t.Title = "Unknown"; t.Status = DownloadStatus.Error; }).DisplayTitle);
    }

    [Fact]
    public void Speed_is_split_into_number_and_unit_and_hidden_while_processing()
    {
        var card = Card(t => t.Speed = "3.30MiB/s");
        Assert.Equal("3.30", card.SpeedValue);
        Assert.Equal("MiB/s", card.SpeedUnit);

        var merging = Card(t => { t.Speed = "0B/s"; t.CurrentPhase = "Merging video and audio"; });
        Assert.True(merging.IsProcessing);
        Assert.Equal("", merging.SpeedText);
        Assert.True(merging.IsProgressIndeterminate);
    }

    [Fact]
    public void Eta_reads_as_elapsed_for_live_streams()
    {
        Assert.Equal("ETA 00:07", Card(t => t.Eta = "00:07").EtaText);
        Assert.Equal("00:42 elapsed", Card(t => { t.Eta = "00:42"; t.IsIndeterminate = true; }).EtaText);
        Assert.Equal("", Card(t => t.Eta = "Unknown").EtaText);
    }

    [Fact]
    public void Status_line_says_what_is_happening()
    {
        Assert.Equal("Paused at 42.0%", Card(t => { t.Status = DownloadStatus.Paused; t.Progress = 42; }).StatusLine);
        Assert.Equal("In queue", Card(t => { t.Status = DownloadStatus.Queued; t.CurrentPhase = "Queued"; }).StatusLine);
        Assert.Equal("Waiting for the same video to finish", Card(t => { t.Status = DownloadStatus.Queued; t.CurrentPhase = "Similar video running"; }).StatusLine);
        Assert.StartsWith("Completed \u00B7 1 saved", Card(t => { t.Status = DownloadStatus.Completed; t.ResultNote = "1 saved"; }).StatusLine);
        Assert.Equal("Downloading video \u00B7 12.5% of 80MiB", Card(t => { t.Progress = 12.5; t.FileSize = "80MiB"; }).StatusLine);
    }

    [Theory]
    [InlineData("ERROR: [youtube] x: HTTP Error 429: Too Many Requests", "The site is limiting requests (HTTP 429)")]
    [InlineData("ERROR: Sign in to confirm you're not a bot", "The site wants to confirm you\u2019re not a bot")]
    [InlineData("ERROR: Video unavailable. This video is private", "This video isn\u2019t available")]
    [InlineData("ERROR: Requested format is not available", "That quality isn\u2019t offered for this video")]
    [InlineData("ERROR: unable to download: HTTP Error 403: Forbidden", "The site refused the download (HTTP 403)")]
    [InlineData("ERROR: No space left on device", "Not enough disk space")]
    [InlineData("ERROR: Permission denied: '/x'", "Can\u2019t write to the save folder")]
    [InlineData("ERROR: getaddrinfo failed", "Connection problem")]
    [InlineData("", "Download failed")]
    public void Raw_errors_become_a_plain_title_with_a_next_step(string raw, string title)
    {
        var card = Card(t => { t.Status = DownloadStatus.Error; t.ErrorMessage = raw; });
        Assert.Equal(title, card.ErrorTitle);
        Assert.False(string.IsNullOrWhiteSpace(card.ErrorHint));
        Assert.Equal(title, card.StatusLine);
        Assert.Equal(raw, card.ErrorMessage); // the untouched original stays available under "Show details"
    }

    [Fact]
    public void Messages_the_app_wrote_itself_are_split_at_the_first_sentence()
    {
        var card = Card(t => { t.Status = DownloadStatus.Error; t.ErrorMessage = "Vimeo needs you to be logged in. Sign in in your browser, then retry."; });
        Assert.Equal("Vimeo needs you to be logged in.", card.ErrorTitle);
        Assert.Equal("Sign in in your browser, then retry.", card.ErrorHint);
    }

    [Theory]
    [InlineData(DownloadStatus.Queued, "Queued", "queued")]
    [InlineData(DownloadStatus.Downloading, "Downloading video", "downloading")]
    [InlineData(DownloadStatus.Downloading, "Merging video and audio", "processing")]
    [InlineData(DownloadStatus.Paused, "", "paused")]
    [InlineData(DownloadStatus.Completed, "", "completed")]
    [InlineData(DownloadStatus.Error, "", "failed")]
    [InlineData(DownloadStatus.Cancelled, "", "cancelled")]
    public void State_glyph_follows_status_and_phase(DownloadStatus status, string phase, string kind)
    {
        var card = Card(t => { t.Status = status; t.CurrentPhase = phase; });
        Assert.Equal(kind, card.StatusKind);
    }

    [Fact]
    public void Error_details_toggle_flips_the_button_text()
    {
        var card = Card();
        Assert.Equal("Show details", card.ErrorDetailsToggleText);
        card.ToggleErrorDetailsCommand.Execute(null);
        Assert.True(card.IsErrorDetailsVisible);
        Assert.Equal("Hide details", card.ErrorDetailsToggleText);
    }

    [Fact]
    public void Status_change_raises_StatusLabel_last_so_the_window_can_refresh_counts()
    {
        var card = Card();
        var raised = new System.Collections.Generic.List<string?>();
        card.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        card.Task.Status = DownloadStatus.Error;
        Assert.Equal(nameof(DownloadItemViewModel.StatusLabel), raised.Last());
        Assert.Equal("Failed", card.StatusLabel);
    }

    [Fact]
    public async Task Live_progress_updates_are_batched_into_one_refresh()
    {
        var card = Card();
        int statusLineChanges = 0;
        card.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(DownloadItemViewModel.StatusLine)) statusLineChanges++; };
        for (int i = 1; i <= 50; i++) card.Task.Progress = i;
        await Task.Delay(500);
        Assert.InRange(statusLineChanges, 1, 2);
    }
}

public class DownloadTaskLogTests
{
    [Fact]
    public void Progress_lines_replace_each_other_like_a_terminal()
    {
        var task = new DownloadTask { Command = "x" };
        task.AppendLog("Starting");
        for (int i = 1; i <= 5; i++) task.AppendLog($"[download]  {i * 10}.0% of 80MiB at 3MiB/s ETA 00:0{i}");
        task.AppendLog("Done");

        var lines = task.FullLogText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.Contains("50.0%", lines[1]);
    }

    [Fact]
    public void On_screen_log_is_only_built_while_it_is_open_and_keeps_3000_lines()
    {
        var task = new DownloadTask { Command = "x" };
        for (int i = 1; i <= 3100; i++) task.AppendLog("line " + i);
        Assert.Equal("", task.UiLogText);              // nobody is looking: no text is built

        task.SetUiLogActive(true);                      // opening shows the current log immediately
        var shown = task.UiLogText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3000, shown.Length);
        Assert.Equal("line 3100", shown[^1]);
        Assert.Contains("line 1" + Environment.NewLine, task.FullLogText); // the complete log is untouched
    }

    [Fact]
    public async Task Open_log_refreshes_at_most_a_few_times_a_second()
    {
        var task = new DownloadTask { Command = "x" };
        task.SetUiLogActive(true);
        int changes = 0;
        task.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(DownloadTask.UiLogText)) changes++; };
        for (int i = 0; i < 200; i++) task.AppendLog("line " + i);
        await Task.Delay(700);
        Assert.InRange(changes, 1, 3);
        Assert.EndsWith("line 199", task.UiLogText);
    }
}

public class QueueSummaryTests
{
    private static readonly DownloadStatus[] Mixed =
    {
        DownloadStatus.Downloading, DownloadStatus.Downloading, DownloadStatus.Queued, DownloadStatus.Paused,
        DownloadStatus.Completed, DownloadStatus.Error, DownloadStatus.Cancelled
    };

    [Fact]
    public void Counts_group_states_the_way_the_tabs_do()
    {
        var c = QueueSummary.Count(Mixed);
        Assert.Equal(new QueueCounts(7, 4, 1, 1, 1, 2, 1, 1), c);
        Assert.True(QueueSummary.CanClearFinished(c));
        Assert.False(QueueSummary.CanClearFinished(QueueSummary.Count(new[] { DownloadStatus.Downloading })));
    }

    [Theory]
    [InlineData(QueueFilter.All, 7)]
    [InlineData(QueueFilter.Active, 4)]
    [InlineData(QueueFilter.Done, 1)]
    [InlineData(QueueFilter.Failed, 1)]
    public void Filters_match_their_tab_counts(QueueFilter filter, int expected) =>
        Assert.Equal(expected, Mixed.Count(s => QueueSummary.Matches(filter, s)));

    [Fact]
    public void Filters_appear_from_four_cards_up()
    {
        Assert.False(QueueSummary.ShowFilters(3));
        Assert.True(QueueSummary.ShowFilters(4));
    }

    [Fact]
    public void Footer_describes_the_queue_unless_the_app_has_a_real_message()
    {
        Assert.Equal("2 downloading \u00B7 1 waiting \u00B7 1 paused", QueueSummary.FooterText("Added: Fetching video...", QueueSummary.Count(Mixed)));
        Assert.Equal("Ready", QueueSummary.FooterText("", QueueSummary.Count(Array.Empty<DownloadStatus>())));
        Assert.Equal("All downloads finished", QueueSummary.FooterText("All downloads complete", QueueSummary.Count(new[] { DownloadStatus.Completed })));
        Assert.Equal("Engine updated to 2026.11.01", QueueSummary.FooterText("Engine updated to 2026.11.01", QueueSummary.Count(Mixed)));
    }

    [Fact]
    public void Limits_label_shows_a_speed_cap_and_nothing_otherwise()
    {
        Assert.Equal("Limits", QueueSummary.LimitsLabel("0"));
        Assert.Equal("Limits", QueueSummary.LimitsLabel(null));
        Assert.Equal("Limit 5M/s", QueueSummary.LimitsLabel("5m"));
        Assert.Equal("3 downloads at once \u00B7 no speed limit", QueueSummary.LimitsTooltip(3, "0"));
        Assert.Equal("2 downloads at once \u00B7 500K/s speed limit", QueueSummary.LimitsTooltip(2, "500k"));
    }

    [Fact]
    public void Total_speed_adds_only_downloads_that_move_bytes()
    {
        DownloadItemViewModel Item(DownloadStatus s, string speed, string phase = "Downloading video") =>
            new(new DownloadTask { Command = "x", Status = s, Speed = speed, CurrentPhase = phase });

        var items = new[]
        {
            Item(DownloadStatus.Downloading, "1.00MiB/s"), Item(DownloadStatus.Downloading, "512.00KiB/s"),
            Item(DownloadStatus.Downloading, "9.00MiB/s", "Merging video and audio"), Item(DownloadStatus.Paused, "5.00MiB/s")
        };
        Assert.Equal("\u2193 1.50 MiB/s", QueueSummary.TotalSpeedText(items));
        Assert.Null(QueueSummary.TotalSpeedText(new[] { Item(DownloadStatus.Queued, "") }));
    }
}

public class RateMathAndThemeTests
{
    [Theory]
    [InlineData("3.30MiB/s", 3.30 * 1024 * 1024)]
    [InlineData("512KiB/s", 512 * 1024)]
    [InlineData("900B/s", 900)]
    [InlineData("Unknown B/s", 0)]
    [InlineData(null, 0)]
    public void Rates_parse_to_bytes_per_second(string? text, double bytes) => Assert.Equal(bytes, RateMath.ParseRate(text), 1);

    [Fact]
    public void Rates_format_with_a_matching_unit() => Assert.Equal(("1.50", "MiB/s"), RateMath.Format(1.5 * 1024 * 1024));

    [Fact]
    public void Both_themes_define_the_same_colors_and_every_color_is_valid_hex()
    {
        Assert.Equal(ThemePalette.Dark.Keys.OrderBy(k => k), ThemePalette.Light.Keys.OrderBy(k => k));
        foreach (var hex in ThemePalette.Dark.Values.Concat(ThemePalette.Light.Values))
            Assert.Matches("^#([0-9A-F]{6}|[0-9A-F]{8})$", hex.ToUpperInvariant());
    }

    [Theory]
    [InlineData("Light", ThemeMode.Light)]
    [InlineData("system", ThemeMode.System)]
    [InlineData("Dark", ThemeMode.Dark)]
    [InlineData("nonsense", ThemeMode.Dark)]
    [InlineData(null, ThemeMode.Dark)]
    public void Saved_theme_text_maps_to_a_mode(string? text, ThemeMode mode) => Assert.Equal(mode, ThemePalette.Parse(text));

    [Fact]
    public void Theme_setting_defaults_to_dark_and_survives_a_round_trip()
    {
        Assert.Equal("Dark", new AppSettings().Theme);
        var json = System.Text.Json.JsonSerializer.Serialize(new AppSettings { Theme = "Light" });
        Assert.Equal("Light", System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)!.Theme);
    }
}
