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

    [Fact]
    public void Initialize_RunTwiceWithoutForce_ReportsEveryFileAsConflictAndChangesNothing()
    {
        var first = Init(InitProfile.Extended);
        var before = first.Created.ToDictionary(path => path, File.ReadAllText);

        var second = Init(InitProfile.Extended);

        Assert.False(first.HasErrors);
        Assert.False(second.HasErrors);
        Assert.Empty(second.Created);
        Assert.Equal(before.Keys, second.Conflicts);
        Assert.All(before, file => Assert.Equal(file.Value, File.ReadAllText(file.Key)));
    }

    [Fact]
    public void Initialize_RunTwiceWithForce_WritesTheSameFilesAgain()
    {
        var first = Init(InitProfile.Core);

        var second = Init(InitProfile.Core, force: true);

        Assert.False(second.HasErrors);
        Assert.Empty(second.Conflicts);
        Assert.Equal(first.Created, second.Created);
    }

    [Theory]
    [InlineData(InitProfile.Core, false)]
    [InlineData(InitProfile.Core, true)]
    [InlineData(InitProfile.Extended, false)]
    [InlineData(InitProfile.Extended, true)]
    public void Initialize_ContextDirectoryIsLinkLeavingProject_WritesNothingOutside(InitProfile profile, bool force)
    {
        var sentinel = Path.Combine(_outside, "sentinel.txt");
        File.WriteAllText(sentinel, "private");
        var link = Path.Combine(_basePath, ".aicontext");
        TestLinks.CreateDirectoryLink(link, _outside);
        _links.Add(link);

        var result = Init(profile, force);

        Assert.Equal("private", File.ReadAllText(sentinel));
        Assert.Equal(["sentinel.txt"], TestLinks.Entries(_outside));
        AssertRefused(result, DiagnosticCodes.PathOutsideProject);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Initialize_ContextDirectoryIsLinkIntoSeparateProject_WritesNothingThere(bool force)
    {
        var child = Path.Combine(_basePath, "pkg", "child");
        Directory.CreateDirectory(Path.Combine(child, ".aicontext"));
        var link = Path.Combine(_basePath, ".aicontext");
        TestLinks.CreateDirectoryLink(link, child);
        _links.Add(link);

        var result = Init(InitProfile.Core, force);

        Assert.Equal([".aicontext"], TestLinks.Entries(child));
        Assert.Empty(TestLinks.Entries(Path.Combine(child, ".aicontext")));
        AssertRefused(result, DiagnosticCodes.PathInSeparateProject);
    }

    [Fact]
    public void Initialize_ContextDirectoryIsLinkInsideProject_WritesThroughIt()
    {
        var shared = Path.Combine(_basePath, "shared-context");
        Directory.CreateDirectory(shared);
        var link = Path.Combine(_basePath, ".aicontext");
        TestLinks.CreateDirectoryLink(link, shared);
        _links.Add(link);

        var result = Init(InitProfile.Core);

        Assert.False(result.HasErrors);
        Assert.Empty(result.Conflicts);
        Assert.Equal(["README.md", "context.json"], TestLinks.Entries(shared));
    }

    [LinkFact(LinkCapability.DirectorySymlink)]
    public void Initialize_ContextDirectoryIsDanglingLinkLeavingProject_CreatesNothingOutside()
    {
        var missing = Path.Combine(_outside, "missing");
        var link = Path.Combine(_basePath, ".aicontext");
        TestLinks.CreateDirectorySymlink(link, missing);
        _links.Add(link);

        var result = Init(InitProfile.Core);

        Assert.False(Directory.Exists(missing));
        Assert.Empty(TestLinks.Entries(_outside));
        AssertRefused(result, DiagnosticCodes.PathOutsideProject);
    }

    // The file that is a link is the last one init writes, so a version that checked destinations one at a time while
    // writing would already have created the others.
    [LinkTheory(LinkCapability.FileSymlink)]
    [InlineData(InitProfile.Core, "context.json", false)]
    [InlineData(InitProfile.Core, "context.json", true)]
    [InlineData(InitProfile.Extended, "constraints.md", false)]
    [InlineData(InitProfile.Extended, "constraints.md", true)]
    public void Initialize_ExistingFileLinkLeavingProject_KeepsTheExternalFileAndWritesNothing(InitProfile profile, string linked, bool force)
    {
        var secret = Path.Combine(_outside, "secret.md");
        File.WriteAllText(secret, "private");
        var contextDirectory = CreateContextDirectory();
        var link = Path.Combine(contextDirectory, linked);
        TestLinks.CreateFileLink(link, secret);
        _links.Add(link);

        var result = Init(profile, force);

        Assert.Equal("private", File.ReadAllText(secret));
        Assert.Equal([linked], TestLinks.Entries(contextDirectory));
        AssertRefused(result, DiagnosticCodes.PathOutsideProject);
    }

    [LinkTheory(LinkCapability.FileSymlink)]
    [InlineData(InitProfile.Core, "context.json", false)]
    [InlineData(InitProfile.Core, "context.json", true)]
    [InlineData(InitProfile.Extended, "constraints.md", false)]
    [InlineData(InitProfile.Extended, "constraints.md", true)]
    public void Initialize_DanglingFileLinkLeavingProject_DoesNotCreateTheExternalTarget(InitProfile profile, string linked, bool force)
    {
        var target = Path.Combine(_outside, "not-yet.md");
        var contextDirectory = CreateContextDirectory();
        var link = Path.Combine(contextDirectory, linked);
        TestLinks.CreateFileLink(link, target);
        _links.Add(link);

        var result = Init(profile, force);

        Assert.False(File.Exists(target));
        Assert.Empty(TestLinks.Entries(_outside));
        Assert.Equal([linked], TestLinks.Entries(contextDirectory));
        AssertRefused(result, DiagnosticCodes.PathOutsideProject);
    }

    [LinkFact(LinkCapability.FileSymlink)]
    public void Initialize_ExistingFileLinkInsideProjectWithoutForce_ReportsConflictAndKeepsItsTarget()
    {
        var notes = Path.Combine(_basePath, "notes.md");
        File.WriteAllText(notes, "mine");
        var contextDirectory = CreateContextDirectory();
        var link = Path.Combine(contextDirectory, "README.md");
        TestLinks.CreateFileLink(link, notes);
        _links.Add(link);

        var result = Init(InitProfile.Core);

        Assert.False(result.HasErrors);
        Assert.Equal([link], result.Conflicts);
        Assert.Equal("mine", File.ReadAllText(notes));
        Assert.False(File.Exists(Path.Combine(contextDirectory, "context.json")));
    }

    [LinkFact(LinkCapability.FileSymlink)]
    public void Initialize_ExistingFileLinkInsideProjectWithForce_OverwritesItsTarget()
    {
        var notes = Path.Combine(_basePath, "notes.md");
        File.WriteAllText(notes, "mine");
        var contextDirectory = CreateContextDirectory();
        var link = Path.Combine(contextDirectory, "README.md");
        TestLinks.CreateFileLink(link, notes);
        _links.Add(link);

        var result = Init(InitProfile.Core, force: true);

        Assert.False(result.HasErrors);
        Assert.Contains("# Project: demo", File.ReadAllText(notes));
        Assert.True(File.Exists(Path.Combine(contextDirectory, "context.json")));
    }

    private static void AssertRefused(InitResult result, string code)
    {
        Assert.True(result.HasErrors);
        Assert.Empty(result.Created);
        Assert.Empty(result.Conflicts);
        Assert.Contains(result.Diagnostics, d => d.IsError && d.Code == code && d.Message.Contains("Init never writes"));
    }
}
