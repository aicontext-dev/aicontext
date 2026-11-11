using AIContext;
using Xunit;

namespace AIContext.Tests;

// D11 (a directory with its own .aicontext/ is a separate project) and D12 (export never writes or deletes
// through a link that leaves the project).
public sealed class ExportSafetyTests : IDisposable
{
    private static readonly ExportTarget Agents = ExportTargets.Find("agents")!;
    private static readonly ExportTarget Copilot = ExportTargets.Find("copilot")!;
    private static readonly string GeneratedFile =
        $"<!-- {ContextExporter.GeneratedMarker} --target agents -->\n# Rules for pkg\n";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "aicontext-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _project;
    private readonly string _outside;
    private readonly List<string> _links = [];

    public ExportSafetyTests()
    {
        _project = Path.Combine(_root, "project");
        _outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(_project);
        Directory.CreateDirectory(_outside);
    }

    public void Dispose()
    {
        foreach (var link in Enumerable.Reverse(_links))
        {
            TestLinks.Remove(link);
        }

        Directory.Delete(_root, recursive: true);
    }

    private static ExportContext Context(params RuleDocument[] rules) =>
        new("demo", "# Project: demo\n\nSummary.", null, null, null, "Constraint.", null, rules);

    private string InProject(string relative) =>
        Path.Combine(_project, relative.Replace('/', Path.DirectorySeparatorChar));

