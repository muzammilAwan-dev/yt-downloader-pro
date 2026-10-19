using Xunit;
using YTDLPHost.Services;

namespace YTDLPHost.Tests;

public class CommandTokenizerTests
{
    [Fact]
    public void Spaces_and_tabs_separate_arguments()
    {
        Assert.Equal(new[] { "a", "b", "c" }, CommandTokenizer.Tokenize("a  b\tc")!.ToArray());
    }

    [Fact]
    public void Quotes_group_words_and_are_dropped()
    {
        Assert.Equal(new[] { "a", "b c", "d" }, CommandTokenizer.Tokenize("a \"b c\" d")!.ToArray());
    }

    [Fact]
    public void Quoted_and_unquoted_parts_next_to_each_other_form_one_argument()
    {
        Assert.Equal(new[] { "prefix edpost" }, CommandTokenizer.Tokenize("pre\"fix ed\"post")!.ToArray());
    }

    [Fact]
    public void An_empty_quoted_value_is_an_empty_argument()
    {
        Assert.Equal(new[] { "a", "", "b" }, CommandTokenizer.Tokenize("a \"\" b")!.ToArray());
    }

    [Fact]
    public void Backslashes_are_ordinary_characters()
    {
        Assert.Equal(new[] { "-o", @"C:\dir\file.mp4" }, CommandTokenizer.Tokenize(@"-o ""C:\dir\file.mp4""")!.ToArray());
    }

    [Fact]
    public void An_unclosed_quote_returns_null()
    {
        Assert.Null(CommandTokenizer.Tokenize("a \"b c"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_to_split_gives_an_empty_list(string command)
    {
        Assert.Empty(CommandTokenizer.Tokenize(command)!);
    }
}
