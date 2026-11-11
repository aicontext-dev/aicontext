using AIContext;
using Xunit;

namespace AIContext.Tests;

// F03: in the VS Code Local harness a `description` can load an instructions file by relevance, separately from
// `applyTo` and not as a condition on it (custom instructions reference and "Use custom instructions", checked
// 2026-11-11; the tools were not run). A scoped rule exported with both would therefore be loaded for files outside the
// scope its author declared. A scoped Copilot rule has `applyTo` alone and keeps the description as text in its body.
// Always-on and on-demand rules, the other tools and the single layout are not touched by this.
public sealed class CopilotScopedRuleTests : IDisposable
{
    private const string RuleFile = ".github/instructions/aicontext-ui.instructions.md";

    private readonly string _project = Path.Combine(Path.GetTempPath(), "aicontext-tests-" + Guid.NewGuid().ToString("N"));

    public CopilotScopedRuleTests()
    {
        Directory.CreateDirectory(_project);
    }

    public void Dispose() => Directory.Delete(_project, recursive: true);

    private static ExportContext Context(params RuleDocument[] rules) =>
        new("demo", "# Project: demo\n\nSummary.", null, null, null, "Constraint.", null, rules);

    private static IReadOnlyList<ExportOutput> Render(string target, ExportLayout layout, params RuleDocument[] rules) =>
        ExportTargets.Find(target)!.Render(Context(rules), layout);

    private static string RuleContent(IReadOnlyList<ExportOutput> outputs, string path) =>
        outputs.Single(o => o.RelativePath == path).Content;

    private static RuleDocument Scoped(string? description = "UI conventions") =>
        new("ui", description, ["src/ui/**"], "Use shared components.");

    [Fact]
    public void Render_ScopedRuleWithDescription_HasApplyToAloneAndNoDescriptionField()
    {
        var content = RuleContent(Render("copilot", ExportLayout.Split, Scoped()), RuleFile);

        Assert.StartsWith("---\napplyTo: \"src/ui/**\"\n---\n", content);
        var frontmatter = Frontmatter.Parse(content);
        Assert.Null(frontmatter.Error);
        Assert.Equal(["applyTo"], frontmatter.Values.Keys);
    }

    [Fact]
    public void Render_ScopedRuleWithDescription_KeepsTheDescriptionAsTextBeforeTheBody()
    {
        var content = RuleContent(Render("copilot", ExportLayout.Split, Scoped("UI conventions for React components")), RuleFile);

        var title = content.IndexOf("# Rule: ui\n", StringComparison.Ordinal);
        var description = content.IndexOf("Description: UI conventions for React components\n", StringComparison.Ordinal);
        var body = content.IndexOf("Use shared components.", StringComparison.Ordinal);
        Assert.True(title >= 0 && description > title && body > description, content);
    }

    [Fact]
    public void Render_ScopedRuleWithoutDescription_HasNoDescriptionText()
    {
        var content = RuleContent(Render("copilot", ExportLayout.Split, Scoped(null)), RuleFile);

        Assert.StartsWith("---\napplyTo: \"src/ui/**\"\n---\n", content);
        Assert.DoesNotContain("Description:", content);
        Assert.DoesNotContain("description:", content);
    }

    [Fact]
    public void Render_ScopedRuleWithSeveralPatterns_KeepsAllOfThemInApplyTo()
    {
        var rule = new RuleDocument("ui", "UI conventions", ["src/ui/**", "**/*.tsx"], "Use shared components.");

        var content = RuleContent(Render("copilot", ExportLayout.Split, rule), RuleFile);

        Assert.StartsWith("---\napplyTo: \"src/ui/**,**/*.tsx\"\n---\n", content);
    }

    [Fact]
    public void Render_AlwaysOnRule_StaysInTheMainFileWithoutAnyScope()
    {
        var rule = new RuleDocument("style", null, [], "Format code.");

        var outputs = Render("copilot", ExportLayout.Split, rule);

        var main = Assert.Single(outputs);
        Assert.Equal(".github/copilot-instructions.md", main.RelativePath);
        Assert.Contains("## Rule: style", main.Content);
        Assert.Contains("Format code.", main.Content);
        Assert.DoesNotContain("applyTo", main.Content);
        Assert.DoesNotContain("Applies to:", main.Content);
        Assert.DoesNotContain("Use when:", main.Content);
    }

    // The documented fallback of SPEC 6.4: Copilot has no verified description mechanism, so on-demand content stays
    // inline with its description as a "Use when" line. This is not changed, and there is still no instructions file
    // that carries a description as a field.
    [Fact]
    public void Render_OnDemandRule_StaysInTheMainFileWithItsUseWhenLine()
    {
        var rule = new RuleDocument("api", "API design", [], "Return JSON envelopes.");

        var outputs = Render("copilot", ExportLayout.Split, rule);

        var main = Assert.Single(outputs);
        Assert.Contains("## Rule: api", main.Content);
        Assert.Contains("Use when: API design", main.Content);
        Assert.Contains("Return JSON envelopes.", main.Content);
        Assert.DoesNotContain("applyTo", main.Content);
        Assert.DoesNotContain(outputs, o => o.RelativePath.StartsWith(".github/instructions/", StringComparison.Ordinal));
    }

