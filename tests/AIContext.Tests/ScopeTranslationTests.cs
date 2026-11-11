using AIContext;
using AIContext.Cli;
using Xunit;

namespace AIContext.Tests;

// What a scoped rule becomes in each tool (SPEC 4.4.2 and 6.4). A translation is written only where the tool's
// documentation shows that it reads the pattern the way the source means it; otherwise the export is refused and
// nothing is written. The expected behavior of each tool below is what its documentation says (checked 2026-11-11,
// see ScopeTranslator); nothing here ran in the tools themselves.
public sealed class ScopeTranslationTests : IDisposable
{
    private const string Marker = "UI-BODY-MARKER";

    private static readonly string[] ScopedTargets = ["copilot", "cursor", "claude", "windsurf", "devin"];

    private readonly string _project = Path.Combine(Path.GetTempPath(), "aicontext-tests-" + Guid.NewGuid().ToString("N"));

    public ScopeTranslationTests()
    {
        Directory.CreateDirectory(_project);
        var init = ProjectInitializer.Initialize(_project, "demo", InitProfile.Core, ContextSchema.CanonicalUrl, "0.1.0", force: false);
        Assert.Empty(init.Conflicts);
    }

    public void Dispose() => Directory.Delete(_project, recursive: true);

    private string InProject(string relative) => Path.Combine(_project, relative.Replace('/', Path.DirectorySeparatorChar));

    private void Configure(string targets, string layout, params (string Name, string Content)[] rules)
    {
        File.WriteAllText(
            InProject(".aicontext/context.json"),
            $$"""{ "specVersion": "0.1.0", "project": { "name": "demo" }, "exports": { "targets": [{{targets}}], "layout": "{{layout}}" } }""");
        Directory.CreateDirectory(InProject(".aicontext/rules"));
        foreach (var (name, content) in rules)
        {
            File.WriteAllText(InProject($".aicontext/rules/{name}.md"), content);
        }
    }

    private static string ScopedRule(string paths) => $"---\npaths: {paths}\n---\n{Marker}\n";

    private ExportPlan Plan(string? target = "all", string? layout = null) =>
        ExportService.Plan(new ExportRequest(_project, target, layout));

    private IReadOnlyList<string> FilesOnDisk() =>
        Directory.EnumerateFiles(_project, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(_project, file))
            .Order(StringComparer.Ordinal)
            .ToList();

    private static ExportTarget Target(string name) => ExportTargets.Find(name)!;

    private static ScopeTranslation Translate(string target, params string[] items) =>
        ScopeTranslator.Translate(Target(target), "ui", items);

    // What the vendors' documentation says a tool matches, as matchers over the reference matcher. The pages are
    // named in ScopeTranslator. For Windsurf and Devin the documentation shows only a single pattern with a '/', so
    // the same reading is assumed for the one form that is translated for them.
    private static class Documented
    {
        // VS Code `applyTo`, Cursor `globs`: one comma-separated text; a pattern is read from the project root.
        public static bool CommaSeparatedText(string text, string path) =>
            text.Split(',').Select(p => p.Trim()).Any(p => PortableGlob.IsMatch(p, path));

        // Claude `paths`: a list; `*.md` matches at any depth and `/*.md` only in the root.
        public static bool ClaudeList(IEnumerable<string> items, string path) =>
            items.Any(item => item.StartsWith('/')
                ? PortableGlob.IsMatch(item[1..], path)
                : item.Contains('/', StringComparison.Ordinal)
                    ? PortableGlob.IsMatch(item, path)
                    : PortableGlob.IsMatch("**/" + item, path));
    }

    [Theory]
    // VS Code, custom instructions reference, "File patterns".
    [InlineData("*.py", "app.py", true)]
    [InlineData("*.py", "src/app.py", false)]
    [InlineData("src/*.py", "src/app.py", true)]
    [InlineData("src/*.py", "src/api/routes.py", false)]
    [InlineData("src/**/*.py", "src/app.py", true)]
    [InlineData("src/**/*.py", "src/api/routes.py", true)]
    [InlineData("src/**/*.py", "tests/test_app.py", false)]
    [InlineData("tests/**/*.py", "tests/test_app.py", true)]
    [InlineData("tests/**/*.py", "tests/unit/test_app.py", true)]
    [InlineData("tests/**/*.py", "src/app.py", false)]
    // Cursor rules, "Glob pattern examples".
    [InlineData("*.ts", "app.ts", true)]
    [InlineData("*.ts", "src/app.ts", false)]
    [InlineData("**/*.ts", "src/deep/app.ts", true)]
    [InlineData("src/**", "src/a/b.ts", true)]
    [InlineData("src/**/*.tsx", "src/a/b.tsx", true)]
    [InlineData("tailwind.config.*", "tailwind.config.ts", true)]
    public void ReferenceMatcher_AgreesWithTheExamplesVSCodeAndCursorDocument(string pattern, string path, bool expected)
    {
        Assert.Equal(expected, PortableGlob.IsMatch(pattern, path));
    }

