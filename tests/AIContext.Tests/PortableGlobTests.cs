using AIContext;
using Xunit;

namespace AIContext.Tests;

// The meaning of a path pattern (SPEC 4.4.2). Which files each pattern matches is defined by the conformance suite
// (ConformanceTests); these tests cover how an item is read: brace expansion, its limits and the refusals.
public sealed class PortableGlobTests
{
    [Theory]
    [InlineData("src/**/*.{ts,tsx}", new[] { "src/**/*.ts", "src/**/*.tsx" })]
    [InlineData("{src,lib}/{a,b}/**", new[] { "src/a/**", "src/b/**", "lib/a/**", "lib/b/**" })]
    [InlineData("{a,a}/x", new[] { "a/x" })]
    [InlineData("{**/a,b}", new[] { "**/a", "b" })]
    [InlineData("src/**", new[] { "src/**" })]
    [InlineData("src/a,b/**", new[] { "src/a,b/**" })]
    public void Parse_ReadsAnItemAsTheSeparatePatternsItStandsFor(string item, string[] expected)
    {
        var parse = PortableGlob.Parse(item);

        Assert.True(parse.IsValid);
        Assert.Equal(expected, parse.Patterns);
    }

    [Fact]
    public void Parse_BraceGroupsThatExpandToExactlyTheLimit_AreAccepted()
    {
        var item = string.Join('/', Enumerable.Repeat("{a,b}", 6));

        var parse = PortableGlob.Parse(item);

        Assert.True(parse.IsValid);
        Assert.Equal(PortableGlob.MaxExpansions, parse.Patterns.Count);
    }

    [Fact]
    public void Parse_OneExpansionMoreThanTheLimit_IsRefused()
    {
        var item = string.Join('/', Enumerable.Repeat("{a,b}", 7));

        var parse = PortableGlob.Parse(item);

        Assert.False(parse.IsValid);
        Assert.Contains("64", parse.Problem);
        Assert.Empty(parse.Patterns);
    }

    [Theory]
    [InlineData("", "empty")]
    [InlineData("   ", "empty")]
    [InlineData("**", "'**' alone")]
    [InlineData("/src/**", "start with '/'")]
    [InlineData("src/", "end with '/'")]
    [InlineData("src//a", "empty path segment")]
    [InlineData("./src/**", "'.' or '..'")]
    [InlineData("src/../a", "'.' or '..'")]
    [InlineData("src\\a", "only path separator")]
    [InlineData("a**b", "whole path segment")]
    [InlineData("src/[ab]", "'[' is reserved")]
    [InlineData("src/a]", "']' is reserved")]
    [InlineData("src/?", "'?' is reserved")]
    [InlineData("!src", "'!' is reserved")]
    [InlineData("(a)/x", "'(' is reserved")]
    [InlineData("{a}/x", "two alternatives")]
    [InlineData("{a,b/x", "not closed")]
    [InlineData("a,b}/x", "no matching")]
    [InlineData("{a,{b,c}}/x", "nested")]
    [InlineData("{a,}/x", "empty or has spaces")]
    [InlineData("{a, b}/x", "empty or has spaces")]
    public void Parse_AnItemOutsideThePortableSubset_SaysWhy(string item, string expectedProblem)
    {
        var parse = PortableGlob.Parse(item);

        Assert.False(parse.IsValid);
        Assert.Contains(expectedProblem, parse.Problem);
    }

    // The problem is shown next to the item by callers that decide how to word text from a project file, so it must
    // never quote the item itself (it could be a secret).
    [Theory]
    [InlineData("{0}/[x]")]
    [InlineData("{0}/{{a")]
    [InlineData("/{0}")]
    [InlineData("{0}/")]
    [InlineData("{0}//x")]
    [InlineData("{0}/../x")]
    [InlineData("{0}\\x")]
    [InlineData("{0}/a**b")]
    public void Parse_TheProblemNeverRepeatsTheItem(string template)
    {
        var item = string.Format(System.Globalization.CultureInfo.InvariantCulture, template, "ghp_SYNTH0123456789ABCDEFGHIJKLMNOPQRSTUV");

        var parse = PortableGlob.Parse(item);

        Assert.False(parse.IsValid);
        Assert.DoesNotContain("SYNTH0123456789", parse.Problem);
    }

    [Fact]
    public void IsMatch_AnItemOutsideTheSubset_MatchesNothing()
    {
        Assert.False(PortableGlob.IsMatch("src/[ab]/**", "src/a/x.ts"));
        Assert.False(PortableGlob.IsMatch("src/[ab]/**", "src/[ab]/x.ts"));
    }

    [Fact]
    public void IsMatch_AnEmptyPath_MatchesNothing()
    {
        Assert.False(PortableGlob.IsMatch("**/*", string.Empty));
    }

    [Theory]
    [InlineData("a*b*c", "abc", true)]
    [InlineData("a*b*c", "aXXbYYc", true)]
    [InlineData("a*b*c", "aXXbYY", false)]
    [InlineData("*a", "aaa", true)]
    [InlineData("a*", "a", true)]
    [InlineData("*.ts", ".ts", true)]
    [InlineData("*.ts", "x.tsx", false)]
    public void IsMatch_StarInsideASegment_MatchesAnyRunOfCharacters(string pattern, string path, bool expected)
    {
        Assert.Equal(expected, PortableGlob.IsMatch(pattern, path));
    }

    [Theory]
    [InlineData("src/**", "src", false)]
    [InlineData("src/**", "src/a", true)]
    [InlineData("src/**/a", "src/a", true)]
    [InlineData("src/**/a", "src/x/y/a", true)]
    [InlineData("**/a/**", "a/b", true)]
    [InlineData("**/a/**", "x/a/b/c", true)]
    [InlineData("**/a/**", "a", false)]
    [InlineData("**/**", "a", true)]
    public void IsMatch_DoubleStarCoversWholeSegments(string pattern, string path, bool expected)
    {
        Assert.Equal(expected, PortableGlob.IsMatch(pattern, path));
    }

    [Fact]
    public void IsMatch_LetterCaseAndLeadingDotsAreOrdinary()
    {
        Assert.False(PortableGlob.IsMatch("src/foo/**", "src/Foo/x.ts"));
        Assert.True(PortableGlob.IsMatch("*", ".env"));
        Assert.True(PortableGlob.IsMatch("**/*", ".github/workflows/ci.yml"));
    }
}
