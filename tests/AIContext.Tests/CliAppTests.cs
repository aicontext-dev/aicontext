using System.Runtime.Versioning;
using AIContext.Cli;
using Xunit;

namespace AIContext.Tests;

public sealed class CliAppTests : IDisposable
{
    private readonly string _basePath = Path.Combine(Path.GetTempPath(), "aicontext-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _outsidePath = Path.Combine(Path.GetTempPath(), "aicontext-tests-outside-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _links = [];

    public CliAppTests()
    {
        Directory.CreateDirectory(_basePath);
    }

    public void Dispose()
    {
        foreach (var link in Enumerable.Reverse(_links))
        {
            TestLinks.Remove(link);
        }

        Directory.Delete(_basePath, recursive: true);
        if (Directory.Exists(_outsidePath))
        {
            Directory.Delete(_outsidePath, recursive: true);
        }
    }

    private sealed record CliResult(int ExitCode, string Output, string Error);

    // The temp directory is the working directory, so a command without --path can never touch the repository checkout.
    private CliResult Run(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = CliApp.Run(args, output, error, _basePath);
        return new CliResult(exitCode, output.ToString(), error.ToString());
    }

    private void InitProject(string profile = "core")
    {
        Assert.Equal(0, Run("init", "--name", "demo", "--profile", profile).ExitCode);
    }

    private string PathOf(string relative) => Path.Combine(_basePath, relative);

    [Fact]
    public void Run_NoArguments_PrintsHelp()
    {
        var result = Run();

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Usage:", result.Output);
        Assert.Equal(string.Empty, result.Error);
    }

    [Fact]
    public void Run_Version_PrintsToolAndSpecificationVersion()
    {
        var result = Run("--version");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("aicontext ", result.Output);
        Assert.Contains("Specification version: 0.1.0", result.Output);
    }

    [Fact]
    public void Run_UnknownCommand_ReturnsError()
    {
        var result = Run("bogus");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Unknown command 'bogus'", result.Error);
    }

    [Theory]
    [InlineData("init", "--help")]
    [InlineData("validate", "--help")]
    [InlineData("export", "-h")]
    [InlineData("status", "--help")]
    public void Run_CommandHelp_PrintsUsageAndSucceeds(string command, string helpFlag)
    {
        var result = Run(command, helpFlag);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains($"aicontext {command} [--", result.Output);
        Assert.Equal(string.Empty, result.Error);
    }

    [Fact]
    public void Validate_UnknownOption_ReturnsErrorWithoutValidating()
    {
        InitProject();

        var result = Run("validate", "--strcit");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Unknown option '--strcit'", result.Error);
        Assert.Contains("--strict", result.Error);
        Assert.DoesNotContain("validation passed", result.Output);
    }

    [Fact]
    public void Validate_OptionOfAnotherCommand_ReturnsError()
    {
        InitProject();

        var result = Run("validate", "--force");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Unknown option '--force'", result.Error);
    }

    [Fact]
    public void Validate_PositionalArgument_ReturnsError()
    {
        InitProject();

        var result = Run("validate", "strict");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Unknown argument 'strict'", result.Error);
    }

    [Fact]
    public void Validate_StrictWithNonBooleanValue_ReturnsError()
    {
        InitProject();

        var result = Run("validate", "--strict", "no");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("'--strict' does not take a value", result.Error);
    }

    [Theory]
    [InlineData("--strict")]
    [InlineData("--STRICT")]
    public void Validate_StrictOnProjectWithWarnings_ReturnsError(string flag)
    {
        InitProject();

        var result = Run("validate", flag);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("treated as errors", result.Error);
    }

    [Fact]
    public void Validate_StrictExplicitTrue_TreatsWarningsAsErrors()
    {
        InitProject();

        Assert.Equal(1, Run("validate", "--strict", "true").ExitCode);
    }

    [Fact]
    public void Validate_StrictExplicitFalse_DoesNotTreatWarningsAsErrors()
    {
        InitProject();

        var result = Run("validate", "--strict", "false");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("validation passed", result.Output);
    }

    [Fact]
    public void Init_PathWithoutValue_ReturnsErrorAndLeavesExistingFilesUntouched()
    {
        InitProject();
        var readme = PathOf(Path.Combine(".aicontext", "README.md"));
        File.WriteAllText(readme, "hand-written content");

        var result = Run("init", "--path", "--force");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("'--path' requires a value", result.Error);
        Assert.Equal("hand-written content", File.ReadAllText(readme));
    }

    [Fact]
    public void Init_NameWithoutValue_ReturnsError()
    {
        var result = Run("init", "--name");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("'--name' requires a value", result.Error);
        Assert.False(Directory.Exists(PathOf(".aicontext")));
    }

    [Fact]
    public void Export_TargetWithoutValue_ReturnsError()
    {
        InitProject();

        var result = Run("export", "--target", "--dry-run");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("'--target' requires a value", result.Error);
    }

    [Fact]
    public void Export_DuplicateOption_ReturnsError()
    {
        InitProject();

        var result = Run("export", "--target", "agents", "--target", "claude");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("'--target' was specified more than once", result.Error);
        Assert.False(File.Exists(PathOf("AGENTS.md")));
    }

    [Fact]
    public void Export_MistypedCheckOption_WritesNothing()
    {
        InitProject();

        var result = Run("export", "--target", "agents", "--chek");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Unknown option '--chek'", result.Error);
        Assert.False(File.Exists(PathOf("AGENTS.md")));
    }

    [Fact]
    public void Status_UnknownOption_ReturnsError()
    {
        InitProject();

        var result = Run("status", "--strict");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Unknown option '--strict'", result.Error);
    }

    [Fact]
    public void Status_InitializedProject_ReportsNotExported()
    {
        InitProject();

        var result = Run("status");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("not exported", result.Output);
    }

    [Fact]
    public void InitValidateExportCheck_ExtendedProject_FollowsExitCodeContract()
    {
        var init = Run("init", "--name", "demo", "--profile", "extended");
        Assert.Equal(0, init.ExitCode);
        Assert.Contains("replace the TODO placeholders", init.Output);

        Assert.Equal(0, Run("validate").ExitCode);
        Assert.Equal(1, Run("validate", "--strict").ExitCode);
        FillInPlaceholders();
        Assert.Equal(0, Run("validate", "--strict").ExitCode);

        Assert.Equal(1, Run("export", "--target", "all", "--check").ExitCode);
        Assert.Equal(0, Run("export", "--target", "all").ExitCode);
        Assert.Equal(0, Run("export", "--target", "all", "--check").ExitCode);

        File.AppendAllText(PathOf(Path.Combine(".aicontext", "README.md")), "\nChanged.\n");
        var stale = Run("export", "--target", "all", "--check");

        Assert.Equal(1, stale.ExitCode);
        Assert.Contains("out of date", stale.Error);
    }

    private void FillInPlaceholders()
    {
        var directory = PathOf(".aicontext");
        File.WriteAllText(Path.Combine(directory, "README.md"), "# Project: demo\n\nA billing service.\n\n**Tech stack:** .NET 10\n");
        File.WriteAllText(Path.Combine(directory, "architecture.md"), "Two layers: API and domain.");
        File.WriteAllText(Path.Combine(directory, "decisions.md"), "### D1. Use PostgreSQL\n**Decision:** PostgreSQL.");
        File.WriteAllText(Path.Combine(directory, "constraints.md"), "- Never log customer data.");
    }

    [Fact]
    public void Validate_PlaceholdersFromInit_AreWarningsThatFailOnlyUnderStrict()
    {
        Assert.Equal(0, Run("init", "--name", "demo", "--profile", "extended").ExitCode);

        var plain = Run("validate");
        var strict = Run("validate", "--strict");

        Assert.Equal(0, plain.ExitCode);
        Assert.Contains("WARNING: Placeholder text in .aicontext/constraints.md:3", plain.Output);
        Assert.Equal(1, strict.ExitCode);
        Assert.Contains("treated as errors", strict.Error);
    }

    [Fact]
    public void Validate_Success_SaysThatContentIsNotEvaluated()
    {
        InitProject();

        var result = Run("validate");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("not whether the content is useful or up to date", result.Output);
    }

    [Fact]
    public void Validate_FailedRun_DoesNotPrintTheSuccessNote()
    {
        InitProject();
        File.Delete(PathOf(Path.Combine(".aicontext", "context.json")));

        Assert.DoesNotContain("not whether the content", Run("validate").Output);
    }

    [Fact]
    public void Export_HandWrittenFile_ReturnsConflictAndKeepsFile()
    {
        InitProject();
        File.WriteAllText(PathOf("AGENTS.md"), "hand written");

        var result = Run("export", "--target", "agents");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("No files were written", result.Error);
        Assert.Equal("hand written", File.ReadAllText(PathOf("AGENTS.md")));
    }

    [Fact]
    public void Init_WithHandWrittenInstructionFiles_ListsThemAndPointsToTheRecipe()
    {
        File.WriteAllText(PathOf("AGENTS.md"), "# My rules");
        File.WriteAllText(PathOf("CLAUDE.md"), "# Claude rules");

        var result = Run("init", "--name", "demo");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("These instruction files already exist and were not generated by aicontext:", result.Output);
        Assert.Contains("  AGENTS.md", result.Output);
        Assert.Contains("  CLAUDE.md", result.Output);
        Assert.Contains("recipe B in the user manual", result.Output);
        Assert.Equal("# My rules", File.ReadAllText(PathOf("AGENTS.md")));
    }

    [Fact]
    public void Init_WithHandWrittenRuleFiles_ListsThemAndSaysTheyStayActive()
    {
        Directory.CreateDirectory(PathOf(".github/instructions"));
        File.WriteAllText(PathOf(".github/instructions/api.instructions.md"), "---\napplyTo: 'src/**'\n---\nRule.");

        var result = Run("init", "--name", "demo");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("  .github/instructions/api.instructions.md", result.Output);
        Assert.Contains("stay active next to the generated ones", result.Output);
        Assert.Contains("Other places are not searched", result.Output);
    }

    [Fact]
    public void Init_ManyHandWrittenRuleFiles_ListsOnlyTheFirstOnes()
    {
        Directory.CreateDirectory(PathOf(".cursor/rules"));
        for (var i = 0; i < 13; i++)
        {
            File.WriteAllText(PathOf($".cursor/rules/rule-{i:00}.mdc"), "---\nalwaysApply: true\n---\nRule.");
        }

        var result = Run("init", "--name", "demo");

        Assert.Contains("  .cursor/rules/rule-00.mdc", result.Output);
        Assert.DoesNotContain("rule-12.mdc", result.Output);
        Assert.Contains("... and 3 more", result.Output);
    }

    [Fact]
    public void Init_WithoutInstructionFiles_PrintsNoMigrationHint()
    {
        var result = Run("init", "--name", "demo");

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("already exist and were not generated", result.Output);
    }

    [Fact]
    public void Export_HandWrittenFile_PointsToTheMigrationRecipe()
    {
        InitProject();
        File.WriteAllText(PathOf("AGENTS.md"), "hand written");

        var result = Run("export", "--target", "agents");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("recipe B in the user manual", result.Error);
    }

    [Fact]
    public void Export_OtherErrors_DoNotMentionTheMigrationRecipe()
    {
        InitProject();

        Assert.DoesNotContain("recipe B", Run("export", "--target", "nope").Error);
    }

    [Fact]
    public void Export_ParentOfIndependentProject_LeavesTheChildExportInSync()
    {
        InitProject();
        var child = PathOf(Path.Combine("pkg", "child"));
        Assert.Equal(0, Run("init", "--path", child, "--name", "child").ExitCode);
        Assert.Equal(0, Run("export", "--target", "agents", "--path", child).ExitCode);

        var parent = Run("export", "--target", "agents");

        Assert.Equal(0, parent.ExitCode);
        Assert.DoesNotContain("Removed", parent.Output);
        Assert.True(File.Exists(Path.Combine(child, "AGENTS.md")));
        Assert.Equal(0, Run("export", "--target", "agents", "--path", child, "--check").ExitCode);
    }

    private void WriteContext(string json) =>
        File.WriteAllText(PathOf(Path.Combine(".aicontext", "context.json")), json);

    private void SetUpNestedRuleBehindLink()
    {
        InitProject();
        WriteContext("""{ "specVersion": "0.1.0", "project": { "name": "demo" }, "exports": { "layout": "split", "nestedAgents": true } }""");
        Directory.CreateDirectory(PathOf(Path.Combine(".aicontext", "rules")));
        File.WriteAllText(
            PathOf(Path.Combine(".aicontext", "rules", "linked.md")),
            "---\ndescription: Linked\npaths: [\"linked/**\"]\n---\nBody.\n");

        Directory.CreateDirectory(_outsidePath);
        var link = PathOf("linked");
        TestLinks.CreateDirectoryLink(link, _outsidePath);
        _links.Add(link);
    }

    [Fact]
    public void Export_NestedDirectoryLinkedOutsideProject_FailsAndWritesNothing()
    {
        SetUpNestedRuleBehindLink();

        var result = Run("export", "--target", "agents");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("outside the project", result.Error);
        Assert.Contains("No files were written", result.Error);
        Assert.Empty(Directory.GetFileSystemEntries(_outsidePath));
        Assert.False(File.Exists(PathOf("AGENTS.md")));
    }

    [Fact]
    public void Export_DryRunWithLinkOutsideProject_StillFails()
    {
        SetUpNestedRuleBehindLink();

        var result = Run("export", "--target", "agents", "--dry-run");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Blocked (agents)", result.Output);
    }

    [Fact]
    public void Status_NestedDirectoryLinkedOutsideProject_ReportsBlocked()
    {
        SetUpNestedRuleBehindLink();

        var result = Run("status");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("blocked", result.Output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Init_ContextDirectoryLinkedOutsideProject_FailsAndWritesNothing(bool force)
    {
        Directory.CreateDirectory(_outsidePath);
        File.WriteAllText(Path.Combine(_outsidePath, "sentinel.txt"), "private");
        var link = PathOf(".aicontext");
        TestLinks.CreateDirectoryLink(link, _outsidePath);
        _links.Add(link);

        var result = force
            ? Run("init", "--name", "demo", "--profile", "extended", "--force")
            : Run("init", "--name", "demo", "--profile", "extended");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Init never writes outside the project", result.Error);
        Assert.Contains("No files were written", result.Error);
        Assert.DoesNotContain("Created:", result.Output);
        Assert.Equal(["sentinel.txt"], TestLinks.Entries(_outsidePath));
        Assert.Equal("private", File.ReadAllText(Path.Combine(_outsidePath, "sentinel.txt")));
    }

    [Fact]
    public void Export_UnsupportedSpecVersion_FailsAndWritesNothing()
    {
        InitProject();
        WriteContext("""{ "specVersion": "999.0.0", "project": { "name": "demo" } }""");

        var result = Run("export", "--target", "agents");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("ERROR: context.json /specVersion:", result.Error);
        Assert.Contains("No files were written", result.Error);
        Assert.False(File.Exists(PathOf("AGENTS.md")));
    }

    [Fact]
    public void Export_ContextThatViolatesTheSchema_FailsAndWritesNothing()
    {
        InitProject();
        WriteContext("""{ "specVersion": "0.1.0", "project": { "name": "demo" }, "bogus": true }""");

        var result = Run("export", "--target", "all");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("/bogus", result.Error);
        Assert.False(File.Exists(PathOf("AGENTS.md")));
    }

    [Fact]
    public void Status_InvalidContext_Fails()
    {
        InitProject();
        WriteContext("[1, 2, 3]");

        var result = Run("status");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("ERROR: context.json", result.Error);
    }

    [Fact]
    public void Export_ProjectWithOnlyWarnings_Succeeds()
    {
        InitProject();
        Assert.Equal(1, Run("validate", "--strict").ExitCode);

        var result = Run("export", "--target", "agents");

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(PathOf("AGENTS.md")));
    }

    [Fact]
    public void Export_UnsupportedTarget_ReturnsErrorAndWritesNothing()
    {
        InitProject();

        var result = Run("export", "--target", "nope");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("ERROR: Unsupported export target 'nope'", result.Error);
        Assert.Contains("No files were written", result.Error);
    }

    [Fact]
    public void Export_NoTargetAndNoDefaultTarget_NamesTheCliOption()
    {
        InitProject();

        var result = Run("export");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Missing required option: --target", result.Error);
    }

    [Fact]
    public void Export_UnsupportedLayout_ReturnsError()
    {
        InitProject();

        var result = Run("export", "--target", "agents", "--layout", "wide");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Unsupported layout 'wide'", result.Error);
        Assert.False(File.Exists(PathOf("AGENTS.md")));
    }

    [Fact]
    public void Export_DefaultTargetFromContext_IsAnnounced()
    {
        InitProject();
        WriteContext("""{ "specVersion": "0.1.0", "project": { "name": "demo" }, "exports": { "defaultTarget": "claude" } }""");

        var result = Run("export");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Using exports.defaultTarget 'claude'", result.Output);
        Assert.True(File.Exists(PathOf("CLAUDE.md")));
    }

    [Fact]
    public void Status_UnsupportedLayout_ReturnsError()
    {
        InitProject();

        var result = Run("status", "--layout", "wide");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("ERROR: Unsupported layout 'wide'", result.Error);
    }

    [Fact]
    public void Status_TargetsThatAreReadTogether_PrintsTheOverlap()
    {
        InitProject();
        WriteContext("""{ "specVersion": "0.1.0", "project": { "name": "demo" }, "exports": { "targets": ["agents", "copilot"] } }""");

        var result = Run("status");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Overlap:", result.Output);
        Assert.Contains("Copilot (VS Code)", result.Output);
        Assert.Contains("copilot + agents", result.Output);
    }

    [Fact]
    public void Status_SingleTarget_PrintsNoOverlap()
    {
        InitProject();
        WriteContext("""{ "specVersion": "0.1.0", "project": { "name": "demo" }, "exports": { "targets": ["agents"] } }""");

        Assert.DoesNotContain("Overlap:", Run("status").Output);
    }

    private void UseAgentsAndCopilot() =>
        WriteContext("""{ "specVersion": "0.1.0", "project": { "name": "demo" }, "exports": { "targets": ["agents", "copilot"] } }""");

    [Theory]
    [InlineData("validate")]
    [InlineData("status")]
    [InlineData("export")]
    public void Command_LockedReadme_ReportsTheFileAndFailsWithoutAnException(string command)
    {
        InitProject();
        UseAgentsAndCopilot();

        CliResult result;
        using (TestFiles.Lock(PathOf(Path.Combine(".aicontext", "README.md"))))
        {
            result = command == "export" ? Run(command, "--target", "all") : Run(command);
        }

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("README.md could not be read", result.Error);
        Assert.False(File.Exists(PathOf("AGENTS.md")));
    }

    // The preview must say what the real export will find: an output that has to change but is read-only (a file the
    // tool never has to write is not a problem). Every mode fails with exit code 1, names the file, and writes nothing.
    [LinkTheory(LinkCapability.ReadOnlyFile)]
    [InlineData("--dry-run")]
    [InlineData("--check")]
    [InlineData("")]
    public void Export_OutOfDateOutputIsReadOnly_EveryModeFailsAndNamesTheFile(string mode)
    {
        InitProject();
        UseAgentsAndCopilot();
        Assert.Equal(0, Run("export", "--target", "all").ExitCode);
        File.AppendAllText(PathOf(Path.Combine(".aicontext", "README.md")), "\nA new line.\n");
        var agentsBefore = File.ReadAllText(PathOf("AGENTS.md"));
        var copilotPath = PathOf(Path.Combine(".github", "copilot-instructions.md"));
        var copilotBefore = File.ReadAllText(copilotPath);

        CliResult result;
        using (TestFiles.ReadOnlyFile(copilotPath))
        {
            result = mode.Length == 0 ? Run("export", "--target", "all") : Run("export", "--target", "all", mode);
        }

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("ERROR: ", result.Error);
        Assert.Contains("copilot-instructions.md cannot be written", result.Error);
        Assert.Contains("No files were written", result.Error);
        Assert.Contains("Blocked (copilot)", result.Output);
        Assert.DoesNotContain("Export is out of date", result.Error);
        Assert.DoesNotContain("Export completed", result.Output);
        Assert.DoesNotContain("Dry run:", result.Output);
        Assert.Equal(agentsBefore, File.ReadAllText(PathOf("AGENTS.md")));
        Assert.Equal(copilotBefore, File.ReadAllText(copilotPath));
    }

    [LinkFact(LinkCapability.ReadOnlyFile)]
    public void Export_ReadOnlyOutputIsUpToDate_PreviewAndCheckPass()
    {
        InitProject();
        UseAgentsAndCopilot();
        Assert.Equal(0, Run("export", "--target", "all").ExitCode);

        using (TestFiles.ReadOnlyFile(PathOf(Path.Combine(".github", "copilot-instructions.md"))))
        {
            Assert.Equal(0, Run("export", "--target", "all", "--dry-run").ExitCode);
            Assert.Equal(0, Run("export", "--target", "all", "--check").ExitCode);
            Assert.Equal(0, Run("export", "--target", "all").ExitCode);
        }
    }

    [Fact]
    public void Export_OutputPathIsADirectory_FailsBeforeWritingAnyFileAndClaimsNoSuccess()
    {
        InitProject();
        UseAgentsAndCopilot();
        Directory.CreateDirectory(PathOf(Path.Combine(".github", "copilot-instructions.md")));

        var result = Run("export", "--target", "all");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("is a directory", result.Error);
        Assert.Contains("No files were written", result.Error);
        Assert.Contains("Would create (agents)", result.Output);
        Assert.DoesNotContain("Created (", result.Output);
        Assert.DoesNotContain("Export completed", result.Output);
        Assert.False(File.Exists(PathOf("AGENTS.md")));
    }

    [Fact]
    public void Export_Success_ReportsTheFilesOnlyAfterTheyWereWritten()
    {
        InitProject();
        UseAgentsAndCopilot();

        var first = Run("export", "--target", "all");

        Assert.Equal(0, first.ExitCode);
        Assert.Contains("Created (agents)", first.Output);
        Assert.Contains("Created (copilot)", first.Output);
        Assert.DoesNotContain("Would", first.Output);
        Assert.Contains("Export completed (2 changed, 0 unchanged).", first.Output);
        Assert.True(File.Exists(PathOf("AGENTS.md")));

        var second = Run("export", "--target", "all");

        Assert.Equal(0, second.ExitCode);
        Assert.Contains("Unchanged (agents)", second.Output);
        Assert.Contains("Export completed (0 changed, 2 unchanged).", second.Output);
    }

    [Fact]
    public void Export_DryRun_AnnouncesAPlanAndWritesNothing()
    {
        InitProject();
        UseAgentsAndCopilot();

        var result = Run("export", "--target", "all", "--dry-run");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Would create (agents)", result.Output);
        Assert.DoesNotContain("Created (", result.Output);
        Assert.False(File.Exists(PathOf("AGENTS.md")));
    }

    [UnsupportedOSPlatform("windows")]
    [LinkFact(LinkCapability.ReadOnlyDirectory)]
    public void Export_SecondFileCannotBeWritten_ReportsWhatWasWrittenAndWhatWasNot()
    {
        InitProject();
        UseAgentsAndCopilot();
        Assert.Equal(0, Run("export", "--target", "all").ExitCode);
        File.AppendAllText(PathOf(Path.Combine(".aicontext", "README.md")), "\nA new line.\n");
        var copilotBefore = File.ReadAllText(PathOf(Path.Combine(".github", "copilot-instructions.md")));

        CliResult failed;
        using (TestFiles.ReadOnlyDirectory(PathOf(".github")))
        {
            failed = Run("export", "--target", "all");
        }

        Assert.Equal(1, failed.ExitCode);
        Assert.Contains("Updated (agents)", failed.Output);
        Assert.Contains("Not written (copilot)", failed.Output);
        Assert.DoesNotContain("Updated (copilot)", failed.Output);
        Assert.DoesNotContain("Export completed", failed.Output);
        Assert.Contains("could not be written", failed.Error);
        Assert.Contains("Only 1 of 2 changed file(s) were written", failed.Error);
        Assert.Contains("not a transaction", failed.Error);
        Assert.Equal(copilotBefore, File.ReadAllText(PathOf(Path.Combine(".github", "copilot-instructions.md"))));

        var retry = Run("export", "--target", "all");

        Assert.Equal(0, retry.ExitCode);
        Assert.Contains("Unchanged (agents)", retry.Output);
        Assert.Contains("Updated (copilot)", retry.Output);
        Assert.Contains("Export completed (1 changed, 1 unchanged).", retry.Output);
    }

    [Fact]
    public void Init_OutputPathIsADirectory_FailsAndWritesNothing()
    {
        Directory.CreateDirectory(PathOf(Path.Combine(".aicontext", "README.md")));

        var result = Run("init", "--name", "demo");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("is a directory", result.Error);
        Assert.Contains("No files were written", result.Error);
        Assert.DoesNotContain("Created:", result.Output);
        Assert.False(File.Exists(PathOf(Path.Combine(".aicontext", "context.json"))));
    }
}