    [Theory]
    // Claude Code memory and rules, "Path-specific rules": `*.md` anywhere, `/*.md` only directly in the base directory.
    [InlineData("**/*.ts", "src/app.ts", true)]
    [InlineData("**/*.ts", "app.ts", true)]
    [InlineData("src/**/*", "src/a/b.ts", true)]
    [InlineData("src/**/*", "lib/b.ts", false)]
    [InlineData("*.md", "README.md", true)]
    [InlineData("*.md", "docs/guide.md", true)]
    [InlineData("/*.md", "README.md", true)]
    [InlineData("/*.md", "docs/guide.md", false)]
    [InlineData("src/components/*.tsx", "src/components/A.tsx", true)]
    [InlineData("src/components/*.tsx", "src/components/ui/A.tsx", false)]
    public void ClaudeModel_AgreesWithTheExamplesClaudeDocuments(string pattern, string path, bool expected)
    {
        Assert.Equal(expected, Documented.ClaudeList([pattern], path));
    }

    [Fact]
    public void Claude_ReadingAPatternWithoutASlashVerbatim_WouldMatchWhatTheSourceDoesNot()
    {
        // This is why the translation anchors it: the same text means the root only to the source and at any depth to Claude.
        Assert.False(PortableGlob.IsMatch("*.ts", "src/app.ts"));
        Assert.True(Documented.ClaudeList(["*.ts"], "src/app.ts"));

        var translation = Translate("claude", "*.ts");

        Assert.Equal(["/*.ts"], translation.Patterns);
        Assert.False(Documented.ClaudeList(translation.Patterns, "src/app.ts"));
        Assert.True(Documented.ClaudeList(translation.Patterns, "app.ts"));
    }

    public static IEnumerable<object[]> PatternsAndTargets() =>
        from testCase in ConformanceSuite.GlobCases()
        from target in ScopedTargets
        select new object[] { testCase.Pattern, target };

    // Either the translation matches exactly the files the source pattern matches, or it is refused for a reason
    // that is the same for every pattern of that shape. There is no third outcome in which a tool matches other files.
    [Theory]
    [MemberData(nameof(PatternsAndTargets))]
    public void Translate_EitherKeepsExactlyTheMatchedFilesOrRefuses(string pattern, string target)
    {
        var testCase = ConformanceSuite.GlobCases().Single(c => c.Pattern == pattern);
        var expanded = PortableGlob.Parse(pattern).Patterns;
        var mustRefuse = target switch
        {
            "copilot" or "cursor" => expanded.Any(p => p.Contains(',', StringComparison.Ordinal)),
            "windsurf" or "devin" => expanded.Count != 1 || !expanded[0].Contains('/', StringComparison.Ordinal) || expanded[0].Contains(',', StringComparison.Ordinal),
            _ => false
        };

        var translation = Translate(target, pattern);

        Assert.Equal(mustRefuse, translation.Problem is not null);
        Assert.Equal(mustRefuse, translation.Patterns.Count == 0);
        var comparedFiles = mustRefuse ? [] : ConformanceSuite.GlobFiles();
        foreach (var file in comparedFiles)
        {
            var matched = target == "claude"
                ? Documented.ClaudeList(translation.Patterns, file)
                : Documented.CommaSeparatedText(string.Join(',', translation.Patterns), file);

            Assert.True(testCase.Matches.Contains(file) == matched, $"{target}: '{pattern}' and {file}");
        }
    }

    [Theory]
    [InlineData("claude", new[] { "src/a,b/**" }, new[] { "src/a,b/**" })]
    [InlineData("claude", new[] { "*.ts", "Makefile", "src/**" }, new[] { "/*.ts", "/Makefile", "src/**" })]
    [InlineData("claude", new[] { "**/*.ts", "src/*.ts" }, new[] { "**/*.ts", "src/*.ts" })]
    [InlineData("copilot", new[] { "*.ts" }, new[] { "*.ts" })]
    [InlineData("copilot", new[] { "{a,b}/x", "a/x" }, new[] { "a/x", "b/x" })]
    [InlineData("cursor", new[] { "src/**/*.{ts,tsx}" }, new[] { "src/**/*.ts", "src/**/*.tsx" })]
    [InlineData("cursor", new[] { "src/**", "**/*.tsx" }, new[] { "src/**", "**/*.tsx" })]
    [InlineData("windsurf", new[] { "src/**" }, new[] { "src/**" })]
    [InlineData("devin", new[] { "**/*.test.ts" }, new[] { "**/*.test.ts" })]
    public void Translate_WhatTheToolDocuments_IsWrittenInItsOwnForm(string target, string[] items, string[] expected)
    {
        var translation = Translate(target, items);

        Assert.Null(translation.Problem);
        Assert.Equal(expected, translation.Patterns);
    }

