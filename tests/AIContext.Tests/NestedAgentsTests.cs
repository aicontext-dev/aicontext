using AIContext;
using Xunit;

namespace AIContext.Tests;

public sealed class NestedAgentsTests : IDisposable
{
    private readonly string _basePath = Path.Combine(Path.GetTempPath(), "aicontext-tests-" + Guid.NewGuid().ToString("N"));

    private static readonly ExportTarget Agents = ExportTargets.Find("agents")!;

    public NestedAgentsTests()
    {
        foreach (var directory in new[] { "src/ui", "src/api", "tests", "other" })
        {
            Directory.CreateDirectory(Path.Combine(_basePath, directory.Replace('/', Path.DirectorySeparatorChar)));
        }

        File.WriteAllText(Path.Combine(_basePath, "src", "ui", "button.tsx"), "export {}");
    }

    public void Dispose()
    {
        Directory.Delete(_basePath, recursive: true);
    }

    private static RuleDocument Rule(string name, params string[] paths) =>
        new(name, null, paths, $"Body of {name}.");

    private ExportContext Context(params RuleDocument[] rules) =>
        new("demo", "# Project: demo\n\nSummary.", null, null, null, "Constraint.", null, rules);

    private RenderOptions Nested(bool enabled = true) => new(_basePath, enabled);

    [Theory]
    [InlineData("src/ui/**")]
    [InlineData("src/ui/**/*")]
    [InlineData("src/ui/")]
    [InlineData("src/ui")]
    [InlineData("./src/ui/**")]
    public void Plan_MapsExactDirectoryScopes(string pattern)
    {
        var plan = NestedAgentsPlanner.Plan([Rule("ui", pattern)], _basePath);

        Assert.Equal(["src/ui"], plan.ByDirectory.Keys);
        Assert.Contains("ui", plan.NestedRuleNames);
        Assert.Empty(plan.Kept);
    }

    [Theory]
    [InlineData("**/*.tsx", "not a directory scope")]
    [InlineData("src/ui/**/*.tsx", "not a directory scope")]
    [InlineData("src/*/ui/**", "not a directory scope")]
    [InlineData("**", "not a directory scope")]
    [InlineData("../outside/**", "not a directory scope")]
    [InlineData("/abs/**", "not a directory scope")]
    [InlineData("C:/Windows/**", "not a directory scope")]
    [InlineData("C:", "not a directory scope")]
    [InlineData("missing/**", "does not exist")]
    [InlineData("src/ui/button.tsx", "is a file")]
    public void Plan_KeepsEverythingElseInTheRootFile(string pattern, string expectedReason)
    {
        var plan = NestedAgentsPlanner.Plan([Rule("r", pattern)], _basePath);

        Assert.Empty(plan.ByDirectory);
        var kept = Assert.Single(plan.Kept);
        Assert.Equal("r", kept.Name);
        Assert.Contains(expectedReason, kept.Reason);
    }

    [Fact]
    public void Plan_RuleWithOneUnmappablePattern_StaysEntirelyInRootFile()
    {
        var plan = NestedAgentsPlanner.Plan([Rule("mixed", "src/ui/**", "**/*.tsx")], _basePath);

        Assert.Empty(plan.ByDirectory);
        Assert.Single(plan.Kept);
    }

    [Fact]
    public void Plan_RuleWithSeveralDirectories_IsWrittenToEachAndRulesShareAFile()
    {
        var plan = NestedAgentsPlanner.Plan(
            [Rule("a", "src/ui/**", "src/api/**"), Rule("b", "src/ui/**")],
            _basePath);

        Assert.Equal(["src/api", "src/ui"], plan.ByDirectory.Keys);
        Assert.Equal(["a", "b"], plan.ByDirectory["src/ui"].Select(r => r.Name));
        Assert.Equal(["a"], plan.ByDirectory["src/api"].Select(r => r.Name));
    }

