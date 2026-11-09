using AIContext;
using Xunit;

namespace AIContext.Tests;

public sealed class ProjectInitializerTests : IDisposable
{
    private const string SchemaUrl = "https://aicontext.dev/schema/v1/context.schema.json";
    private const string SpecVersion = "0.1.0";

    private readonly string _basePath = Path.Combine(Path.GetTempPath(), "aicontext-tests-" + Guid.NewGuid().ToString("N"));

    public ProjectInitializerTests()
    {
        Directory.CreateDirectory(_basePath);
    }

    public void Dispose()
    {
        Directory.Delete(_basePath, recursive: true);
    }

    private InitResult Init(InitProfile profile, bool force = false) =>
        ProjectInitializer.Initialize(_basePath, "demo", profile, SchemaUrl, SpecVersion, force);

    [Theory]
    [InlineData(null, InitProfile.Core, true)]
    [InlineData("", InitProfile.Core, true)]
    [InlineData("core", InitProfile.Core, true)]
    [InlineData("Extended", InitProfile.Extended, true)]
    [InlineData("full", InitProfile.Core, false)]
    public void TryParseProfile_Value_ReturnsExpectedResult(string? value, InitProfile expected, bool parsed)
    {
        Assert.Equal(parsed, ProjectInitializer.TryParseProfile(value, out var profile));
        Assert.Equal(expected, profile);
    }

    [Fact]
    public void Initialize_CoreProfile_CreatesOnlyCoreFiles()
    {
        var result = Init(InitProfile.Core);

        Assert.Empty(result.Conflicts);
        Assert.Equal(2, result.Created.Count);
        Assert.True(File.Exists(Path.Combine(_basePath, ".aicontext", "README.md")));
        Assert.True(File.Exists(Path.Combine(_basePath, ".aicontext", "context.json")));
        Assert.False(File.Exists(Path.Combine(_basePath, ".aicontext", "architecture.md")));
    }

    [Fact]
    public void Initialize_CoreProfile_ProducesCoreProfileWithRecommendedWarnings()
    {
        Init(InitProfile.Core);

        var validation = ContextValidator.ValidateDirectory(_basePath, SchemaUrl);

        Assert.Empty(validation.Errors);
        Assert.False(validation.ExtendedProfile);
    }

    [Fact]
    public void Initialize_ExtendedProfile_IsExtendedProfileWithOnlyPlaceholderWarnings()
    {
        var result = Init(InitProfile.Extended);

        Assert.Equal(5, result.Created.Count);
        var validation = ContextValidator.ValidateDirectory(_basePath, SchemaUrl);

        Assert.Empty(validation.Errors);
        Assert.True(validation.ExtendedProfile);
        Assert.NotEmpty(validation.Diagnostics);
        Assert.All(validation.Diagnostics, d => Assert.Equal(DiagnosticCodes.PlaceholderContent, d.Code));
    }

    [Fact]
    public void Initialize_ExtendedProfile_PassesStrictValidationOnceThePlaceholdersAreReplaced()
    {
        Init(InitProfile.Extended);
        var contextDirectory = Path.Combine(_basePath, ".aicontext");
        File.WriteAllText(Path.Combine(contextDirectory, "README.md"), "# Project: demo\n\nA billing service.\n\n**Tech stack:** .NET 10\n");
        File.WriteAllText(Path.Combine(contextDirectory, "architecture.md"), "Two layers: API and domain.");
        File.WriteAllText(Path.Combine(contextDirectory, "decisions.md"), "### D1. Use PostgreSQL\n**Decision:** PostgreSQL.");
        File.WriteAllText(Path.Combine(contextDirectory, "constraints.md"), "- Never log customer data.");

        var validation = ContextValidator.ValidateDirectory(_basePath, SchemaUrl);

        Assert.Empty(validation.Errors);
        Assert.Empty(validation.Warnings);
        Assert.True(validation.ExtendedProfile);
    }

    [Fact]
    public void Initialize_ExistingFileWithoutForce_ReportsConflictsAndWritesNothing()
    {
        var contextDirectory = Path.Combine(_basePath, ".aicontext");
        Directory.CreateDirectory(contextDirectory);
        var readmePath = Path.Combine(contextDirectory, "README.md");
        File.WriteAllText(readmePath, "custom");

        var result = Init(InitProfile.Extended);

        Assert.Empty(result.Created);
        Assert.Equal([readmePath], result.Conflicts);
        Assert.Equal("custom", File.ReadAllText(readmePath));
        Assert.False(File.Exists(Path.Combine(contextDirectory, "context.json")));
    }

