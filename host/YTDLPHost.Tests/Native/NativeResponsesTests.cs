using System.Text;
using Xunit;
using YTDLPHost.Native;

namespace YTDLPHost.Tests.Native;

public class NativeResponsesTests
{
    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes);

    [Fact]
    public void Hello_reports_the_host_version_and_protocol()
    {
        Assert.Equal("{\"v\":1,\"id\":\"h1\",\"type\":\"hello\",\"hostVersion\":\"6.4.9\",\"protocol\":1}", Text(NativeResponses.Hello("h1", "6.4.9")));
    }

    [Fact]
    public void Accepted_carries_the_request_id()
    {
        Assert.Equal("{\"v\":1,\"id\":\"d1\",\"type\":\"accepted\"}", Text(NativeResponses.Accepted("d1")));
    }

    [Fact]
    public void A_rejection_names_the_code_the_reason_and_the_field()
    {
        var error = new NativeParseError(NativeErrorCode.BadField, "'urls' must be an array", "urls");

        Assert.Equal("{\"v\":1,\"id\":\"d1\",\"type\":\"rejected\",\"code\":\"bad_field\",\"reason\":\"'urls' must be an array\",\"field\":\"urls\"}",
                     Text(NativeResponses.Rejected("d1", error)));
    }

    [Fact]
    public void A_rejection_without_a_readable_id_leaves_the_id_and_field_out()
    {
        var error = new NativeParseError(NativeErrorCode.BadJson, "not json");

        Assert.Equal("{\"v\":1,\"type\":\"rejected\",\"code\":\"bad_json\",\"reason\":\"not json\"}", Text(NativeResponses.Rejected(null, error)));
    }

    [Theory]
    [InlineData(NativeErrorCode.BadJson, "bad_json")]
    [InlineData(NativeErrorCode.BadVersion, "bad_version")]
    [InlineData(NativeErrorCode.BadType, "bad_type")]
    [InlineData(NativeErrorCode.BadField, "bad_field")]
    public void Every_error_code_has_a_stable_wire_name(NativeErrorCode code, string expected)
    {
        Assert.Equal(expected, new NativeParseError(code, "x").CodeName);
    }

    [Fact]
    public void Progress_rounds_the_percentage_to_one_decimal()
    {
        var json = Text(NativeResponses.Progress("d1", 41.2345, "3.1MiB/s", "00:42", "Downloading Video...", "My video"));

        Assert.Equal("{\"v\":1,\"id\":\"d1\",\"type\":\"progress\",\"percent\":41.2,\"speed\":\"3.1MiB/s\",\"eta\":\"00:42\",\"phase\":\"Downloading Video...\",\"title\":\"My video\"}", json);
    }

    [Fact]
    public void Done_leaves_out_a_missing_note()
    {
        Assert.Equal("{\"v\":1,\"id\":\"d1\",\"type\":\"done\"}", Text(NativeResponses.Done("d1", null)));
        Assert.Equal("{\"v\":1,\"id\":\"d1\",\"type\":\"done\",\"note\":\"already existed\"}", Text(NativeResponses.Done("d1", "already existed")));
    }

    [Fact]
    public void An_error_message_is_cut_to_a_safe_length()
    {
        var json = Text(NativeResponses.Error("d1", new string('x', 1000)));

        Assert.Contains("\"type\":\"error\"", json);
        Assert.True(json.Length < 450, $"expected a short message, got {json.Length} characters");
    }

    [Fact]
    public void Every_response_fits_chromes_one_megabyte_limit_even_with_long_texts()
    {
        var bytes = NativeResponses.Progress("d1", 1, "s", "e", new string('p', 100_000), new string('t', 100_000));

        Assert.True(bytes.Length < NativeFrame.MaxHostToBrowserBytes);
    }
}
