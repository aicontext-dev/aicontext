using System.Text.Json;
using AIContext;
using Xunit;

namespace AIContext.Tests;

public sealed class SplitExportTests : IDisposable
{
    private readonly string _basePath = Path.Combine(Path.GetTempPath(), "aicontext-tests-" + Guid.NewGuid().ToString("N"));

    private static readonly ExportContext Context = new(
        "sample",
        "# Project: sample\n\nShort summary.\n",
        "Architecture text.",
        "Decision text.",
        null,
        "Constraint text.",
        null,
        [
            new RuleDocument("ui", "UI conventions", ["src/ui/**", "**/*.tsx"], "Use shared components."),
            new RuleDocument("api", "API design", [], "Return JSON envelopes."),
            new RuleDocument("style", null, [], "Format code.")
        ]);

    public SplitExportTests()
    {
        Directory.CreateDirectory(_basePath);
    }

    public void Dispose()
    {
        Directory.Delete(_basePath, recursive: true);
    }

    private static IReadOnlyList<ExportOutput> Render(string target, ExportLayout layout = ExportLayout.Split) =>
        ExportTargets.Find(target)!.Render(Context, layout);

    private static string Content(IReadOnlyList<ExportOutput> outputs, string path) =>
        outputs.Single(o => o.RelativePath == path).Content;

    [Fact]
    public void Cursor_Split_KeepsOnlyAlwaysOnContentInMainFile()
    {
        var outputs = Render("cursor");

        Assert.Equal(
            [
                ".cursor/rules/project.mdc",
                ".cursor/rules/aicontext-architecture.mdc",
                ".cursor/rules/aicontext-decisions.mdc",
                ".cursor/rules/aicontext-api.mdc",
                ".cursor/rules/aicontext-ui.mdc"
            ],
            outputs.Select(o => o.RelativePath));

        var main = outputs[0].Content;
        Assert.Contains("alwaysApply: true", main);
        Assert.Contains("Constraint text.", main);
        Assert.Contains("## Rule: style", main);
        Assert.DoesNotContain("Architecture text.", main);
        Assert.DoesNotContain("Use shared components.", main);

        var ui = Content(outputs, ".cursor/rules/aicontext-ui.mdc");
        Assert.Contains("globs: \"src/ui/**,**/*.tsx\"", ui);
        Assert.Contains("description: \"UI conventions\"", ui);
        Assert.Contains("alwaysApply: false", ui);

        var api = Content(outputs, ".cursor/rules/aicontext-api.mdc");
        Assert.Contains("description: \"API design\"", api);
        Assert.DoesNotContain("globs:", api);

        var architecture = Content(outputs, ".cursor/rules/aicontext-architecture.mdc");
        Assert.Contains("description: \"Architecture and design rationale for sample\"", architecture);
        Assert.Contains("Architecture text.", architecture);
    }

    [Fact]
    public void Copilot_Split_SplitsOnlyPathScopedRules()
    {
        var outputs = Render("copilot");

        Assert.Equal(
            [".github/copilot-instructions.md", ".github/instructions/aicontext-ui.instructions.md"],
            outputs.Select(o => o.RelativePath));

        var main = outputs[0].Content;
        Assert.Contains("Architecture text.", main);
        Assert.Contains("Return JSON envelopes.", main);
        Assert.DoesNotContain("Use shared components.", main);

        var ui = outputs[1].Content;
        Assert.StartsWith("---\napplyTo: \"src/ui/**,**/*.tsx\"\n", ui);
    }

    [Fact]
    public void Claude_Split_WritesPathsAsYamlList()
    {
        var outputs = Render("claude");

        var ui = Content(outputs, ".claude/rules/aicontext-ui.md");
        Assert.StartsWith("---\npaths:\n  - \"src/ui/**\"\n  - \"**/*.tsx\"\n---\n", ui);
        Assert.Contains("Architecture text.", outputs[0].Content);
    }

    [Theory]
    [InlineData("windsurf", ".windsurf/rules")]
    [InlineData("devin", ".devin/rules")]
    public void WindsurfAndDevin_Split_UseTriggerFrontmatter(string target, string directory)
    {
        var outputs = Render(target);

        Assert.StartsWith("---\ntrigger: always_on\n---\n", outputs[0].Content);

        var ui = Content(outputs, $"{directory}/aicontext-ui.md");
        Assert.StartsWith("---\ntrigger: glob\nglobs: \"src/ui/**,**/*.tsx\"\n", ui);

        var api = Content(outputs, $"{directory}/aicontext-api.md");
        Assert.StartsWith("---\ntrigger: model_decision\ndescription: \"API design\"\n---\n", api);
    }

    [Fact]
    public void Devin_AlwaysDeclaresAlwaysOnTrigger_AndIsNotInDefaultTargets()
    {
        var main = Render("devin", ExportLayout.Single).Single().Content;

        Assert.StartsWith("---\ntrigger: always_on\n---\n", main);
        Assert.Contains("devin", ExportTargets.Names);
        Assert.DoesNotContain("devin", ExportTargets.DefaultNames);
        Assert.Contains("windsurf", ExportTargets.DefaultNames);
    }