    [Fact]
    public void FindExistingInstructionFiles_NothingThere_ReturnsEmpty()
    {
        Assert.Empty(ProjectInitializer.FindExistingInstructionFiles(_basePath));
    }

    [Fact]
    public void FindExistingInstructionFiles_HandWrittenFiles_AreListedByTheirTargetPath()
    {
        File.WriteAllText(Path.Combine(_basePath, "AGENTS.md"), "# My rules");
        File.WriteAllText(Path.Combine(_basePath, "CLAUDE.md"), "# Claude rules");
        Directory.CreateDirectory(Path.Combine(_basePath, ".github"));
        File.WriteAllText(Path.Combine(_basePath, ".github", "copilot-instructions.md"), "Be concise.");

        var found = ProjectInitializer.FindExistingInstructionFiles(_basePath);

        Assert.Equal(["AGENTS.md", ".github/copilot-instructions.md", "CLAUDE.md"], found);
    }

    [Fact]
    public void FindExistingInstructionFiles_FilesGeneratedByAicontext_AreNotListed()
    {
        File.WriteAllText(Path.Combine(_basePath, "CONVENTIONS.md"), $"<!-- {ContextExporter.GeneratedMarker} --target aider -->\n# Conventions\n");
        File.WriteAllText(Path.Combine(_basePath, "AGENTS.md"), "# Hand written");

        Assert.Equal(["AGENTS.md"], ProjectInitializer.FindExistingInstructionFiles(_basePath));
    }

    [Fact]
    public void FindExistingInstructionFiles_HandWrittenRuleFiles_AreListedAfterTheMainFiles()
    {
        Directory.CreateDirectory(Path.Combine(_basePath, ".github", "instructions", "backend"));
        File.WriteAllText(Path.Combine(_basePath, ".github", "instructions", "backend", "api.instructions.md"), "Rule.");
        Directory.CreateDirectory(Path.Combine(_basePath, ".cursor", "rules"));
        File.WriteAllText(Path.Combine(_basePath, ".cursor", "rules", "style.mdc"), "Rule.");
        Directory.CreateDirectory(Path.Combine(_basePath, ".claude", "rules"));
        File.WriteAllText(Path.Combine(_basePath, ".claude", "rules", "tests.md"), "Rule.");
        File.WriteAllText(Path.Combine(_basePath, "AGENTS.md"), "# Hand written");

        var found = ProjectInitializer.FindExistingInstructionFiles(_basePath);

        Assert.Equal(
            ["AGENTS.md", ".github/instructions/backend/api.instructions.md", ".claude/rules/tests.md", ".cursor/rules/style.mdc"],
            found);
    }

    [Fact]
    public void FindExistingInstructionFiles_GeneratedRuleFilesAndOtherExtensions_AreNotListed()
    {
        Directory.CreateDirectory(Path.Combine(_basePath, ".cursor", "rules"));
        File.WriteAllText(Path.Combine(_basePath, ".cursor", "rules", "aicontext-api.mdc"), $"<!-- {ContextExporter.GeneratedMarker} --target cursor -->\nold\n");
        File.WriteAllText(Path.Combine(_basePath, ".cursor", "rules", "notes.txt"), "Not a rule.");
        File.WriteAllText(Path.Combine(_basePath, ".cursor", "rules", "project.mdc"), $"<!-- {ContextExporter.GeneratedMarker} --target cursor -->\nmain\n");

        Assert.Empty(ProjectInitializer.FindExistingInstructionFiles(_basePath));
    }

    [Fact]
    public void FindExistingInstructionFiles_NestedAgentsFile_IsListedUnlessItBelongsToAnotherProject()
    {
        Directory.CreateDirectory(Path.Combine(_basePath, "services", "billing"));
        File.WriteAllText(Path.Combine(_basePath, "services", "billing", "AGENTS.md"), "# Billing rules");
        Directory.CreateDirectory(Path.Combine(_basePath, "packages", "child", ".aicontext"));
        File.WriteAllText(Path.Combine(_basePath, "packages", "child", "AGENTS.md"), "# Child project");

        Assert.Equal(["services/billing/AGENTS.md"], ProjectInitializer.FindExistingInstructionFiles(_basePath));
    }

    [Fact]
    public void Initialize_ExistingFileWithForce_OverwritesFiles()
    {
        var contextDirectory = Path.Combine(_basePath, ".aicontext");
        Directory.CreateDirectory(contextDirectory);
        var readmePath = Path.Combine(contextDirectory, "README.md");
        File.WriteAllText(readmePath, "custom");

        var result = Init(InitProfile.Core, force: true);

        Assert.Empty(result.Conflicts);
        Assert.Contains("# Project: demo", File.ReadAllText(readmePath));
    }
}
