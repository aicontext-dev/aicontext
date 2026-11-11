using System.Reflection;
using System.Text.RegularExpressions;
using AIContext;
using Xunit;

namespace AIContext.Tests;

// Diagnostic codes are a contract with tools that match on them: every code is well formed, unique, and keeps
// meaning the same problem.
public sealed class DiagnosticCodeTests : IDisposable
{
    private const string SchemaUrl = ContextSchema.CanonicalUrl;

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"aicontext-tests-{Guid.NewGuid():N}");

    public DiagnosticCodeTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".aicontext", "rules"));
        WriteFile("README.md", "# Project: sample\n\nSummary.");
        WriteContext("""{ "$schema": "https://aicontext.dev/schema/v1/context.schema.json", "specVersion": "0.1.0", "project": { "name": "sample" } }""");
        foreach (var name in ContextValidator.RecommendedFiles)
        {
            WriteFile(name, "Content.");
        }
    }

    public void Dispose()
    {
        Directory.Delete(_root, true);
    }

    private void WriteFile(string name, string content) =>
        File.WriteAllText(Path.Combine(_root, ".aicontext", name), content);

    private void WriteContext(string json) => WriteFile("context.json", json);

    private void WriteRule(string fileName, string content) =>
        File.WriteAllText(Path.Combine(_root, ".aicontext", "rules", fileName), content);

    private IReadOnlyList<Diagnostic> Validate() =>
        ContextValidator.ValidateDirectory(_root, SchemaUrl).Diagnostics;

    private static Diagnostic Single(IEnumerable<Diagnostic> diagnostics, string code)
    {
        var found = diagnostics.Where(d => d.Code == code).ToList();
        Assert.True(found.Count == 1, $"Expected exactly one {code}, found {found.Count}.");
        return found[0];
    }

    private static string[] AllCodes() =>
        typeof(DiagnosticCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToArray();

    [Fact]
    public void DiagnosticCodes_AreWellFormedAndUnique()
    {
        var codes = AllCodes();

        Assert.NotEmpty(codes);
        Assert.All(codes, code => Assert.Matches(new Regex("^AIC[1-4][0-9]{3}$"), code));
        Assert.Equal(codes.Length, codes.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Validate_CleanProject_HasNoDiagnostics()
    {
        Assert.Empty(Validate());
    }

    [Fact]
    public void Validate_ErrorsAndWarnings_AreTheMessagesOfTheDiagnostics()
    {
        WriteContext("""{ "specVersion": "999.0.0", "project": { "name": "sample" } }""");
        File.Delete(Path.Combine(_root, ".aicontext", "decisions.md"));

        var result = ContextValidator.ValidateDirectory(_root, SchemaUrl);

        Assert.Equal(result.Diagnostics.Where(d => d.IsError).Select(d => d.Message), result.Errors);
        Assert.Equal(result.Diagnostics.Where(d => !d.IsError).Select(d => d.Message), result.Warnings);
        Assert.NotEmpty(result.Errors);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void Validate_MissingFiles_AreReportedWithTheirLocation()
    {
        File.Delete(Path.Combine(_root, ".aicontext", "README.md"));
        File.Delete(Path.Combine(_root, ".aicontext", "context.json"));

        var diagnostics = Validate().Where(d => d.Code == DiagnosticCodes.MissingRequiredFile).ToList();

        Assert.Equal([".aicontext/README.md", ".aicontext/context.json"], diagnostics.Select(d => d.Location));
        Assert.All(diagnostics, d => Assert.True(d.IsError));
    }

    [Fact]
    public void Validate_EmptyReadme_ReportsAIC1002()
    {
        WriteFile("README.md", "  \n");

        var diagnostic = Single(Validate(), DiagnosticCodes.EmptyReadme);

        Assert.True(diagnostic.IsError);
        Assert.Equal(".aicontext/README.md", diagnostic.Location);
    }

    [Fact]
    public void Validate_InvalidJson_ReportsAIC1003()
    {
        WriteContext("{ nope");

        Assert.Equal(".aicontext/context.json", Single(Validate(), DiagnosticCodes.InvalidJson).Location);
    }

    [Fact]
    public void Validate_SchemaViolation_ReportsAIC1004WithJsonPointer()
    {
        WriteContext("""{ "specVersion": "999.0.0", "project": { "name": "sample" } }""");

        var diagnostic = Single(Validate(), DiagnosticCodes.SchemaViolation);

        Assert.True(diagnostic.IsError);
        Assert.Equal(".aicontext/context.json#/specVersion", diagnostic.Location);
    }

    [Fact]
    public void Validate_WhitespaceProjectName_ReportsAIC1005()
    {
        WriteContext("""{ "$schema": "https://aicontext.dev/schema/v1/context.schema.json", "specVersion": "0.1.0", "project": { "name": "  " } }""");

        Assert.Equal(".aicontext/context.json#/project/name", Single(Validate(), DiagnosticCodes.ProjectNameWhitespace).Location);
    }

    [Fact]
    public void Validate_WhitespaceProjectName_IsNotAlsoReportedAsASchemaViolation()
    {
        WriteContext("""{ "$schema": "https://aicontext.dev/schema/v1/context.schema.json", "specVersion": "0.1.0", "project": { "name": "\t \n" } }""");

        var diagnostics = Validate();

        Assert.Single(diagnostics, d => d.IsError);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCodes.SchemaViolation);
    }

    [Fact]
    public void Validate_EmptyProjectName_ReportsOnlyTheSchemaViolation()
    {
        WriteContext("""{ "$schema": "https://aicontext.dev/schema/v1/context.schema.json", "specVersion": "0.1.0", "project": { "name": "" } }""");

        var diagnostics = Validate();

        Assert.Equal(".aicontext/context.json#/project/name", Single(diagnostics, DiagnosticCodes.SchemaViolation).Location);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCodes.ProjectNameWhitespace);
    }

    [Fact]
    public void Validate_DefaultTargetNotInTargets_ReportsAIC1006()
    {
        WriteContext("""{ "$schema": "https://aicontext.dev/schema/v1/context.schema.json", "specVersion": "0.1.0", "project": { "name": "sample" }, "exports": { "targets": ["agents"], "defaultTarget": "claude" } }""");

        Assert.True(Single(Validate(), DiagnosticCodes.DefaultTargetNotInTargets).IsError);
    }

    [Fact]
    public void Validate_SchemaPropertyProblems_AreWarnings()
    {
        WriteContext("""{ "specVersion": "0.1.0", "project": { "name": "sample" } }""");
        Assert.False(Single(Validate(), DiagnosticCodes.MissingSchemaProperty).IsError);

        WriteContext("""{ "$schema": "https://example.com/other.json", "specVersion": "0.1.0", "project": { "name": "sample" } }""");
        Assert.False(Single(Validate(), DiagnosticCodes.SchemaUrlMismatch).IsError);
    }

    [Fact]
    public void Validate_ExportConfigurationHints_AreWarnings()
    {
        WriteContext("""{ "$schema": "https://aicontext.dev/schema/v1/context.schema.json", "specVersion": "0.1.0", "project": { "name": "sample" }, "exports": { "targets": ["windsurf", "devin"], "nestedAgents": true } }""");

        var diagnostics = Validate();

        Assert.False(Single(diagnostics, DiagnosticCodes.WindsurfDevinOverlap).IsError);
        Assert.False(Single(diagnostics, DiagnosticCodes.NestedAgentsWithoutSplit).IsError);
    }

    [Fact]
    public void Validate_RecommendedFiles_AreWarningsWithLocation()
    {
        File.Delete(Path.Combine(_root, ".aicontext", "architecture.md"));
        WriteFile("decisions.md", " ");

        var diagnostics = Validate();

        var missing = Single(diagnostics, DiagnosticCodes.RecommendedFileMissing);
        var empty = Single(diagnostics, DiagnosticCodes.RecommendedFileEmpty);
        Assert.Equal(".aicontext/architecture.md", missing.Location);
        Assert.Equal(".aicontext/decisions.md", empty.Location);
        Assert.False(missing.IsError);
        Assert.False(empty.IsError);
    }

    [Theory]
    [InlineData("Bad Name.md", "Body.", DiagnosticCodes.InvalidRuleName)]
    [InlineData("project.md", "Body.", DiagnosticCodes.ReservedRuleName)]
    [InlineData("ui.md", "---\ndescription: x\nBody.", DiagnosticCodes.InvalidFrontmatter)]
    [InlineData("ui.md", "---\ndescription: [a, b]\n---\nBody.", DiagnosticCodes.InvalidRuleDescription)]
    [InlineData("ui.md", "---\npaths: [src\\ui/**]\n---\nBody.", DiagnosticCodes.InvalidRulePathSeparator)]
    [InlineData("ui.md", "---\npaths: [\"src/[ab]/**\"]\n---\nBody.", DiagnosticCodes.InvalidRulePathPattern)]
    [InlineData("ui.md", "---\npaths: []\n---\nBody.", DiagnosticCodes.EmptyFrontmatterValue)]
    [InlineData("ui.md", "---\ndescription:\n---\nBody.", DiagnosticCodes.EmptyFrontmatterValue)]
    public void Validate_InvalidRule_ReportsRuleErrorWithLocation(string fileName, string content, string expectedCode)
    {
        WriteRule(fileName, content);

        var diagnostic = Validate().First(d => d.Code == expectedCode);

        Assert.True(diagnostic.IsError);
        Assert.Equal($".aicontext/rules/{fileName}", diagnostic.Location);
    }

    [Fact]
    public void Validate_IgnoredAndEmptyRuleContent_AreWarnings()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".aicontext", "rules", "nested"));
        WriteRule("notes.txt", "x");
        WriteRule("empty.md", "---\ndescription: x\n---\n");
        WriteRule("ui.md", "---\nowner: me\n---\nBody.");

        var diagnostics = Validate();

        Assert.False(Single(diagnostics, DiagnosticCodes.IgnoredRuleDirectory).IsError);
        Assert.False(Single(diagnostics, DiagnosticCodes.IgnoredRuleFile).IsError);
        Assert.False(Single(diagnostics, DiagnosticCodes.EmptyRule).IsError);
        Assert.False(Single(diagnostics, DiagnosticCodes.UnknownFrontmatterKey).IsError);
    }

    [Fact]
    public void Validate_PossibleSecret_ReportsAIC3001WithFileAndLine()
    {
        WriteFile("glossary.md", "Terms.\npassword = hunter2\n");

        var diagnostic = Single(Validate(), DiagnosticCodes.PossibleSecret);

        Assert.False(diagnostic.IsError);
        Assert.Equal(".aicontext/glossary.md:2", diagnostic.Location);
        Assert.DoesNotContain("hunter2", diagnostic.Message);
    }

    [Fact]
    public void Validate_PlaceholderText_ReportsAIC1107WithFileAndLine()
    {
        WriteFile("constraints.md", "Hard rules.\n\n- TODO: a rule that must never be violated.\n");
        WriteRule("ui.md", "---\ndescription: UI\n---\nTODO: write the rule\n");

        var diagnostics = Validate().Where(d => d.Code == DiagnosticCodes.PlaceholderContent).ToList();

        Assert.Equal([".aicontext/constraints.md:3", ".aicontext/rules/ui.md:4"], diagnostics.Select(d => d.Location));
        Assert.All(diagnostics, d => Assert.False(d.IsError));
    }

    [Fact]
    public void Validate_UnreadableFile_ReportsAIC3002()
    {
        WriteFile("glossary.md", "Terms.");

        using var locked = new FileStream(Path.Combine(_root, ".aicontext", "glossary.md"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var diagnostic = Single(Validate(), DiagnosticCodes.SecretScanIncomplete);

        Assert.Equal(".aicontext/glossary.md", diagnostic.Location);
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("context.json")]
    [InlineData("architecture.md")]
    [InlineData("constraints.md")]
    public void Validate_LockedContextFile_ReportsAIC1007InsteadOfThrowing(string fileName)
    {
        using var locked = TestFiles.Lock(Path.Combine(_root, ".aicontext", fileName));

        var diagnostic = Single(Validate(), DiagnosticCodes.UnreadableContextFile);

        Assert.True(diagnostic.IsError);
        Assert.Equal($".aicontext/{fileName}", diagnostic.Location);
        Assert.Contains("could not be read", diagnostic.Message);
    }

    [Fact]
    public void Validate_LockedRuleFile_ReportsAIC2006InsteadOfThrowing()
    {
        WriteRule("ui.md", "---\ndescription: UI\n---\nBody.");

        using var locked = TestFiles.Lock(Path.Combine(_root, ".aicontext", "rules", "ui.md"));
        var diagnostic = Single(Validate(), DiagnosticCodes.UnreadableRuleFile);

        Assert.True(diagnostic.IsError);
        Assert.Equal(".aicontext/rules/ui.md", diagnostic.Location);
    }

    [Fact]
    public void Load_LockedReadme_ReturnsTheDiagnosticAndNoContext()
    {
        using var locked = TestFiles.Lock(Path.Combine(_root, ".aicontext", "README.md"));

        var load = ContextLoader.Load(_root);

        Assert.Null(load.Loaded);
        Assert.Contains(load.Diagnostics, d => d.IsError && d.Code == DiagnosticCodes.UnreadableContextFile);
    }
}
