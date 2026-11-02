using AIContext;
using Xunit;

namespace AIContext.Tests;

public class FrontmatterTests
{
    [Fact]
    public void Parse_WithoutFrontmatter_ReturnsWholeTextAsBody()
    {
        var result = Frontmatter.Parse("Just text.\n");

        Assert.Null(result.Error);
        Assert.Empty(result.Values);
        Assert.Equal("Just text.\n", result.Body);
    }

    [Fact]
    public void Parse_ReadsScalarsWithAndWithoutQuotes()
    {
        var result = Frontmatter.Parse("---\ndescription: \"UI: components\"\nname: plain value\nother: 'it''s'\n---\nBody");

        Assert.Null(result.Error);
        Assert.Equal("UI: components", result.Values["description"].Scalar);
        Assert.Equal("plain value", result.Values["name"].Scalar);
        Assert.Equal("it's", result.Values["other"].Scalar);
        Assert.Equal("Body", result.Body);
    }

    [Fact]
    public void Parse_AcceptsUnquotedGlobsInInlineAndBlockLists()
    {
        var inline = Frontmatter.Parse("---\npaths: [**/*.ts, \"src/**\"]\n---\nx");
        var block = Frontmatter.Parse("---\npaths:\n  - **/*.ts\n  - \"src/**\"\n---\nx");

        Assert.Equal(["**/*.ts", "src/**"], inline.Values["paths"].List);
        Assert.Equal(["**/*.ts", "src/**"], block.Values["paths"].List);
    }

    [Fact]
    public void SplitTopLevel_KeepsCommasInsideBraces()
    {
        var parts = Frontmatter.SplitTopLevel("src/**/*.{ts,tsx}, lib/**");

        Assert.Equal(["src/**/*.{ts,tsx}", "lib/**"], parts);
    }

    [Fact]
    public void Parse_HandlesCrlf()
    {
        var result = Frontmatter.Parse("---\r\ndescription: Hello\r\n---\r\nBody\r\n");

        Assert.Equal("Hello", result.Values["description"].Scalar);
        Assert.DoesNotContain("\r", result.Body);
    }

    [Fact]
    public void Parse_UnclosedFrontmatter_ReturnsError()
    {
        var result = Frontmatter.Parse("---\ndescription: Hello\nBody");

        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Parse_UnsupportedLine_ReturnsError()
    {
        var result = Frontmatter.Parse("---\nthis is not a pair\n---\nBody");

        Assert.NotNull(result.Error);
    }
}
