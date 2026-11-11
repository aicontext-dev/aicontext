using AIContext;
using Xunit;

namespace AIContext.Tests;

public sealed class RuleLoaderTests : IDisposable
{
    private readonly string _contextDirectory = Path.Combine(Path.GetTempPath(), $"aicontext-tests-{Guid.NewGuid():N}");

    public RuleLoaderTests()
    {
        Directory.CreateDirectory(Path.Combine(_contextDirectory, "rules"));
    }

    public void Dispose()
    {
        Directory.Delete(_contextDirectory, true);
    }

    [Fact]
    public void Load_WithoutRulesDirectory_ReturnsNothing()
    {
        Directory.Delete(Path.Combine(_contextDirectory, "rules"));

        var result = RuleLoader.Load(_contextDirectory);

        Assert.Empty(result.Rules);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Load_DerivesKindFromFrontmatter()
    {
        WriteRule("ui.md", "---\ndescription: UI rules\npaths: [\"src/ui/**\", \"**/*.tsx\"]\n---\nUse components.");
        WriteRule("api.md", "---\ndescription: API design\n---\nReturn JSON.");
        WriteRule("style.md", "Always format code.");

        var result = RuleLoader.Load(_contextDirectory);

        Assert.Empty(result.Errors);
        Assert.Equal(["api", "style", "ui"], result.Rules.Select(r => r.Name));
        Assert.Equal(RuleKind.OnDemand, result.Rules[0].Kind);
        Assert.Equal(RuleKind.Always, result.Rules[1].Kind);
        Assert.Equal(RuleKind.Scoped, result.Rules[2].Kind);
        Assert.Equal(["src/ui/**", "**/*.tsx"], result.Rules[2].Paths);
    }

    [Fact]
    public void Load_AcceptsCommaSeparatedPathsString()
    {
        WriteRule("ui.md", "---\npaths: src/**/*.{ts,tsx}, lib/**\n---\nBody");

        var rule = Assert.Single(RuleLoader.Load(_contextDirectory).Rules);

        Assert.Equal(["src/**/*.{ts,tsx}", "lib/**"], rule.Paths);
    }

    [Theory]
    [InlineData("Bad Name.md")]
    [InlineData("UPPER.md")]
    [InlineData("architecture.md")]
    public void Load_RejectsInvalidOrReservedNames(string fileName)
    {
        WriteRule(fileName, "Body");

        var result = RuleLoader.Load(_contextDirectory);

        Assert.Empty(result.Rules);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void Load_ReportsFrontmatterProblems()
    {
        WriteRule("broken.md", "---\ndescription: x\nBody");
        WriteRule("windows.md", "---\npaths: src\\ui\\**\n---\nBody");
        WriteRule("listdesc.md", "---\ndescription: [a, b]\n---\nBody");

        var result = RuleLoader.Load(_contextDirectory);

        Assert.Empty(result.Rules);
        Assert.Equal(3, result.Errors.Count);
    }

    [Fact]
    public void Load_WarnsAboutUnknownKeysEmptyRulesAndIgnoredFiles()
    {
        WriteRule("ok.md", "---\ndescription: Fine\nowner: me\n---\nBody");
        WriteRule("empty.md", "---\ndescription: Nothing\n---\n");
        WriteRule("notes.txt", "ignored");
        Directory.CreateDirectory(Path.Combine(_contextDirectory, "rules", "nested"));

        var result = RuleLoader.Load(_contextDirectory);

        Assert.Equal("ok", Assert.Single(result.Rules).Name);
        Assert.Empty(result.Errors);
        Assert.Contains(result.Warnings, w => w.Contains("unknown frontmatter key 'owner'", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, w => w.Contains("empty.md", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, w => w.Contains("notes.txt", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, w => w.Contains("nested", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("---\npaths: []\n---\nBody")]
    [InlineData("---\npaths:\n---\nBody")]
    [InlineData("---\npaths: \"\"\n---\nBody")]
    [InlineData("---\npaths: [\"\"]\n---\nBody")]
    [InlineData("---\npaths: ,\n---\nBody")]
    [InlineData("---\npaths: # to be decided\n---\nBody")]
    public void Load_PathsWithoutAPattern_IsAnErrorAndNeverAnAlwaysOnRule(string content)
    {
        WriteRule("ui.md", content);

        var result = RuleLoader.Load(_contextDirectory);

        Assert.Empty(result.Rules);
        var error = Assert.Single(result.Diagnostics, d => d.IsError);
        Assert.Equal(DiagnosticCodes.EmptyFrontmatterValue, error.Code);
        Assert.Equal(".aicontext/rules/ui.md", error.Location);
    }

    [Theory]
    [InlineData("---\ndescription:\n---\nBody")]
    [InlineData("---\ndescription: \"  \"\npaths: [\"src/**\"]\n---\nBody")]
    public void Load_EmptyDescription_IsAnErrorEvenNextToValidPaths(string content)
    {
        WriteRule("ui.md", content);

        var result = RuleLoader.Load(_contextDirectory);

        Assert.Empty(result.Rules);
        Assert.Equal(DiagnosticCodes.EmptyFrontmatterValue, Assert.Single(result.Diagnostics, d => d.IsError).Code);
    }

    [Theory]
    [InlineData("_team.md")]
    [InlineData("-team.md")]
    [InlineData("team_.md")]
    [InlineData("-.md")]
    [InlineData("2024-rules.md")]
    public void Load_AcceptsEveryNameTheSpecificationAllows(string fileName)
    {
        WriteRule(fileName, "Body");

        var result = RuleLoader.Load(_contextDirectory);

        Assert.Empty(result.Errors);
        Assert.Equal(fileName[..^3], Assert.Single(result.Rules).Name);
    }

    [Theory]
    [InlineData(".md")]
    [InlineData("a.b.md")]
    [InlineData("Team.md")]
    [InlineData("my rule.md")]
    public void Load_RejectsNamesOutsideTheAllowedCharacters(string fileName)
    {
        WriteRule(fileName, "Body");

        var result = RuleLoader.Load(_contextDirectory);

        Assert.Empty(result.Rules);
        Assert.Equal(DiagnosticCodes.InvalidRuleName, Assert.Single(result.Diagnostics, d => d.IsError).Code);
    }

    [Fact]
    public void Load_UnknownKeyWithANestedValue_WarnsAndKeepsTheRule()
    {
        WriteRule("ui.md", "---\nowner:\n  team: web\npaths: [\"src/**\"]\n---\nBody");

        var result = RuleLoader.Load(_contextDirectory);

        Assert.Empty(result.Errors);
        Assert.Equal(["src/**"], Assert.Single(result.Rules).Paths);
        Assert.Equal(DiagnosticCodes.UnknownFrontmatterKey, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Load_RepeatedKey_IsAFrontmatterErrorAndTheRuleIsNotLoaded()
    {
        WriteRule("ui.md", "---\npaths: src/**\npaths: lib/**\n---\nBody");

        var result = RuleLoader.Load(_contextDirectory);

        Assert.Empty(result.Rules);
        Assert.Equal(DiagnosticCodes.InvalidFrontmatter, Assert.Single(result.Diagnostics, d => d.IsError).Code);
    }

    [Theory]
    [InlineData("---\npaths:\n  src: ui\n---\nBody", DiagnosticCodes.InvalidFrontmatter)]
    [InlineData("---\npaths: \"src/**\" lib/**\n---\nBody", DiagnosticCodes.InvalidFrontmatter)]
    [InlineData("---\npaths: src/**, \"lib/**\"\n---\nBody", DiagnosticCodes.InvalidFrontmatter)]
    [InlineData("---\ndescription: >\n  long\n---\nBody", DiagnosticCodes.InvalidRuleDescription)]
    [InlineData("---\ndescription: \"open\n---\nBody", DiagnosticCodes.InvalidFrontmatter)]
    public void Load_UnusableValueOfAKnownKey_IsAnErrorAndTheRuleIsNotLoaded(string content, string expectedCode)
    {
        WriteRule("ui.md", content);

        var result = RuleLoader.Load(_contextDirectory);

        Assert.Empty(result.Rules);
        Assert.Equal(expectedCode, Assert.Single(result.Diagnostics, d => d.IsError).Code);
    }

    [Theory]
    [InlineData("[\"src/[ab]/**\"]")]
    [InlineData("[\"src/?.ts\"]")]
    [InlineData("[\"!src/**\"]")]
    [InlineData("[\"src/(group)/**\"]")]
    [InlineData("/src/**")]
    [InlineData("[\"./src/**\"]")]
    [InlineData("src/ui/")]
    [InlineData("\"**\"")]
    [InlineData("[\"src/{a}/**\"]")]
    [InlineData("[\"src/{a,{b,c}}/**\"]")]
    public void Load_PatternOutsideThePortableSubset_IsAnErrorAndTheRuleIsNotLoaded(string paths)
    {
        WriteRule("ui.md", $"---\npaths: {paths}\n---\nBody");

        var result = RuleLoader.Load(_contextDirectory);

        Assert.Empty(result.Rules);
        var error = Assert.Single(result.Diagnostics, d => d.IsError);
        Assert.Equal(DiagnosticCodes.InvalidRulePathPattern, error.Code);
        Assert.Equal(".aicontext/rules/ui.md", error.Location);
        Assert.Contains("SPEC 4.4.2", error.Message);
    }

    [Fact]
    public void Load_OneBadPatternNextToGoodOnes_ReportsOnlyTheBadOneAndTheRuleIsNotLoaded()
    {
        WriteRule("ui.md", "---\npaths: [\"src/**\", \"src/[ab]/**\", \"lib/**\"]\n---\nBody");

        var result = RuleLoader.Load(_contextDirectory);

        Assert.Empty(result.Rules);
        var error = Assert.Single(result.Diagnostics, d => d.IsError);
        Assert.Contains("'src/[ab]/**'", error.Message);
    }

    [Theory]
    [InlineData("[\"src/a,b/**\"]", new[] { "src/a,b/**" })]
    [InlineData("[Makefile, \"*.config.js\"]", new[] { "Makefile", "*.config.js" })]
    [InlineData("src/**/*.{ts,tsx}", new[] { "src/**/*.{ts,tsx}" })]
    [InlineData("[\"{src,lib}/**\"]", new[] { "{src,lib}/**" })]
    public void Load_PatternInsideThePortableSubset_IsKeptAsWritten(string paths, string[] expected)
    {
        WriteRule("ui.md", $"---\npaths: {paths}\n---\nBody");

        var result = RuleLoader.Load(_contextDirectory);

        Assert.Empty(result.Errors);
        Assert.Equal(expected, Assert.Single(result.Rules).Paths);
    }

    [Fact]
    public void Load_BackslashInAPattern_StaysAnAIC2005ErrorOnly()
    {
        WriteRule("ui.md", "---\npaths: [\"src\\\\ui\\\\**\"]\n---\nBody");

        var result = RuleLoader.Load(_contextDirectory);

        Assert.Equal(DiagnosticCodes.InvalidRulePathSeparator, Assert.Single(result.Diagnostics, d => d.IsError).Code);
    }

    [Fact]
    public void Load_ValueOfAnUnknownKeyIsNotInterpreted()
    {
        WriteRule("ui.md", "---\nowner: \"not closed\n---\nBody");

        var result = RuleLoader.Load(_contextDirectory);

        Assert.Empty(result.Errors);
        Assert.Single(result.Rules);
    }

    [Fact]
    public void Load_CommentsAndQuotingGiveTheSamePatternsAsThePlainForms()
    {
        WriteRule("a.md", "---\npaths: src/**, lib/** # source\n---\nBody");
        WriteRule("b.md", "---\npaths: \"src/**, lib/**\"\n---\nBody");
        WriteRule("c.md", "---\npaths: [\"src/**\", lib/**,]\n---\nBody");
        WriteRule("d.md", "---\npaths:\n  - src/** # source\n  - \"lib/**\"\n---\nBody");

        var result = RuleLoader.Load(_contextDirectory);

        Assert.Empty(result.Errors);
        Assert.All(result.Rules, rule => Assert.Equal(["src/**", "lib/**"], rule.Paths));
        Assert.Equal(4, result.Rules.Count);
    }

    private void WriteRule(string fileName, string content)
    {
        File.WriteAllText(Path.Combine(_contextDirectory, "rules", fileName), content);
    }
}
