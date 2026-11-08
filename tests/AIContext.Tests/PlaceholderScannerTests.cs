using AIContext;
using Xunit;

namespace AIContext.Tests;

public sealed class PlaceholderScannerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"aicontext-tests-{Guid.NewGuid():N}");

    public PlaceholderScannerTests()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "rules"));
    }

    public void Dispose()
    {
        Directory.Delete(_directory, true);
    }

    [Theory]
    [InlineData("TODO: describe the main components.")]
    [InlineData("- TODO: a rule that must never be violated.")]
    [InlineData("### D1. TODO: title of the first decision")]
    [InlineData("**Context:** TODO")]
    [InlineData("**Tech stack:** TODO  ")]
    [InlineData("Tech stack: TODO")]
    [InlineData("TODO")]
    [InlineData("- TODO")]
    [InlineData("Brief project summary.")]
    [InlineData("description: TODO: write one")]
    public void Scan_FlagsPlaceholderLines(string line)
    {
        var finding = Assert.Single(PlaceholderScanner.Scan("README.md", $"Intro.\n{line}\nOutro.\n"));

        Assert.Equal("README.md", finding.File);
        Assert.Equal(2, finding.Line);
    }

    [Theory]
    [InlineData("Use TODO comments only with an issue link.")]
    [InlineData("Mark unfinished work with TODO")]
    [InlineData("Policy: TODO comments must link an issue.")]
    [InlineData("The todo list is in the tracker.")]
    [InlineData("Write `TODO: text` for follow-ups.")]
    [InlineData("A pragmatic TODOs approach.")]
    [InlineData("Brief project summary of the billing service.")]
    public void Scan_IgnoresOrdinaryTextThatMentionsTodo(string line)
    {
        Assert.Empty(PlaceholderScanner.Scan("README.md", line));
    }

    [Fact]
    public void Scan_IgnoresFencedCodeBlocks()
    {
        var content = "Example:\n\n```text\nTODO: fill in\n**Tech stack:** TODO\n```\n\nTODO: real placeholder\n";

        var finding = Assert.Single(PlaceholderScanner.Scan("README.md", content));

        Assert.Equal(8, finding.Line);
    }

    [Fact]
    public void Scan_RespectsTheAllowMarker()
    {
        Assert.Empty(PlaceholderScanner.Scan("README.md", $"TODO: documented on purpose <!-- {PlaceholderScanner.AllowMarker} -->"));
    }

    [Fact]
    public void Scan_HandlesCrLfLineEndings()
    {
        var finding = Assert.Single(PlaceholderScanner.Scan("README.md", "Intro.\r\nTODO: fill in\r\n"));

        Assert.Equal(2, finding.Line);
    }

    [Fact]
    public void ScanDirectory_ScansSectionFilesAndRulesWithRelativeNames()
    {
        File.WriteAllText(Path.Combine(_directory, "README.md"), "# Project\n\nTODO: summary\n");
        File.WriteAllText(Path.Combine(_directory, "architecture.md"), "Real content.");
        File.WriteAllText(Path.Combine(_directory, "rules", "ui.md"), "---\ndescription: Real\n---\nTODO: write the rule\n");
        File.WriteAllText(Path.Combine(_directory, "notes.md"), "TODO: not part of the context");
        File.WriteAllText(Path.Combine(_directory, "rules", "notes.txt"), "TODO: ignored");

        var findings = PlaceholderScanner.ScanDirectory(_directory);

        Assert.Equal(["README.md:3", "rules/ui.md:4"], findings.Select(f => $"{f.File}:{f.Line}"));
    }

    [Fact]
    public void ScanDirectory_MissingFilesAndDirectories_AreIgnored()
    {
        Assert.Empty(PlaceholderScanner.ScanDirectory(Path.Combine(_directory, "nowhere")));
        Assert.Empty(PlaceholderScanner.ScanDirectory(_directory));
    }

    [Fact]
    public void ScanDirectory_UnreadableFile_DoesNotThrow()
    {
        var path = Path.Combine(_directory, "README.md");
        File.WriteAllText(path, "TODO: summary");

        using var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Assert.Empty(PlaceholderScanner.ScanDirectory(_directory));
    }
}