    private void Write(string relative, string content)
    {
        var path = InProject(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private string LinkInProject(string relative, string target)
    {
        var link = InProject(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        TestLinks.CreateDirectoryLink(link, target);
        _links.Add(link);
        return link;
    }

    private IReadOnlyList<ExportFilePlan> Plan(ExportTarget target, ExportContext context, ExportLayout layout = ExportLayout.Single, bool nested = false, string? basePath = null) =>
        ExportWriter.PlanTarget(target, basePath ?? _project, context, layout, force: false, new RenderOptions(basePath ?? _project, nested));

    private static ExportFilePlan PlanFor(IEnumerable<ExportFilePlan> plans, string pathSuffix) =>
        Assert.Single(plans, p => p.Path.Replace('\\', '/').EndsWith(pathSuffix, StringComparison.Ordinal));

    private static void ApplyAll(IEnumerable<ExportFilePlan> plans)
    {
        foreach (var plan in plans)
        {
            ExportWriter.Apply(plan);
        }
    }

    [Fact]
    public void PlanTarget_DirectoryWithOwnAicontext_KeepsItsGeneratedAgentsFile()
    {
        Directory.CreateDirectory(InProject("pkg/child/.aicontext"));
        Write("pkg/child/AGENTS.md", GeneratedFile);

        var plans = Plan(Agents, Context());

        Assert.DoesNotContain(plans, p => p.Path.Contains("child", StringComparison.Ordinal));
    }

    [Fact]
    public void PlanTarget_ProjectBoundaryAboveGeneratedFile_KeepsTheWholeSubtree()
    {
        Directory.CreateDirectory(InProject("a/b/.aicontext"));
        Write("a/b/c/AGENTS.md", GeneratedFile);

        var plans = Plan(Agents, Context());

        Assert.DoesNotContain(plans, p => p.Status == ExportFileStatus.Deleted);
    }

    [Fact]
    public void PlanTarget_GeneratedNestedFileWithoutOwnProject_IsStillRemovedAsOrphan()
    {
        Write("docs/AGENTS.md", GeneratedFile);

        var plans = Plan(Agents, Context());

        Assert.Equal(ExportFileStatus.Deleted, PlanFor(plans, "docs/AGENTS.md").Status);
    }

    [Fact]
    public void Plan_RuleScopedToSeparateProject_StaysInRootFile()
    {
        Directory.CreateDirectory(InProject("pkg/child/.aicontext"));
        var rule = new RuleDocument("child", null, ["pkg/child/**"], "Body.");

        var plan = NestedAgentsPlanner.Plan([rule], _project);

        Assert.Empty(plan.ByDirectory);
        Assert.Contains("separate AIContext project", Assert.Single(plan.Kept).Reason);
    }

    [Fact]
    public void Plan_RuleScopedBelowSeparateProject_StaysInRootFile()
    {
        Directory.CreateDirectory(InProject("pkg/child/.aicontext"));
        Directory.CreateDirectory(InProject("pkg/child/src"));
        var rule = new RuleDocument("child-src", null, ["pkg/child/src/**"], "Body.");

        var plan = NestedAgentsPlanner.Plan([rule], _project);

        Assert.Empty(plan.ByDirectory);
        Assert.Contains("'pkg/child' has its own .aicontext/", Assert.Single(plan.Kept).Reason);
    }

    [Fact]
    public void PlanTarget_NestedDirectoryIsLinkOutsideProject_IsBlockedAndNothingIsWritten()
    {
        LinkInProject("linked", _outside);
        var rule = new RuleDocument("linked", null, ["linked/**"], "Body.");

        var plans = Plan(Agents, Context(rule), ExportLayout.Split, nested: true);
        ApplyAll(plans);

        var blocked = PlanFor(plans, "linked/AGENTS.md");
        Assert.Equal(ExportFileStatus.Blocked, blocked.Status);
        Assert.Contains("outside the project", blocked.Reason);
        Assert.Empty(Directory.GetFileSystemEntries(_outside));
    }

    [Fact]
    public void PlanTarget_OutputDirectoryIsLinkOutsideProject_IsBlockedAndNothingIsWritten()
    {
        LinkInProject(".github", _outside);

        var plans = Plan(Copilot, Context());
        ApplyAll(plans);

        Assert.Equal(ExportFileStatus.Blocked, PlanFor(plans, ".github/copilot-instructions.md").Status);
        Assert.Empty(Directory.GetFileSystemEntries(_outside));
    }

    [Fact]
    public void PlanTarget_ChainOfLinksEndingOutsideProject_IsBlocked()
    {
        var middle = Path.Combine(_root, "middle");
        TestLinks.CreateDirectoryLink(middle, _outside);
        _links.Add(middle);
        LinkInProject("hop", middle);
        var rule = new RuleDocument("hop", null, ["hop/**"], "Body.");

        var plans = Plan(Agents, Context(rule), ExportLayout.Split, nested: true);

        Assert.Equal(ExportFileStatus.Blocked, PlanFor(plans, "hop/AGENTS.md").Status);
    }

    [Fact]
    public void PlanTarget_LinkThatStaysInsideProject_IsAllowed()
    {
        Directory.CreateDirectory(InProject("real-docs"));
        LinkInProject("docs-link", InProject("real-docs"));
        var rule = new RuleDocument("docs", null, ["docs-link/**"], "Body.");

        var plans = Plan(Agents, Context(rule), ExportLayout.Split, nested: true);

        Assert.Equal(ExportFileStatus.Created, PlanFor(plans, "docs-link/AGENTS.md").Status);
        Assert.DoesNotContain(plans, p => p.Status == ExportFileStatus.Blocked);
    }

    [Fact]
    public void PlanTarget_ProjectReachedThroughLink_IsNotBlocked()
    {
        var entry = Path.Combine(_root, "entry");
        TestLinks.CreateDirectoryLink(entry, _project);
        _links.Add(entry);

        var plans = Plan(Agents, Context(), basePath: entry);

        Assert.Equal(ExportFileStatus.Created, Assert.Single(plans).Status);
    }

    [LinkFact(LinkCapability.FileSymlink)]
    public void PlanTarget_ExistingFileIsLinkOutsideProject_IsBlockedWithoutTouchingTheTarget()
    {
        var secret = Path.Combine(_outside, "secret.md");
        File.WriteAllText(secret, "private");
        var link = InProject("AGENTS.md");
        TestLinks.CreateFileLink(link, secret);
        _links.Add(link);

        var plans = Plan(Agents, Context());
        ApplyAll(plans);

        Assert.Equal(ExportFileStatus.Blocked, Assert.Single(plans).Status);
        Assert.Equal("private", File.ReadAllText(secret));
    }

    [LinkFact(LinkCapability.FileSymlink)]
    public void PlanTarget_DanglingFileLinkLeavingProject_IsBlocked()
    {
        var target = Path.Combine(_outside, "not-yet.md");
        var link = InProject("AGENTS.md");
        TestLinks.CreateFileLink(link, target);
        _links.Add(link);

        var plans = Plan(Agents, Context());
        ApplyAll(plans);

        Assert.Equal(ExportFileStatus.Blocked, Assert.Single(plans).Status);
        Assert.False(File.Exists(target));
    }

    [Fact]
    public void PlanTarget_GeneratedRuleFileBehindLinkedRuleDirectory_IsNeverDeleted()
    {
        var orphan = Path.Combine(_outside, "aicontext-old.instructions.md");
        File.WriteAllText(orphan, $"<!-- {ContextExporter.GeneratedMarker} --target copilot -->\nold\n");
        LinkInProject(".github/instructions", _outside);

        var plans = Plan(Copilot, Context(), ExportLayout.Split);
        ApplyAll(plans);

        Assert.Equal(ExportFileStatus.Blocked, PlanFor(plans, "aicontext-old.instructions.md").Status);
        Assert.True(File.Exists(orphan));
    }

    [Fact]
    public void Plan_RuleScopedToLinkIntoSeparateProject_StaysInRootFile()
    {
        Directory.CreateDirectory(InProject("pkg/child/.aicontext"));
        Directory.CreateDirectory(InProject("pkg/child/src"));
        LinkInProject("alias", InProject("pkg/child/src"));
        var rule = new RuleDocument("alias", null, ["alias/**"], "Body.");

        var plan = NestedAgentsPlanner.Plan([rule], _project);

        Assert.Empty(plan.ByDirectory);
        Assert.Contains("'pkg/child' has its own .aicontext/", Assert.Single(plan.Kept).Reason);
    }

    [Fact]
    public void PlanTarget_OutputDirectoryIsLinkIntoSeparateProject_IsBlockedAndNothingIsWritten()
    {
        var nestedSource = InProject("pkg/child/src");
        Directory.CreateDirectory(InProject("pkg/child/.aicontext"));
        Directory.CreateDirectory(nestedSource);
        LinkInProject(".github", nestedSource);

        var plans = Plan(Copilot, Context());
        ApplyAll(plans);

        var blocked = PlanFor(plans, ".github/copilot-instructions.md");
        Assert.Equal(ExportFileStatus.Blocked, blocked.Status);
        Assert.Equal(DiagnosticCodes.PathInSeparateProject, blocked.ReasonCode);
        Assert.Contains("'pkg/child'", blocked.Reason);
        Assert.Empty(Directory.GetFileSystemEntries(nestedSource));
    }

    [Fact]
    public void PlanTarget_LinkDirectlyToSeparateProjectRoot_IsBlocked()
    {
        Directory.CreateDirectory(InProject("pkg/child/.aicontext"));
        LinkInProject(".github", InProject("pkg/child"));

        var plans = Plan(Copilot, Context());

        Assert.Equal(ExportFileStatus.Blocked, PlanFor(plans, ".github/copilot-instructions.md").Status);
    }

    [Fact]
    public void PlanTarget_GeneratedRuleFileBehindLinkIntoSeparateProject_IsNeverDeleted()
    {
        var nestedRules = InProject("pkg/child/rules");
        Directory.CreateDirectory(InProject("pkg/child/.aicontext"));
        Directory.CreateDirectory(nestedRules);
        var orphan = Path.Combine(nestedRules, "aicontext-old.instructions.md");
        File.WriteAllText(orphan, $"<!-- {ContextExporter.GeneratedMarker} --target copilot -->\nold\n");
        LinkInProject(".github/instructions", nestedRules);

        var plans = Plan(Copilot, Context(), ExportLayout.Split);
        ApplyAll(plans);

        Assert.Equal(ExportFileStatus.Blocked, PlanFor(plans, "aicontext-old.instructions.md").Status);
        Assert.True(File.Exists(orphan));
    }

    [Fact]
    public void PlanTarget_LinkToDirectoryWithoutOwnAicontext_StaysAllowed()
    {
        Directory.CreateDirectory(InProject("pkg/plain"));
        LinkInProject(".github", InProject("pkg/plain"));

        var plans = Plan(Copilot, Context());

        Assert.Equal(ExportFileStatus.Created, PlanFor(plans, ".github/copilot-instructions.md").Status);
    }

    [Fact]
    public void PlanTarget_ProjectThatIsItselfNestedInsideAnotherProject_IsNotBlocked()
    {
        var outer = Path.Combine(_root, "outer");
        var inner = Path.Combine(outer, "inner");
        Directory.CreateDirectory(Path.Combine(outer, ".aicontext"));
        Directory.CreateDirectory(inner);

        var plans = Plan(Agents, Context(), basePath: inner);

        Assert.Equal(ExportFileStatus.Created, Assert.Single(plans).Status);
    }

    [Fact]
    public void Compute_BlockedPath_ReportsBlockedState()
    {
        Directory.CreateDirectory(Path.Combine(_project, ".aicontext"));
        Write(".aicontext/README.md", "# Project: demo\n\nSummary.");
        Write(".aicontext/context.json", """{ "specVersion": "0.1.0", "project": { "name": "demo" } }""");
        LinkInProject(".github", _outside);

        var loaded = ContextLoader.Load(_project).Loaded!;
        var status = ExportStatus.Compute(loaded, ["copilot"], ExportLayout.Single).Single();

        Assert.Equal(SyncState.Blocked, status.State);
    }
}
