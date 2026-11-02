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

    private void WriteRule(string fileName, string content)
    {
        File.WriteAllText(Path.Combine(_contextDirectory, "rules", fileName), content);
    }
}
