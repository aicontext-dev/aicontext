using System.Runtime.Versioning;
using AIContext;
using Xunit;

namespace AIContext.Tests;

// F01: ordinary I/O problems (a directory where a file has to go, a locked file, a directory without write
// permission) must come back as diagnostics and as an exact account of what was written, never as an exception or as
// a success that did not happen. A destination that is known to be unwritable blocks every write before the first
// one; a failure that cannot be known beforehand stops the export with what was already written reported.
public sealed class ExportFailureTests : IDisposable
{
    private const string Copilot = ".github/copilot-instructions.md";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "aicontext-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _project;

    public ExportFailureTests()
    {
        _project = Path.Combine(_root, "project");
        Directory.CreateDirectory(_project);
        var init = ProjectInitializer.Initialize(_project, "demo", InitProfile.Core, ContextSchema.CanonicalUrl, "0.1.0", force: false);
        Assert.Empty(init.Conflicts);
        File.WriteAllText(
            InProject(".aicontext/context.json"),
            """{ "specVersion": "0.1.0", "project": { "name": "demo" }, "exports": { "targets": ["agents", "copilot"] } }""");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string InProject(string relative) =>
        Path.Combine(_project, relative.Replace('/', Path.DirectorySeparatorChar));

    private ExportPlan Plan() => ExportService.Plan(new ExportRequest(_project, "all"));

    private void ExportEverything()
    {
        var plan = Plan();
        Assert.False(plan.HasErrors);
        Assert.False(ExportService.Apply(plan).HasErrors);
    }

    // Makes both generated files out of date, so that exporting again updates them.
    private void ChangeTheSource() => File.AppendAllText(InProject(".aicontext/README.md"), "\nA new line.\n");

    [Fact]
    public void Plan_OutputPathIsADirectory_BlocksTheWholeExportBeforeAnythingIsWritten()
    {
        Directory.CreateDirectory(InProject(Copilot));

        var plan = Plan();

        var problem = Assert.Single(plan.Diagnostics, d => d.IsError);
        Assert.Equal(DiagnosticCodes.DestinationNotAFile, problem.Code);
        Assert.Equal(Copilot, problem.Location);
        Assert.Contains("is a directory", problem.Message);
        Assert.Equal(ExportFileStatus.Blocked, Assert.Single(plan.Files, f => f.Target == "copilot").Status);
        Assert.Throws<InvalidOperationException>(() => ExportService.Apply(plan));
        Assert.False(File.Exists(InProject("AGENTS.md")));
    }

    [Fact]
    public void Plan_DirectoryAboveTheOutputIsAFile_ReportsAIC4105()
    {
        File.WriteAllText(InProject(".github"), "not a directory");

        var plan = Plan();

        var problem = Assert.Single(plan.Diagnostics, d => d.IsError);
        Assert.Equal(DiagnosticCodes.DestinationNotAFile, problem.Code);
        Assert.Contains("'.github' is a file", problem.Message);
        Assert.Throws<InvalidOperationException>(() => ExportService.Apply(plan));
        Assert.False(File.Exists(InProject("AGENTS.md")));
    }

    [Fact]
    public void Plan_ExistingGeneratedFileIsLocked_ReportsAIC4106AndBlocksEveryFile()
    {
        ExportEverything();
        ChangeTheSource();
        var agentsBefore = File.ReadAllText(InProject("AGENTS.md"));

        using (TestFiles.Lock(InProject(Copilot)))
        {
            var plan = Plan();

            var problem = Assert.Single(plan.Diagnostics, d => d.IsError);
            Assert.Equal(DiagnosticCodes.FileAccessFailed, problem.Code);
            Assert.Equal(Copilot, problem.Location);
            Assert.Throws<InvalidOperationException>(() => ExportService.Apply(plan));
        }

        Assert.Equal(agentsBefore, File.ReadAllText(InProject("AGENTS.md")));
    }

    [Fact]
    public void Apply_OutputBecameADirectoryAfterPlanning_RefusesBeforeWritingAnything()
    {
        var plan = Plan();
        Assert.False(plan.HasErrors);
        Directory.CreateDirectory(InProject(Copilot));

        var result = ExportService.Apply(plan);

        var problem = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCodes.DestinationNotAFile, problem.Code);
        Assert.Equal(0, result.Changed);
        Assert.Empty(result.Written);
        Assert.False(File.Exists(InProject("AGENTS.md")));
    }