    [Fact]
    public void Render_WritesNestedFilesAndRemovesRulesFromRootFile()
    {
        var context = Context(Rule("ui", "src/ui/**"), Rule("types", "**/*.ts"));

        var outputs = Agents.Render(context, ExportLayout.Split, Nested());

        Assert.Equal(["AGENTS.md", "src/ui/AGENTS.md"], outputs.Select(o => o.RelativePath));
        var root = outputs[0].Content;
        Assert.DoesNotContain("Body of ui.", root);
        Assert.Contains("Body of types.", root);
        Assert.Contains("Applies to: `**/*.ts`", root);

        var nested = outputs[1].Content;
        Assert.True(ExportWriter.IsGenerated(nested));
        Assert.Contains("# Rules for src/ui", nested);
        Assert.Contains("## Rule: ui", nested);
        Assert.Contains("Body of ui.", nested);
        Assert.DoesNotContain("\r", nested);
    }

    [Theory]
    [InlineData(ExportLayout.Single, true)]
    [InlineData(ExportLayout.Split, false)]
    public void Render_WithoutSplitOrWithoutOption_KeepsEverythingInRootFile(ExportLayout layout, bool enabled)
    {
        var outputs = Agents.Render(Context(Rule("ui", "src/ui/**")), layout, Nested(enabled));

        var root = Assert.Single(outputs).Content;
        Assert.Contains("Body of ui.", root);
    }

    [Fact]
    public void Render_WithoutOptions_KeepsEverythingInRootFile()
    {
        var outputs = Agents.Render(Context(Rule("ui", "src/ui/**")), ExportLayout.Split);

        Assert.Single(outputs);
    }

    [Fact]
    public void Render_OtherTargetsAreNotAffected()
    {
        var cursor = ExportTargets.Find("cursor")!;

        var outputs = cursor.Render(Context(Rule("ui", "src/ui/**")), ExportLayout.Split, Nested());

        Assert.DoesNotContain(outputs, o => o.RelativePath.EndsWith("/AGENTS.md", StringComparison.Ordinal));
        Assert.Contains(outputs, o => o.RelativePath == ".cursor/rules/aicontext-ui.mdc");
    }

    [Fact]
    public void PlanTarget_RemovesOnlyOrphanedGeneratedNestedFiles()
    {
        var context = Context(Rule("ui", "src/ui/**"));
        foreach (var plan in ExportWriter.PlanTarget(Agents, _basePath, context, ExportLayout.Split, false, Nested()))
        {
            ExportWriter.Apply(plan);
        }

        var nestedPath = Path.Combine(_basePath, "src", "ui", "AGENTS.md");
        Assert.True(File.Exists(nestedPath));

        var handWritten = Path.Combine(_basePath, "other", "AGENTS.md");
        File.WriteAllText(handWritten, "my own notes");
        var hidden = Path.Combine(_basePath, "node_modules", "pkg");
        Directory.CreateDirectory(hidden);
        File.WriteAllText(Path.Combine(hidden, "AGENTS.md"), "<!-- Auto-generated by aicontext export --target agents -->");

        var second = ExportWriter.PlanTarget(Agents, _basePath, context, ExportLayout.Split, false, Nested());
        Assert.All(second, p => Assert.Equal(ExportFileStatus.Unchanged, p.Status));

        var disabled = ExportWriter.PlanTarget(Agents, _basePath, context, ExportLayout.Split, false, Nested(false));
        var deleted = Assert.Single(disabled, p => p.Status == ExportFileStatus.Deleted);
        Assert.Equal(nestedPath, deleted.Path);

        ExportWriter.Apply(deleted);
        Assert.False(File.Exists(nestedPath));
        Assert.True(File.Exists(handWritten));
        Assert.True(File.Exists(Path.Combine(hidden, "AGENTS.md")));
    }

    [Fact]
    public void ExistingHandWrittenNestedFile_IsAConflict()
    {
        File.WriteAllText(Path.Combine(_basePath, "src", "ui", "AGENTS.md"), "my own notes");

        var plans = ExportWriter.PlanTarget(Agents, _basePath, Context(Rule("ui", "src/ui/**")), ExportLayout.Split, false, Nested());

        Assert.Contains(plans, p => p.Status == ExportFileStatus.Conflict && p.Path.EndsWith("AGENTS.md", StringComparison.Ordinal) && p.Path.Contains("ui", StringComparison.Ordinal));
    }
}