    [Fact]
    public void Render_ScopedAlwaysOnAndOnDemandRulesTogether_EachGetsItsOwnPlace()
    {
        var outputs = Render(
            "copilot",
            ExportLayout.Split,
            Scoped(),
            new RuleDocument("style", null, [], "Format code."),
            new RuleDocument("api", "API design", [], "Return JSON envelopes."));

        Assert.Equal([".github/copilot-instructions.md", RuleFile], outputs.Select(o => o.RelativePath));
        var main = outputs[0].Content;
        Assert.Contains("Format code.", main);
        Assert.Contains("Use when: API design", main);
        Assert.DoesNotContain("Use shared components.", main);
        Assert.DoesNotContain("API design", outputs[1].Content);
    }

    [Fact]
    public void Render_SingleLayout_StatesTheScopeAsTextAndWritesNoInstructionsFile()
    {
        var outputs = Render("copilot", ExportLayout.Single, Scoped());

        var main = Assert.Single(outputs);
        Assert.Contains("Applies to: `src/ui/**`", main.Content);
        Assert.DoesNotContain("applyTo", main.Content);
    }

    [Fact]
    public void Render_OtherToolsKeepTheirDescriptionOnAScopedRule()
    {
        var cursor = RuleContent(Render("cursor", ExportLayout.Split, Scoped()), ".cursor/rules/aicontext-ui.mdc");
        var windsurf = RuleContent(Render("windsurf", ExportLayout.Split, Scoped()), ".windsurf/rules/aicontext-ui.md");
        var devin = RuleContent(Render("devin", ExportLayout.Split, Scoped()), ".devin/rules/aicontext-ui.md");
        var claude = RuleContent(Render("claude", ExportLayout.Split, Scoped()), ".claude/rules/aicontext-ui.md");

        Assert.Contains("description: \"UI conventions\"\nglobs: src/ui/**\n", cursor);
        Assert.StartsWith("---\ntrigger: glob\nglobs: \"src/ui/**\"\ndescription: \"UI conventions\"\n---\n", windsurf);
        Assert.StartsWith("---\ntrigger: glob\nglobs: \"src/ui/**\"\ndescription: \"UI conventions\"\n---\n", devin);
        Assert.StartsWith("---\npaths:\n  - \"src/ui/**\"\n---\n", claude);
        Assert.DoesNotContain("UI conventions", claude);
    }

    [Fact]
    public void Export_FileGeneratedByAnEarlierVersion_IsUpdatedToTheScopedForm()
    {
        var init = ProjectInitializer.Initialize(_project, "demo", InitProfile.Core, ContextSchema.CanonicalUrl, "0.1.0", force: false);
        Assert.Empty(init.Conflicts);
        File.WriteAllText(
            Path.Combine(_project, ".aicontext", "context.json"),
            """{ "specVersion": "0.1.0", "project": { "name": "demo" }, "exports": { "targets": ["copilot"], "layout": "split" } }""");
        Directory.CreateDirectory(Path.Combine(_project, ".aicontext", "rules"));
        File.WriteAllText(
            Path.Combine(_project, ".aicontext", "rules", "ui.md"),
            "---\ndescription: UI conventions\npaths: [\"src/ui/**\"]\n---\nUse shared components.\n");
        var earlier = Path.Combine(_project, ".github", "instructions", "aicontext-ui.instructions.md");
        Directory.CreateDirectory(Path.GetDirectoryName(earlier)!);
        File.WriteAllText(
            earlier,
            $"---\napplyTo: \"src/ui/**\"\ndescription: \"UI conventions\"\n---\n\n<!-- {ContextExporter.GeneratedMarker} --target copilot -->\n\n# Rule: ui\n\nUse shared components.\n");

        var plan = ExportService.Plan(new ExportRequest(_project, "all"));
        Assert.False(plan.HasErrors);
        Assert.Equal(ExportFileStatus.Updated, plan.Files.Single(f => f.Path == earlier).Status);
        Assert.False(ExportService.Apply(plan).HasErrors);

        var written = File.ReadAllText(earlier);
        Assert.StartsWith("---\napplyTo: \"src/ui/**\"\n---\n", written);
        Assert.Contains("Description: UI conventions\n", written);
        Assert.Equal(ExportFileStatus.Unchanged, ExportService.Plan(new ExportRequest(_project, "all")).Files.Single(f => f.Path == earlier).Status);
    }
}