    [Fact]
    public void Apply_FileIsLockedAfterPlanning_RefusesBeforeWritingAnything()
    {
        ExportEverything();
        ChangeTheSource();
        var agentsBefore = File.ReadAllText(InProject("AGENTS.md"));
        var plan = Plan();
        Assert.False(plan.HasErrors);
        Assert.Equal(2, plan.ChangedCount);

        ExportResult result;
        using (TestFiles.Lock(InProject(Copilot)))
        {
            result = ExportService.Apply(plan);
        }

        var problem = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCodes.FileAccessFailed, problem.Code);
        Assert.Equal(Copilot, problem.Location);
        Assert.Equal(0, result.Changed);
        Assert.Empty(result.Written);
        Assert.Equal(agentsBefore, File.ReadAllText(InProject("AGENTS.md")));
    }

    // The first file creates "claimed" as a file; the second needs it to be a directory. Nothing in the plan could
    // tell, so verification passes and the write of the second file fails: the result must list the first as written,
    // say why the second was not, and never report the second as created.
    [Fact]
    public void Apply_SecondFileFailsToWrite_ListsTheFirstAsWrittenAndReportsTheFailure()
    {
        var first = InProject("claimed");
        var second = InProject("claimed/inner.md");
        var files = new[]
        {
            new ExportFilePlan("agents", first, "first", ExportFileStatus.Created),
            new ExportFilePlan("agents", second, "second", ExportFileStatus.Created)
        };
        var plan = new ExportPlan(_project, ["agents"], null, ExportLayout.Single, files, []);

        var result = ExportService.Apply(plan);

        Assert.True(result.HasErrors);
        var problem = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCodes.FileAccessFailed, problem.Code);
        Assert.Equal("claimed/inner.md", problem.Location);
        Assert.Contains("could not be written", problem.Message);
        Assert.Equal(1, result.Changed);
        Assert.Equal(first, Assert.Single(result.Written).Path);
        Assert.Equal("first", File.ReadAllText(first));
        Assert.False(Directory.Exists(first));
    }

    [UnsupportedOSPlatform("windows")]
    [LinkFact(LinkCapability.ReadOnlyDirectory)]
    public void Apply_DirectoryOfTheSecondFileIsReadOnly_KeepsItsContentAndLaterPlanningFinishesTheJob()
    {
        ExportEverything();
        ChangeTheSource();
        var copilotBefore = File.ReadAllText(InProject(Copilot));
        var plan = Plan();
        Assert.False(plan.HasErrors);
        Assert.Equal(2, plan.ChangedCount);

        ExportResult result;
        using (TestFiles.ReadOnlyDirectory(InProject(".github")))
        {
            result = ExportService.Apply(plan);
        }

        var problem = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCodes.FileAccessFailed, problem.Code);
        Assert.Equal(Copilot, problem.Location);
        Assert.Equal(1, result.Changed);
        Assert.Equal(InProject("AGENTS.md"), Assert.Single(result.Written).Path);
        Assert.Contains("A new line.", File.ReadAllText(InProject("AGENTS.md")));
        Assert.Equal(copilotBefore, File.ReadAllText(InProject(Copilot)));
        Assert.Equal(["copilot-instructions.md"], TestLinks.Entries(InProject(".github")));

        var again = Plan();
        Assert.False(again.HasErrors);
        Assert.Equal(1, again.ChangedCount);
        var finished = ExportService.Apply(again);
        Assert.False(finished.HasErrors);
        Assert.Equal(1, finished.Changed);
        Assert.Equal(0, Plan().ChangedCount);
        Assert.Contains("A new line.", File.ReadAllText(InProject(Copilot)));
    }
}
