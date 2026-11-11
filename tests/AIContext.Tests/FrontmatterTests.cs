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

    [Theory]
    [InlineData("---\n  paths: src/**\n---\nBody")]
    [InlineData("---\n- a\n---\nBody")]
    [InlineData("---\n: value\n---\nBody")]
    public void Parse_LineThatBelongsToNoKey_ReturnsError(string text)
    {
        Assert.NotNull(Frontmatter.Parse(text).Error);
    }

    [Fact]
    public void Parse_RepeatedKey_ReturnsErrorNamingTheLine()
    {
        var result = Frontmatter.Parse("---\nowner: a\npaths: x\nowner: b\n---\nBody");

        Assert.Contains("duplicate key 'owner'", result.Error);
        Assert.Contains("line 4", result.Error);
    }

    [Fact]
    public void Parse_NestedOrMultiLineValue_IsRecordedAndIsNoError()
    {
        var result = Frontmatter.Parse("---\nowner:\n  team: web\n  tags:\n    - a\ntext: |\n  line\nend: x\n---\nBody");

        Assert.Null(result.Error);
        Assert.True(result.Values["owner"].IsNested);
        Assert.True(result.Values["text"].IsNested);
        Assert.Equal("x", result.Values["end"].Scalar);
    }

    [Fact]
    public void Parse_BlockListItemsMayBeIndentedOrAlignedWithTheKey()
    {
        var result = Frontmatter.Parse("---\na:\n- x\n- y\nb:\n  - z\n---\nBody");

        Assert.Equal(["x", "y"], result.Values["a"].List);
        Assert.Equal(["z"], result.Values["b"].List);
    }

    [Fact]
    public void Parse_MalformedValue_IsRecordedOnTheValueAndOtherKeysAreRead()
    {
        var result = Frontmatter.Parse("---\nowner: \"not closed\npaths: src/**\n---\nBody");

        Assert.Null(result.Error);
        Assert.NotNull(result.Values["owner"].Problem);
        Assert.Null(result.Values["paths"].Problem);
        Assert.Equal("src/**", result.Values["paths"].Scalar);
    }

    [Theory]
    [InlineData("description: Fix #12 now", "Fix")]
    [InlineData("description: Fix#12 now", "Fix#12 now")]
    [InlineData("description: \"Fix #12\"", "Fix #12")]
    [InlineData("description: 'it''s' # comment", "it's")]
    [InlineData("description: # nothing", "")]
    public void Parse_CommentStartsAtAHashAfterWhitespaceOutsideQuotes(string line, string expected)
    {
        var result = Frontmatter.Parse($"---\n{line}\n---\nBody");

        Assert.Equal(expected, result.Values["description"].Scalar);
    }

    [Fact]
    public void Parse_FlowListKeepsAHashInsideQuotesAndDropsTheCommentAfterIt()
    {
        var result = Frontmatter.Parse("---\npaths: [\"a #b\", c] # note\n---\nBody");

        Assert.Equal(["a #b", "c"], result.Values["paths"].List);
    }

    [Theory]
    [InlineData("paths: \"a\" b")]
    [InlineData("paths: \"a")]
    [InlineData("paths: [\"a, b]")]
    [InlineData("paths: ['a'b, c]")]
    public void Parse_BadQuoting_IsAProblemOfTheValue(string line)
    {
        var result = Frontmatter.Parse($"---\n{line}\n---\nBody");

        Assert.Null(result.Error);
        Assert.NotNull(result.Values["paths"].Problem);
    }

    [Theory]
    [InlineData("a, b", new[] { "a", "b" })]
    [InlineData("src/**/*.{ts,tsx}, lib/**", new[] { "src/**/*.{ts,tsx}", "lib/**" })]
    [InlineData("a,,b,", new[] { "a", "", "b", "" })]
    public void ReadItems_SplitsAScalarOnCommasOutsideBraces(string scalar, string[] expected)
    {
        var items = Frontmatter.ReadItems(new FrontmatterValue(scalar, null), out var problem);

        Assert.Null(problem);
        Assert.Equal(expected, items);
    }

    [Fact]
    public void ReadItems_OneQuotedStringIsTheTextOfTheList()
    {
        var value = Frontmatter.Parse("---\npaths: \"src/**, lib/**\"\n---\nBody").Values["paths"];

        Assert.Equal(["src/**", "lib/**"], Frontmatter.ReadItems(value, out var problem));
        Assert.Null(problem);
    }

    [Fact]
    public void ReadItems_AQuotedItemInABareListIsAProblem()
    {
        var value = Frontmatter.Parse("---\npaths: a, \"b\"\n---\nBody").Values["paths"];

        Assert.Null(Frontmatter.ReadItems(value, out var problem));
        Assert.NotNull(problem);
    }

    [Fact]
    public void ReadItems_NestedValueIsAProblem()
    {
        var value = Frontmatter.Parse("---\npaths:\n  src: ui\n---\nBody").Values["paths"];

        Assert.Null(Frontmatter.ReadItems(value, out var problem));
        Assert.NotNull(problem);
    }

    [Fact]
    public void SplitTopLevel_QuoteInsideAnItem_DoesNotSwallowTheCommas()
    {
        Assert.Equal(["src/it's/**", "lib/**"], Frontmatter.SplitTopLevel("src/it's/**, lib/**"));
        Assert.Equal(["\"a, b\"", "c"], Frontmatter.SplitTopLevel("\"a, b\", c"));
    }
}