    [Theory]
    [InlineData("copilot", new[] { "src/a,b/**" }, "contains a comma")]
    [InlineData("cursor", new[] { "src/a,b/**" }, "contains a comma")]
    [InlineData("windsurf", new[] { "src/a,b/**" }, "contains a comma")]
    [InlineData("windsurf", new[] { "src/**", "lib/**" }, "documents a single pattern only")]
    [InlineData("devin", new[] { "src/**/*.{ts,tsx}" }, "has 2 (after brace groups")]
    [InlineData("devin", new[] { "*.ts" }, "does not document how it reads a pattern without '/'")]
    [InlineData("claude", new[] { "src/**", "src/[ab]/**" }, "is not a pattern the specification defines")]
    [InlineData("copilot", new[] { "/src/**" }, "is not a pattern the specification defines")]
    public void Translate_WhatCannotBeToldWithoutChangingTheScope_IsRefusedWithTheReason(string target, string[] items, string expectedReason)
    {
        var translation = Translate(target, items);

        Assert.Empty(translation.Patterns);
        Assert.Contains("rule 'ui'", translation.Problem);
        Assert.Contains(expectedReason, translation.Problem);
    }

    [Fact]
    public void Translate_RootLevelPatternForWindsurfAndDevin_SuggestsTheDeepFormInTheMessage()
    {
        var problem = Translate("windsurf", "*.ts").Problem;

        Assert.Contains("'**/*.ts'", problem);
    }

    [Fact]
    public void Plan_ScopedRuleThatSomeTargetsCannotCarry_RefusesTheWholeExportAndWritesNothing()
    {
        Configure("\"agents\", \"copilot\", \"claude\", \"cursor\", \"windsurf\"", "split", ("ui", ScopedRule("[\"src/a,b/**\"]")));
        var before = FilesOnDisk();

        var plan = Plan();

        Assert.True(plan.HasErrors);
        var errors = plan.Diagnostics.Where(d => d.IsError).ToList();
        Assert.All(errors, e => Assert.Equal(DiagnosticCodes.ScopeNotRepresentable, e.Code));
        Assert.Equal(
            [
                ".cursor/rules/aicontext-ui.mdc",
                ".github/instructions/aicontext-ui.instructions.md",
                ".windsurf/rules/aicontext-ui.md"
            ],
            errors.Select(e => e.Location!).Order(StringComparer.Ordinal));
        Assert.All(errors, e => Assert.Contains("never widened to the whole project", e.Message));
        Assert.Equal(
            ["copilot", "cursor", "windsurf"],
            plan.Files.Where(f => f.Status == ExportFileStatus.Blocked).Select(f => f.Target).Order(StringComparer.Ordinal));

        Assert.Throws<InvalidOperationException>(() => ExportService.Apply(plan));
        Assert.Equal(before, FilesOnDisk());
    }

    [Fact]
    public void Plan_ScopedRuleThatATargetCannotCarry_IsNotMovedIntoItsMainFile()
    {
        Configure("\"copilot\", \"cursor\", \"windsurf\"", "split", ("ui", ScopedRule("[\"src/a,b/**\"]")));

        var plan = Plan();

        foreach (var main in plan.Files.Where(f => f.Path.EndsWith("copilot-instructions.md", StringComparison.Ordinal) ||
                                                   f.Path.EndsWith("project.mdc", StringComparison.Ordinal) ||
                                                   f.Path.EndsWith("project.md", StringComparison.Ordinal)))
        {
            Assert.DoesNotContain(Marker, main.Content);
        }
    }

    [Fact]
    public void Plan_OnlyTargetsThatCarryTheScope_AreWrittenWhenTheOthersAreLeftOut()
    {
        Configure("\"agents\", \"copilot\", \"claude\"", "split", ("ui", ScopedRule("[\"src/a,b/**\"]")));

        var plan = Plan("claude");

        Assert.False(plan.HasErrors);
        Assert.False(ExportService.Apply(plan).HasErrors);
        Assert.Contains("  - \"src/a,b/**\"\n", File.ReadAllText(InProject(".claude/rules/aicontext-ui.md")));
    }

    [Fact]
    public void Plan_SingleLayout_StatesTheScopeAsTextAndRefusesNothing()
    {
        Configure("\"copilot\", \"cursor\", \"windsurf\", \"claude\", \"agents\"", "single", ("ui", ScopedRule("[\"src/a,b/**\"]")));

        var plan = Plan();

        Assert.False(plan.HasErrors);
        Assert.All(plan.Files, f => Assert.Contains("Applies to: `src/a,b/**`", f.Content));
    }

