using AIContext;
using AIContext.Cli;
using Xunit;

namespace AIContext.Tests;

// D17: generated files of a target that was dropped from exports.targets stay on disk and are reported, never deleted.
public sealed class OrphanedTargetExportTests : IDisposable
{
    private readonly string _project = Path.Combine(Path.GetTempPath(), "aicontext-tests-" + Guid.NewGuid().ToString("N"));

    public OrphanedTargetExportTests()
    {
        Directory.CreateDirectory(_project);
        var init = ProjectInitializer.Initialize(_project, "demo", InitProfile.Core, ContextSchema.CanonicalUrl, "0.1.0", force: false);
        Assert.Empty(init.Conflicts);
    }

    public void Dispose()
    {
        Directory.Delete(_project, recursive: true);
    }

    private string InProject(string relative) =>
        Path.Combine(_project, relative.Replace('/', Path.DirectorySeparatorChar));

    private void WriteContext(string exportsJson) =>
        File.WriteAllText(
            InProject(".aicontext/context.json"),
            "{ \"specVersion\": \"0.1.0\", \"project\": { \"name\": \"demo\" }, \"exports\": " + exportsJson + " }");

    private void ExportAllThenKeepOnly(string targetsJson, string extraExports = "")
    {
        WriteContext("{ \"targets\": [\"agents\", \"claude\", \"copilot\", \"cursor\"]" + extraExports + " }");
        Assert.False(ExportService.Apply(ExportService.Plan(new ExportRequest(_project, "all"))).HasErrors);
        WriteContext("{ \"targets\": " + targetsJson + extraExports + " }");
    }

    private ExportPlan Plan(string? target = "all") => ExportService.Plan(new ExportRequest(_project, target));

    private static IReadOnlyList<Diagnostic> Orphans(IEnumerable<Diagnostic> diagnostics) =>
        diagnostics.Where(d => d.Code == DiagnosticCodes.OrphanedTargetExport).ToList();

    [Fact]
    public void Plan_TargetDroppedFromConfiguration_WarnsAboutItsGeneratedFile()
    {
        ExportAllThenKeepOnly("[\"copilot\"]");

        var plan = Plan();

        Assert.False(plan.HasErrors);
        var warning = Assert.Single(Orphans(plan.Diagnostics), d => d.Location == "AGENTS.md");
        Assert.False(warning.IsError);
        Assert.Contains("Target 'agents'", warning.Message);
        Assert.Contains("exports.targets", warning.Message);
        Assert.Contains(Orphans(plan.Diagnostics), d => d.Location == "CLAUDE.md");
        Assert.DoesNotContain(Orphans(plan.Diagnostics), d => d.Message.Contains("Target 'copilot'", StringComparison.Ordinal));
        Assert.True(File.Exists(InProject("AGENTS.md")));
    }

    [Fact]
    public void Plan_WarningNamesTheToolsThatAlsoReadTheFile()
    {
        ExportAllThenKeepOnly("[\"copilot\"]");

        var claude = Assert.Single(Orphans(ExportService.Plan(new ExportRequest(_project, "all")).Diagnostics), d => d.Location == "CLAUDE.md");

        Assert.Contains("also read by Copilot (VS Code)", claude.Message);
    }

    [Fact]
    public void Plan_RuleFilesOfDroppedTarget_AreListedInOneWarning()
    {
        ExportAllThenKeepOnly("[\"agents\"]", ", \"layout\": \"split\"");
        Directory.CreateDirectory(InProject(".aicontext/rules"));
        File.WriteAllText(InProject(".aicontext/rules/api.md"), "---\npaths: [\"src/**\"]\n---\nBody.\n");
        // Generated rule files are only written for the exported targets, so put one there as an earlier export would have.
        Directory.CreateDirectory(InProject(".cursor/rules"));
        File.WriteAllText(
            InProject(".cursor/rules/aicontext-api.mdc"),
            $"<!-- {ContextExporter.GeneratedMarker} --target cursor -->\nold\n");

        var warning = Assert.Single(Orphans(Plan().Diagnostics), d => d.Message.Contains("Target 'cursor'", StringComparison.Ordinal));

        Assert.Contains(".cursor/rules/aicontext-api.mdc", warning.Message);
        Assert.Contains(".cursor/rules/project.mdc", warning.Message);
    }

    [Fact]
    public void Plan_NestedAgentsFileOfDroppedAgentsTarget_IsReported()
    {
        ExportAllThenKeepOnly("[\"claude\"]");
        Directory.CreateDirectory(InProject("docs"));
        File.WriteAllText(InProject("docs/AGENTS.md"), $"<!-- {ContextExporter.GeneratedMarker} --target agents -->\nnested\n");

        var warning = Assert.Single(Orphans(Plan().Diagnostics), d => d.Message.Contains("Target 'agents'", StringComparison.Ordinal));

        Assert.Contains("docs/AGENTS.md", warning.Message);
    }

    [Fact]
    public void Plan_HandWrittenFileOfDroppedTarget_IsNotReported()
    {
        WriteContext("""{ "targets": ["copilot"] }""");
        File.WriteAllText(InProject("AGENTS.md"), "hand written");

        Assert.Empty(Orphans(Plan().Diagnostics));
    }

    [Fact]
    public void Plan_ExplicitSingleTarget_DoesNotWarn()
    {
        ExportAllThenKeepOnly("[\"copilot\"]");

        Assert.Empty(Orphans(Plan("copilot").Diagnostics));
    }

    [Fact]
    public void Plan_DefaultTargetAll_Warns()
    {
        ExportAllThenKeepOnly("[\"copilot\"]", ", \"defaultTarget\": \"all\"");

        Assert.NotEmpty(Orphans(Plan(target: null).Diagnostics));
    }

    [Fact]
    public void Plan_WarningNeverBlocksOrChangesTheResult()
    {
        ExportAllThenKeepOnly("[\"copilot\"]");

        var plan = Plan();

        Assert.False(plan.HasErrors);
        Assert.Equal(0, plan.ChangedCount);
        Assert.False(ExportService.Apply(plan).HasErrors);
        Assert.True(File.Exists(InProject("CLAUDE.md")));
    }

    [Fact]
    public void Status_TargetDroppedFromConfiguration_ReportsTheSameWarning()
    {
        ExportAllThenKeepOnly("[\"copilot\"]");

        var report = ExportService.Status(_project);

        Assert.False(report.HasErrors);
        Assert.Contains(Orphans(report.Diagnostics), d => d.Location == "AGENTS.md");
    }

    [Fact]
    public void Status_AllGeneratedTargetsStillConfigured_ReportsNothing()
    {
        ExportAllThenKeepOnly("[\"agents\", \"claude\", \"copilot\", \"cursor\"]");

        Assert.Empty(Orphans(ExportService.Status(_project).Diagnostics));
    }

    [Fact]
    public void Run_CheckAfterTargetWasDropped_WarnsAndStillSucceeds()
    {
        ExportAllThenKeepOnly("[\"copilot\"]");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = CliApp.Run(["export", "--target", "all", "--check"], output, error, _project);

        Assert.Equal(0, exitCode);
        Assert.Contains("WARNING: Target 'agents' is not exported", output.ToString());
        Assert.Contains("Export is up to date.", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public void Run_StatusAfterTargetWasDropped_PrintsTheWarning()
    {
        ExportAllThenKeepOnly("[\"copilot\"]");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = CliApp.Run(["status"], output, error, _project);

        Assert.Equal(0, exitCode);
        Assert.Contains("WARNING: Target 'claude' is not exported", output.ToString());
    }
}
