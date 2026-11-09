using AIContext;
using Xunit;

namespace AIContext.Tests;

public sealed class ExportServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aicontext-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _project;
    private readonly string _outside;
    private readonly List<string> _links = [];

    public ExportServiceTests()
    {
        _project = Path.Combine(_root, "project");
        _outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(_project);
        Directory.CreateDirectory(_outside);

        var init = ProjectInitializer.Initialize(_project, "demo", InitProfile.Core, ContextSchema.CanonicalUrl, "0.1.0", force: false);
        Assert.Empty(init.Conflicts);
    }

    public void Dispose()
    {
        foreach (var link in Enumerable.Reverse(_links))
        {
            TestLinks.Remove(link);
        }

        Directory.Delete(_root, recursive: true);
    }

    private string InProject(string relative) =>
        Path.Combine(_project, relative.Replace('/', Path.DirectorySeparatorChar));

    private void WriteContext(string exportsJson)
    {
        File.WriteAllText(
            InProject(".aicontext/context.json"),
            "{ \"specVersion\": \"0.1.0\", \"project\": { \"name\": \"demo\" }" + (exportsJson.Length == 0 ? "" : $", \"exports\": {exportsJson}") + " }");
    }

    private ExportPlan Plan(string? target = null, string? layout = null, bool force = false) =>
        ExportService.Plan(new ExportRequest(_project, target, layout, force));

    private static Diagnostic ErrorWithCode(ExportPlan plan, string code) =>
        Assert.Single(plan.Diagnostics, d => d.IsError && d.Code == code);

    [Fact]
    public void Plan_ExplicitTarget_PlansFilesAndWritesNothing()
    {
        var plan = Plan("agents");

        Assert.False(plan.HasErrors);
        Assert.Equal(["agents"], plan.Targets);
        Assert.Null(plan.DefaultTargetUsed);
        Assert.Equal(ExportFileStatus.Created, Assert.Single(plan.Files).Status);
        Assert.Equal(1, plan.ChangedCount);
        Assert.False(File.Exists(InProject("AGENTS.md")));
    }

    [Fact]
    public void Plan_WithoutTarget_UsesExportsDefaultTarget()
    {
        WriteContext("""{ "defaultTarget": "claude" }""");

        var plan = Plan();

        Assert.False(plan.HasErrors);
        Assert.Equal("claude", plan.DefaultTargetUsed);
        Assert.Equal(["claude"], plan.Targets);
    }

    [Fact]
    public void Plan_ExplicitTargetWinsOverDefaultTarget()
    {
        WriteContext("""{ "defaultTarget": "claude" }""");

        var plan = Plan("agents");

        Assert.Null(plan.DefaultTargetUsed);
        Assert.Equal(["agents"], plan.Targets);
    }

    [Fact]
    public void Plan_WithoutTargetAndWithoutDefault_ReportsMissingTarget()
    {
        var plan = Plan();

        Assert.Empty(plan.Files);
        Assert.Contains("exports.defaultTarget", ErrorWithCode(plan, DiagnosticCodes.MissingTarget).Message);
    }

    [Fact]
    public void Plan_UnsupportedTarget_IsReportedBeforeTheProjectIsRead()
    {
        Directory.Delete(InProject(".aicontext"), recursive: true);

        var plan = Plan("nope");

        Assert.Contains("'nope'", ErrorWithCode(plan, DiagnosticCodes.UnsupportedTarget).Message);
        Assert.DoesNotContain(plan.Diagnostics, d => d.Code == DiagnosticCodes.MissingRequiredFile);
    }

    [Fact]
    public void Plan_All_UsesConfiguredTargetsInOrder()
    {
        WriteContext("""{ "targets": ["claude", "agents"] }""");

        var plan = Plan("all");

        Assert.Equal(["claude", "agents"], plan.Targets);
        Assert.Equal(["claude", "agents"], plan.Files.Select(f => f.Target));
    }

    [Fact]
    public void Plan_All_WithoutConfiguredTargets_UsesEveryDefaultTarget()
    {
        var plan = Plan("all");

        Assert.Equal(ExportTargets.DefaultNames, plan.Targets);
    }

    [Fact]
    public void Plan_UnsupportedLayout_ReportsAIC4005()
    {
        var plan = Plan("agents", layout: "wide");

        Assert.Empty(plan.Files);
        ErrorWithCode(plan, DiagnosticCodes.UnsupportedLayout);
    }

    [Fact]
    public void Plan_LayoutOverrideWinsOverContext()
    {
        WriteContext("""{ "layout": "single" }""");

        Assert.Equal(ExportLayout.Split, Plan("agents", layout: "split").Layout);
        Assert.Equal(ExportLayout.Single, Plan("agents").Layout);
    }

    [Fact]
    public void Plan_InvalidContext_ReturnsTheValidationErrorsAndNoFiles()
    {
        File.WriteAllText(InProject(".aicontext/context.json"), """{ "specVersion": "999.0.0", "project": { "name": "demo" } }""");

        var plan = Plan("agents");

        Assert.Empty(plan.Files);
        Assert.Equal(".aicontext/context.json#/specVersion", ErrorWithCode(plan, DiagnosticCodes.SchemaViolation).Location);
    }

    [Fact]
    public void Plan_ProjectWithOnlyWarnings_HasNoErrorsButReportsTheWarnings()
    {
        var plan = Plan("agents");

        Assert.False(plan.HasErrors);
        Assert.Contains(plan.Diagnostics, d => !d.IsError && d.Code == DiagnosticCodes.RecommendedFileMissing);
    }

    [Fact]
    public void Plan_HandWrittenFile_ReportsOverwriteConflictUnlessForced()
    {
        File.WriteAllText(InProject("AGENTS.md"), "hand written");

        var plan = Plan("agents");

        var conflict = ErrorWithCode(plan, DiagnosticCodes.OverwriteConflict);
        Assert.Equal("AGENTS.md", conflict.Location);
        Assert.Equal(ExportFileStatus.Conflict, Assert.Single(plan.Files).Status);

        var forced = Plan("agents", force: true);

        Assert.False(forced.HasErrors);
        Assert.Equal(ExportFileStatus.Updated, Assert.Single(forced.Files).Status);
    }

    [Fact]
    public void Plan_PathLeavingTheProjectThroughALink_ReportsAIC4102()
    {
        var link = InProject(".github");
        TestLinks.CreateDirectoryLink(link, _outside);
        _links.Add(link);

        var plan = Plan("copilot");

        var diagnostic = ErrorWithCode(plan, DiagnosticCodes.PathOutsideProject);
        Assert.Equal(".github/copilot-instructions.md", diagnostic.Location);
        Assert.Contains("outside the project", diagnostic.Message);
    }

    [Fact]
    public void Apply_PlanWithoutErrors_WritesTheFilesAndCountsThem()
    {
        var plan = Plan("all");

        var result = ExportService.Apply(plan);

        Assert.Equal(plan.ChangedCount, result.Changed);
        Assert.Equal(0, result.Unchanged);
        Assert.True(File.Exists(InProject("AGENTS.md")));
        Assert.True(File.Exists(InProject("CLAUDE.md")));
    }

    [Fact]
    public void Apply_AfterAnExport_FindsNothingToChange()
    {
        ExportService.Apply(Plan("agents"));

        var plan = Plan("agents");
        var result = ExportService.Apply(plan);

        Assert.Equal(0, plan.ChangedCount);
        Assert.Equal(new ExportResult(0, 1), result);
    }

    [Fact]
    public void Apply_PlanWithErrors_ThrowsAndWritesNothing()
    {
        File.WriteAllText(InProject("AGENTS.md"), "hand written");

        var plan = Plan("agents");

        Assert.Throws<InvalidOperationException>(() => ExportService.Apply(plan));
        Assert.Equal("hand written", File.ReadAllText(InProject("AGENTS.md")));
    }

    [Fact]
    public void Apply_PlanWithOneConflict_WritesNoneOfTheOtherFiles()
    {
        File.WriteAllText(InProject("CLAUDE.md"), "hand written");

        var plan = Plan("all");

        Assert.Throws<InvalidOperationException>(() => ExportService.Apply(plan));
        Assert.False(File.Exists(InProject("AGENTS.md")));
    }

    [Fact]
    public void Apply_FileCreatedAfterPlanning_RefusesAndKeepsTheHandWrittenFile()
    {
        var plan = Plan("agents");
        File.WriteAllText(InProject("AGENTS.md"), "hand written");

        var result = ExportService.Apply(plan);

        var problem = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCodes.StaleExportPlan, problem.Code);
        Assert.Equal("AGENTS.md", problem.Location);
        Assert.Equal(0, result.Changed);
        Assert.Equal("hand written", File.ReadAllText(InProject("AGENTS.md")));
    }

    [Fact]
    public void Apply_OneFileStale_WritesNoneOfTheOtherFiles()
    {
        var plan = Plan("all");
        File.WriteAllText(InProject("CLAUDE.md"), "hand written");

        var result = ExportService.Apply(plan);

        Assert.True(result.HasErrors);
        Assert.False(File.Exists(InProject("AGENTS.md")));
        Assert.Equal("hand written", File.ReadAllText(InProject("CLAUDE.md")));
    }

    [Fact]
    public void Apply_GeneratedFileEditedAfterPlanning_RefusesTheUpdate()
    {
        ExportService.Apply(Plan("agents"));
        File.AppendAllText(InProject(".aicontext/README.md"), "\nA new line.\n");
        var plan = Plan("agents");
        Assert.Equal(ExportFileStatus.Updated, Assert.Single(plan.Files).Status);
        File.WriteAllText(InProject("AGENTS.md"), "hand written");

        var result = ExportService.Apply(plan);

        Assert.Equal(DiagnosticCodes.StaleExportPlan, Assert.Single(result.Diagnostics).Code);
        Assert.Equal("hand written", File.ReadAllText(InProject("AGENTS.md")));
    }

    [Fact]
    public void Apply_OrphanReplacedAfterPlanning_DoesNotDeleteIt()
    {
        var generated = $"<!-- {ContextExporter.GeneratedMarker} --target agents -->\n# Old\n";
        Directory.CreateDirectory(InProject("docs"));
        File.WriteAllText(InProject("docs/AGENTS.md"), generated);
        var plan = Plan("agents");
        Assert.Contains(plan.Files, f => f.Status == ExportFileStatus.Deleted);
        File.WriteAllText(InProject("docs/AGENTS.md"), "hand written");

        var result = ExportService.Apply(plan);

        Assert.Equal(DiagnosticCodes.StaleExportPlan, Assert.Single(result.Diagnostics).Code);
        Assert.Equal("hand written", File.ReadAllText(InProject("docs/AGENTS.md")));
        Assert.False(File.Exists(InProject("AGENTS.md")));
    }

    [Fact]
    public void Apply_DirectoryBecameLinkOutsideAfterPlanning_WritesNothingOutside()
    {
        var plan = Plan("copilot");
        Assert.False(plan.HasErrors);
        TestLinks.CreateDirectoryLink(InProject(".github"), _outside);
        _links.Add(InProject(".github"));

        var result = ExportService.Apply(plan);

        Assert.Equal(DiagnosticCodes.PathOutsideProject, Assert.Single(result.Diagnostics).Code);
        Assert.Empty(Directory.GetFileSystemEntries(_outside));
    }

    [Fact]
    public void Apply_FileAppearsJustBeforeWriting_ExclusiveCreateRefusesToOverwrite()
    {
        var path = InProject("AGENTS.md");
        File.WriteAllText(path, "hand written");
        var plan = new ExportFilePlan("agents", path, "generated", ExportFileStatus.Created);

        Assert.Throws<StaleExportPlanException>(() => ExportWriter.Apply(plan));
        Assert.Equal("hand written", File.ReadAllText(path));
    }

    [Fact]
    public void Status_ReportsLayoutTargetsAndSyncState()
    {
        WriteContext("""{ "targets": ["agents", "claude"], "layout": "split" }""");
        ExportService.Apply(Plan("agents", "split"));

        var report = ExportService.Status(_project);

        Assert.False(report.HasErrors);
        Assert.Equal(ExportLayout.Split, report.Layout);
        Assert.Equal("demo", report.Context!.Context.ProjectName);
        Assert.Equal(["agents", "claude"], report.Targets.Select(t => t.Target));
        Assert.Equal(SyncState.InSync, report.Targets[0].State);
        Assert.Equal(SyncState.NotExported, report.Targets[1].State);
        Assert.Null(report.NestedAgentsPlan);
    }

    [Fact]
    public void Status_LayoutOverrideWinsOverContext()
    {
        WriteContext("""{ "layout": "split" }""");

        Assert.Equal(ExportLayout.Single, ExportService.Status(_project, "single").Layout);
    }

    [Fact]
    public void Status_NestedAgentsInSplitLayout_ReturnsThePlan()
    {
        WriteContext("""{ "targets": ["agents"], "layout": "split", "nestedAgents": true }""");
        Directory.CreateDirectory(InProject("pkg"));
        Directory.CreateDirectory(InProject(".aicontext/rules"));
        File.WriteAllText(InProject(".aicontext/rules/pkg.md"), "---\npaths: [\"pkg/**\"]\n---\nBody.\n");

        var plan = ExportService.Status(_project).NestedAgentsPlan;

        Assert.NotNull(plan);
        Assert.Equal(["pkg"], plan.ByDirectory.Keys);
    }

    [Fact]
    public void Status_InvalidContext_ReturnsErrorsInsteadOfStatuses()
    {
        File.WriteAllText(InProject(".aicontext/context.json"), "[1, 2, 3]");

        var report = ExportService.Status(_project);

        Assert.True(report.HasErrors);
        Assert.Null(report.Context);
        Assert.Empty(report.Targets);
        Assert.Contains(report.Diagnostics, d => d.Code == DiagnosticCodes.SchemaViolation);
    }

    [Fact]
    public void Status_UnsupportedLayout_ReportsAIC4005()
    {
        var report = ExportService.Status(_project, "wide");

        Assert.Single(report.Diagnostics, d => d.IsError && d.Code == DiagnosticCodes.UnsupportedLayout);
    }

    [Fact]
    public void Status_TargetsThatAreReadTogether_ReportTheOverlap()
    {
        WriteContext("""{ "targets": ["agents", "copilot"] }""");

        var overlap = Assert.Single(ExportService.Status(_project).Overlaps);

        Assert.Equal(["copilot", "agents"], overlap.Targets);
    }

    [Fact]
    public void Status_SingleTarget_ReportsNoOverlap()
    {
        WriteContext("""{ "targets": ["agents"] }""");

        Assert.Empty(ExportService.Status(_project).Overlaps);
    }
}
