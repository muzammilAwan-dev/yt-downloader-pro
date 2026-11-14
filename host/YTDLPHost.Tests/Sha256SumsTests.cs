using Xunit;
using YTDLPHost.Services;

namespace YTDLPHost.Tests;

/// <summary>Phase 3, step 3.5: reading the checksum files of yt-dlp, BtbN and Deno. The same samples are checked by tools/test-engine-checksums.js.</summary>
public class Sha256SumsTests
{
    private static readonly string A = new('a', 64);
    private static readonly string B = new('b', 64);
    private static readonly string C = new('c', 64);

    [Fact]
    public void A_gnu_style_list_is_searched_by_file_name()
    {
        string text = $"{A}  yt-dlp\n{B}  yt-dlp.exe\n{C}  yt-dlp_macos\n";

        Assert.Equal(B, Sha256Sums.FindHash(text, "yt-dlp.exe"));
        Assert.Equal(A, Sha256Sums.FindHash(text, "yt-dlp"));
        Assert.Equal(C, Sha256Sums.FindHash(text, "yt-dlp_macos"));
    }

    [Fact]
    public void Windows_line_endings_a_binary_marker_and_a_path_prefix_are_fine()
    {
        string text = $"{A} *./ffmpeg-master-latest-win64-gpl.zip\r\n{B}  ffmpeg-master-latest-linux64-gpl.tar.xz\r\n";

        Assert.Equal(A, Sha256Sums.FindHash(text, "ffmpeg-master-latest-win64-gpl.zip"));
        Assert.Equal(B, Sha256Sums.FindHash(text, "ffmpeg-master-latest-linux64-gpl.tar.xz"));
    }

    [Fact]
    public void A_hash_in_capitals_comes_back_in_lower_case()
    {
        Assert.Equal(A, Sha256Sums.FindHash($"{A.ToUpperInvariant()}  tool.exe\n", "tool.exe"));
    }

    [Theory]
    [InlineData("my-yt-dlp.exe")]
    [InlineData("yt-dlp.exe.sig")]
    [InlineData("dlp.exe")]
    public void Another_file_with_a_similar_name_is_not_a_match(string listed)
    {
        Assert.Null(Sha256Sums.FindHash($"{A}  {listed}\n", "yt-dlp.exe"));
    }

    [Fact]
    public void A_file_that_is_not_listed_has_no_hash()
    {
        Assert.Null(Sha256Sums.FindHash($"{A}  other.zip\n", "tool.zip"));
        Assert.Null(Sha256Sums.FindHash("", "tool.zip"));
        Assert.Null(Sha256Sums.FindHash("no hashes in here\n", "tool.zip"));
    }

    [Fact]
    public void A_hash_that_is_longer_or_shorter_than_64_digits_is_not_a_hash()
    {
        Assert.Null(Sha256Sums.FindHash(A + "f  tool.zip\n", "tool.zip"));
        Assert.Null(Sha256Sums.FindHash(A.Substring(1) + "  tool.zip\n", "tool.zip"));
    }

    [Fact]
    public void A_powershell_style_table_is_read_by_the_file_name_at_the_end_of_the_line()
    {
        string text = "Algorithm       Hash                                                                   Path\r\n" +
                      "---------       ----                                                                   ----\r\n" +
                      $"SHA256          {A.ToUpperInvariant()}       D:\\a\\deno\\target\\release\\deno-x86_64-pc-windows-msvc.zip\r\n";

        Assert.Equal(A, Sha256Sums.FindHash(text, "deno-x86_64-pc-windows-msvc.zip"));
    }

    [Fact]
    public void A_bare_hash_is_only_accepted_when_asked_for_and_when_there_is_exactly_one()
    {
        Assert.Null(Sha256Sums.FindHash(A + "\n", "tool.zip"));
        Assert.Equal(A, Sha256Sums.FindHash(A + "\n", "tool.zip", allowBare: true));
        Assert.Equal(A, Sha256Sums.FindHash($"{A}\n{A}\n", "tool.zip", allowBare: true));
        Assert.Null(Sha256Sums.FindHash($"{A}\n{B}\n", "tool.zip", allowBare: true));
    }

    [Fact]
    public void A_named_entry_wins_over_the_bare_rule()
    {
        Assert.Equal(B, Sha256Sums.FindHash($"{A}  other.zip\n{B}  tool.zip\n", "tool.zip", allowBare: true));
    }

    [Fact]
    public void The_hash_of_known_bytes_is_the_published_sha256()
    {
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", Sha256Hex.Of("abc"u8.ToArray()));
    }
}