    // The two cases look alike and are different on purpose. On-demand content has no scope to lose, and a tool
    // without a description mechanism keeps it inline (SPEC 6.4). A scoped rule has a scope, and a tool that supports
    // scopes is never given the rule without it.
    [Fact]
    public void Plan_OnDemandRuleKeepsItsInlineFallbackWhileTheUnrepresentableScopedRuleIsRefused()
    {
        Configure(
            "\"copilot\"",
            "split",
            ("api", "---\ndescription: API design\n---\nAPI-BODY-MARKER\n"),
            ("ui", ScopedRule("[\"src/a,b/**\"]")));

        var plan = Plan();

        var error = Assert.Single(plan.Diagnostics, d => d.IsError);
        Assert.Equal(DiagnosticCodes.ScopeNotRepresentable, error.Code);
        var main = plan.Files.Single(f => f.Path.EndsWith("copilot-instructions.md", StringComparison.Ordinal));
        Assert.Contains("API-BODY-MARKER", main.Content);
        Assert.DoesNotContain(Marker, main.Content);
    }

    [Fact]
    public void Plan_ExistingGeneratedRuleFile_IsKeptAsItIsWhenTheRuleBecomesUnrepresentable()
    {
        Configure("\"copilot\"", "split", ("ui", ScopedRule("[\"src/ui/**\"]")));
        Assert.False(ExportService.Apply(Plan()).HasErrors);
        var ruleFile = InProject(".github/instructions/aicontext-ui.instructions.md");
        var before = File.ReadAllText(ruleFile);
        Assert.Contains("applyTo: \"src/ui/**\"", before);

        File.WriteAllText(InProject(".aicontext/rules/ui.md"), ScopedRule("[\"src/a,b/**\"]"));
        var plan = Plan();

        Assert.True(plan.HasErrors);
        Assert.DoesNotContain(plan.Files, f => f.Status == ExportFileStatus.Deleted);
        Assert.Throws<InvalidOperationException>(() => ExportService.Apply(plan));
        Assert.Equal(before, File.ReadAllText(ruleFile));
    }

    [Fact]
    public void Plan_BraceGroup_IsWrittenAsSeparatePatternsEverywhereItCanBe()
    {
        Configure("\"copilot\", \"cursor\", \"claude\"", "split", ("ui", ScopedRule("src/**/*.{ts,tsx}")));

        var plan = Plan();

        Assert.False(plan.HasErrors);
        Assert.Contains("applyTo: \"src/**/*.ts,src/**/*.tsx\"\n", plan.Files.Single(f => f.Target == "copilot" && f.Path.Contains("aicontext-ui")).Content);
        Assert.Contains("globs: src/**/*.ts,src/**/*.tsx\n", plan.Files.Single(f => f.Target == "cursor" && f.Path.Contains("aicontext-ui")).Content);
        Assert.Contains("paths:\n  - \"src/**/*.ts\"\n  - \"src/**/*.tsx\"\n", plan.Files.Single(f => f.Target == "claude" && f.Path.Contains("aicontext-ui")).Content);
    }

    [Fact]
    public void Status_BlockedScope_ShowsTheStateAndTheReason()
    {
        Configure("\"copilot\", \"claude\"", "split", ("ui", ScopedRule("[\"src/a,b/**\"]")));

        var report = ExportService.Status(_project);

        Assert.False(report.HasErrors);
        var copilot = report.Targets.Single(t => t.Target == "copilot");
        Assert.Equal(SyncState.Blocked, copilot.State);
        Assert.Contains("contains a comma", Assert.Single(copilot.Problems));
        Assert.Empty(report.Targets.Single(t => t.Target == "claude").Problems);
    }

    [Fact]
    public void Cli_StatusAndExportOfABlockedScope_SayWhatIsRefusedAndWriteNothing()
    {
        Configure("\"copilot\", \"claude\"", "split", ("ui", ScopedRule("[\"src/a,b/**\"]")));
        var before = FilesOnDisk();

        using var statusOut = new StringWriter();
        using var statusErr = new StringWriter();
        var statusCode = CliApp.Run(["status"], statusOut, statusErr, _project);

        Assert.Equal(0, statusCode);
        Assert.Contains("Blocked (export refuses these and writes nothing):", statusOut.ToString());
        Assert.Contains("copilot: ", statusOut.ToString());

        using var exportOut = new StringWriter();
        using var exportErr = new StringWriter();
        var exportCode = CliApp.Run(["export", "--target", "all"], exportOut, exportErr, _project);

        Assert.NotEqual(0, exportCode);
        Assert.Contains("never widened to the whole project", exportErr.ToString());
        Assert.Equal(before, FilesOnDisk());
    }
}