    [Fact]
    public void Windsurf_Single_HasNoFrontmatter()
    {
        var main = Render("windsurf", ExportLayout.Single).Single().Content;

        Assert.StartsWith("<!-- Auto-generated", main);
    }

    [Theory]
    [InlineData("agents")]
    [InlineData("aider")]
    public void ToolsWithoutScoping_IgnoreSplit_AndKeepRulesAsSections(string target)
    {
        var outputs = Render(target);

        var main = Assert.Single(outputs).Content;
        Assert.Contains("## Rule: ui", main);
        Assert.Contains("Applies to: `src/ui/**`, `**/*.tsx`", main);
        Assert.Contains("Use when: API design", main);
        Assert.Contains("Use shared components.", main);
    }

    [Fact]
    public void Single_IncludesRulesInMainFile_ForEveryTarget()
    {
        foreach (var target in ExportTargets.All)
        {
            var main = Assert.Single(target.Render(Context, ExportLayout.Single)).Content;

            Assert.Contains("Use shared components.", main);
            Assert.Contains("Architecture text.", main);
        }
    }

    [Fact]
    public void Split_IsDeterministic_AndEveryFileCarriesTheGeneratedHeader()
    {
        foreach (var target in ExportTargets.All)
        {
            var first = Render(target.Name);
            var second = Render(target.Name);

            Assert.Equal(first, second);
            foreach (var output in first)
            {
                Assert.True(ExportWriter.IsGenerated(output.Content), output.RelativePath);
                Assert.DoesNotContain("\r", output.Content);
            }
        }
    }

    [Fact]
    public void RulesChangeTheSourceHash()
    {
        var withoutRules = Context with { Rules = null };

        Assert.NotEqual(ContextExporter.ComputeSourceHash(Context), ContextExporter.ComputeSourceHash(withoutRules));
    }

    [Fact]
    public void PlanTarget_RemovesOnlyOrphanedGeneratedRuleFiles()
    {
        var cursor = ExportTargets.Find("cursor")!;
        foreach (var plan in ExportWriter.PlanTarget(cursor, _basePath, Context, ExportLayout.Split, force: false))
        {
            ExportWriter.Apply(plan);
        }

        var rulesDirectory = Path.Combine(_basePath, ".cursor", "rules");
        File.WriteAllText(Path.Combine(rulesDirectory, "my-own.mdc"), "hand written");
        File.WriteAllText(Path.Combine(rulesDirectory, "aicontext-manual.mdc"), "no generated header");

        var plans = ExportWriter.PlanTarget(cursor, _basePath, Context, ExportLayout.Single, force: false);

        var deleted = plans.Where(p => p.Status == ExportFileStatus.Deleted).Select(p => Path.GetFileName(p.Path)).Order().ToArray();
        Assert.Equal(
            ["aicontext-api.mdc", "aicontext-architecture.mdc", "aicontext-decisions.mdc", "aicontext-ui.mdc"],
            deleted);

        foreach (var plan in plans)
        {
            ExportWriter.Apply(plan);
        }

        Assert.True(File.Exists(Path.Combine(rulesDirectory, "my-own.mdc")));
        Assert.True(File.Exists(Path.Combine(rulesDirectory, "aicontext-manual.mdc")));
        Assert.False(File.Exists(Path.Combine(rulesDirectory, "aicontext-ui.mdc")));
    }

    [Fact]
    public void PlanTarget_SecondRunIsUnchanged()
    {
        var copilot = ExportTargets.Find("copilot")!;
        foreach (var plan in ExportWriter.PlanTarget(copilot, _basePath, Context, ExportLayout.Split, force: false))
        {
            ExportWriter.Apply(plan);
        }

        var second = ExportWriter.PlanTarget(copilot, _basePath, Context, ExportLayout.Split, force: false);

        Assert.All(second, p => Assert.Equal(ExportFileStatus.Unchanged, p.Status));
    }

    [Theory]
    [InlineData("{\"exports\":{\"layout\":\"split\"}}", null, ExportLayout.Split, true)]
    [InlineData("{}", null, ExportLayout.Single, true)]
    [InlineData("{\"exports\":{\"layout\":\"split\"}}", "single", ExportLayout.Single, true)]
    [InlineData("{}", "split", ExportLayout.Split, true)]
    [InlineData("{\"exports\":{\"layout\":\"weird\"}}", null, ExportLayout.Single, false)]
    [InlineData("{}", "weird", ExportLayout.Single, false)]
    public void LayoutResolver_PrefersCommandLineOverConfiguration(string json, string? overrideValue, ExportLayout expected, bool ok)
    {
        using var document = JsonDocument.Parse(json);

        var (layout, error) = ExportLayoutResolver.Resolve(document.RootElement, overrideValue);

        Assert.Equal(expected, layout);
        Assert.Equal(ok, error is null);
    }
}
