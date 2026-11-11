using AIContext;
using Xunit;

namespace AIContext.Tests;

public sealed class ProjectInitializerTests : IDisposable
{
    private const string SchemaUrl = "https://aicontext.dev/schema/v1/context.schema.json";
    private const string SpecVersion = "0.1.0";

    private readonly string _basePath = Path.Combine(Path.GetTempPath(), "aicontext-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _outside = Path.Combine(Path.GetTempPath(), "aicontext-tests-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _links = [];

    public ProjectInitializerTests()
    {
        Directory.CreateDirectory(_basePath);
        Directory.CreateDirectory(_outside);
    }

    public void Dispose()
    {
        foreach (var link in Enumerable.Reverse(_links))
        {
            TestLinks.Remove(link);
        }

        Directory.Delete(_basePath, recursive: true);
        Directory.Delete(_outside, recursive: true);
    }

    private InitResult Init(InitProfile profile, bool force = false) =>
        ProjectInitializer.Initialize(_basePath, "demo", profile, SchemaUrl, SpecVersion, force);

    private string CreateContextDirectory()
    {
        var contextDirectory = Path.Combine(_basePath, ".aicontext");
        Directory.CreateDirectory(contextDirectory);
        return contextDirectory;
    }


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

    // The tests below check what init really did to the file system outside the project. They assert the safe
    // outcome, so they fail while init writes through a link that leaves the project; they are not skipped.

    [Trait("PlannedFix", "step-03")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Initialize_ContextDirectoryIsLinkLeavingProject_WritesNothingOutside(bool force)
    {
        var sentinel = Path.Combine(_outside, "sentinel.txt");
        File.WriteAllText(sentinel, "private");
        var link = Path.Combine(_basePath, ".aicontext");
        TestLinks.CreateDirectoryLink(link, _outside);
        _links.Add(link);

        Init(InitProfile.Extended, force);

        Assert.Equal("private", File.ReadAllText(sentinel));
        Assert.Equal(["sentinel.txt"], TestLinks.Entries(_outside));
    }

    // On Linux init currently stops with an IOException before it writes anything, because the directory cannot be
    // created; after a fix it may refuse in another way. Only what appeared outside the project matters here, and
    // any failure other than an IOException is unexpected.
    [Trait("PlannedFix", "step-03")]
    [LinkFact(LinkCapability.DirectorySymlink)]
    public void Initialize_ContextDirectoryIsDanglingLinkLeavingProject_CreatesNothingOutside()
    {
        var missing = Path.Combine(_outside, "missing");
        var link = Path.Combine(_basePath, ".aicontext");
        TestLinks.CreateDirectorySymlink(link, missing);
        _links.Add(link);

        var error = Record.Exception(() => Init(InitProfile.Core));

        Assert.True(error is null or IOException, $"Unexpected failure: {error}");
        Assert.False(Directory.Exists(missing));
        Assert.Empty(TestLinks.Entries(_outside));
    }

    [Trait("PlannedFix", "step-03")]
    [LinkTheory(LinkCapability.FileSymlink)]
    [InlineData(false)]
    [InlineData(true)]
    public void Initialize_ExistingFileLinkLeavingProject_KeepsTheExternalFileAndWritesNothing(bool force)
    {
        var secret = Path.Combine(_outside, "secret.md");
        File.WriteAllText(secret, "private");
        var contextDirectory = CreateContextDirectory();
        var link = Path.Combine(contextDirectory, "README.md");
        TestLinks.CreateFileLink(link, secret);
        _links.Add(link);

        Init(InitProfile.Core, force);

        Assert.Equal("private", File.ReadAllText(secret));
        Assert.False(File.Exists(Path.Combine(contextDirectory, "context.json")));
    }

    [Trait("PlannedFix", "step-03")]
    [LinkTheory(LinkCapability.FileSymlink)]
    [InlineData(false)]
    [InlineData(true)]
    public void Initialize_DanglingFileLinkLeavingProject_DoesNotCreateTheExternalTarget(bool force)
    {
        var target = Path.Combine(_outside, "not-yet.md");
        var contextDirectory = CreateContextDirectory();
        var link = Path.Combine(contextDirectory, "README.md");
        TestLinks.CreateFileLink(link, target);
        _links.Add(link);

        Init(InitProfile.Core, force);

        Assert.False(File.Exists(target));
        Assert.False(File.Exists(Path.Combine(contextDirectory, "context.json")));
    }
}
