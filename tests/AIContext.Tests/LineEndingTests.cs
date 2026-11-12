using System.Text;
using AIContext;
using Xunit;

namespace AIContext.Tests;

// F05: "always LF" has to hold for every line ending a source file can have, not only for CRLF. LF, CRLF and a lone CR
// are one kind of line break (LineEndings.Normalize), so the parsed text, the source hash, the line numbers of findings
// and every exported file are the same for LF, CRLF, CR and a mix of them. The mix is LF, CR, CRLF in turn, which never
// puts a CR in front of a separate LF, so the number of breaks stays the same.
public sealed class LineEndingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aicontext-tests-" + Guid.NewGuid().ToString("N"));

    public LineEndingTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    public static TheoryData<string> Styles() => new() { "lf", "crlf", "cr", "mixed" };

    public static TheoryData<string, string, ExportLayout> RenderCases()
    {
        var data = new TheoryData<string, string, ExportLayout>();
        foreach (var style in new[] { "lf", "crlf", "cr", "mixed" })
        {
            foreach (var target in ExportTargets.Names)
            {
                data.Add(style, target, ExportLayout.Single);
                data.Add(style, target, ExportLayout.Split);
            }
        }

        return data;
    }

    public static TheoryData<string, ExportLayout> ProjectCases()
    {
        var data = new TheoryData<string, ExportLayout>();
        foreach (var style in new[] { "lf", "crlf", "cr", "mixed" })
        {
            data.Add(style, ExportLayout.Single);
            data.Add(style, ExportLayout.Split);
        }

        return data;
    }

    // The same text with every line break written the way the style says.
    private static string WithEndings(string lf, string style)
    {
        string[] mixed = ["\n", "\r", "\r\n"];
        var builder = new StringBuilder();
        var breaks = 0;
        foreach (var c in lf)
        {
            if (c != '\n')
            {
                builder.Append(c);
                continue;
            }

            builder.Append(style switch
            {
                "crlf" => "\r\n",
                "cr" => "\r",
                "mixed" => mixed[breaks++ % mixed.Length],
                _ => "\n"
            });
        }

        return builder.ToString();
    }

    private const string Readme = "# Project: demo\n\nShort summary.\nSecond line.\n\n**Tech stack:** .NET\n";
    private const string Architecture = "Layers.\n\n- Core\n- CLI\n";
    private const string Decisions = "### D1. First\n**Decision:** One.\n\n### D2. Second\n**Decision:** Two.\n";
    private const string Patterns = "- Pattern one.\n- Pattern two.\n";
    private const string Constraints = "Hard rules.\n\n- Never do X.\n";
    private const string Glossary = "Term: meaning.\nOther: meaning.\n";
    private const string UiRule = "---\ndescription: UI conventions\npaths:\n  - src/ui/**   # the ui code\n---\nUse shared components.\n\nSecond paragraph.\n";
    private const string ApiRule = "---\ndescription: API design\n---\nReturn JSON envelopes.\nNever throw raw strings.\n";
    private const string StyleRule = "Format code.\n\nOne rule per line.\n";

    private static ExportContext Fixture(string style) => new(
        "demo",
        WithEndings(Readme, style),
        WithEndings(Architecture, style),
        WithEndings(Decisions, style),
        WithEndings(Patterns, style),
        WithEndings(Constraints, style),
        WithEndings(Glossary, style),
        [
            Rule("ui", "UI conventions", ["src/ui/**"], "Use shared components.\n\nSecond paragraph.", style),
            Rule("api", "API design", [], "Return JSON envelopes.\nNever throw raw strings.", style),
            Rule("style", null, [], "Format code.\n\nOne rule per line.", style)
        ]);

    private static RuleDocument Rule(string name, string? description, string[] paths, string body, string style) =>
        new(name, description, paths, WithEndings(body, style));

    private static void AssertSameOutputs(IReadOnlyList<ExportOutput> expected, IReadOnlyList<ExportOutput> actual)
    {
        Assert.Equal(expected.Select(o => o.RelativePath), actual.Select(o => o.RelativePath));
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Content, actual[i].Content);
            Assert.Equal(expected[i].Problem, actual[i].Problem);
            Assert.DoesNotContain('\r', actual[i].Content);
        }
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("a", "a")]
    [InlineData("a\nb", "a\nb")]
    [InlineData("a\r\nb", "a\nb")]
    [InlineData("a\rb", "a\nb")]
    [InlineData("a\r", "a\n")]
    [InlineData("\r", "\n")]
    [InlineData("\r\n", "\n")]
    [InlineData("a\r\r\nb", "a\n\nb")]
    [InlineData("a\n\rb", "a\n\nb")]
    [InlineData("a\r\n\r\nb", "a\n\nb")]
    [InlineData("a\rb\r\nc\nd", "a\nb\nc\nd")]
    // Only CR and LF are line breaks; the other Unicode separators are ordinary characters of a Markdown file.
    [InlineData("a\u0085b\u2028c\u2029d", "a\u0085b\u2028c\u2029d")]
    [InlineData("a\r\u0085b\u2028c\u2029d", "a\n\u0085b\u2028c\u2029d")]
    public void Normalize_TurnsEveryKindOfLineBreakIntoOneLf(string input, string expected)
    {
        Assert.Equal(expected, LineEndings.Normalize(input));
    }

    // A file counts as generated by the marker in its first lines; the lines are those of the normalized text, so a
    // file with CR endings is judged like the same file with LF endings.
    [Theory]
    [MemberData(nameof(Styles))]
    public void IsGenerated_LooksAtTheFirstLinesWhateverTheLineEndings(string style)
    {
        var marker = $"<!-- {ContextExporter.GeneratedMarker} --target agents -->";
        var early = "# Title\n" + marker + "\nText\n";
        var late = string.Concat(Enumerable.Repeat("line\n", 12)) + marker + "\n";

        Assert.True(ExportWriter.IsGenerated(WithEndings(early, style)));
        Assert.False(ExportWriter.IsGenerated(WithEndings(late, style)));
    }

    [Theory]
    [MemberData(nameof(Styles))]
    public void Frontmatter_Parse_ReadsTheSameWhateverTheLineEndings(string style)
    {
        var expected = Frontmatter.Parse(UiRule);

        var actual = Frontmatter.Parse(WithEndings(UiRule, style));

        Assert.Null(actual.Error);
        Assert.Equal(expected.Body, actual.Body);
        Assert.Equal(expected.Values.Keys, actual.Values.Keys);
        foreach (var (key, value) in expected.Values)
        {
            var other = actual.Values[key];
            Assert.Equal(value.Scalar, other.Scalar);
            Assert.Equal(value.List, other.List);
            Assert.Equal(value.IsNested, other.IsNested);
            Assert.Equal(value.Problem, other.Problem);
        }
    }

    [Theory]
    [MemberData(nameof(Styles))]
    public void Frontmatter_Parse_ReportsTheSameProblemAtTheSameLine(string style)
    {
        const string broken = "---\ndescription: a\nnot a key value pair\npaths: x\n---\nBody\n";

        var expected = Frontmatter.Parse(broken);
        var actual = Frontmatter.Parse(WithEndings(broken, style));

        Assert.NotNull(expected.Error);
        Assert.Contains("(line 3)", expected.Error);
        Assert.Equal(expected.Error, actual.Error);
    }

    // The regression behind F05 that matters most: a header the parser cannot find would turn a scoped rule into an
    // always-on rule whose body starts with the header text.
    [Theory]
    [MemberData(nameof(Styles))]
    public void RuleLoader_ScopedRule_StaysScopedWhateverTheLineEndings(string style)
    {
        var contextDirectory = Path.Combine(_root, "load-" + style, ".aicontext");
        Directory.CreateDirectory(Path.Combine(contextDirectory, "rules"));
        File.WriteAllText(Path.Combine(contextDirectory, "rules", "ui.md"), WithEndings(UiRule, style));

        var result = RuleLoader.Load(contextDirectory);

        Assert.Empty(result.Errors);
        var rule = Assert.Single(result.Rules);
        Assert.Equal(RuleKind.Scoped, rule.Kind);
        Assert.Equal(["src/ui/**"], rule.Paths);
        Assert.Equal("UI conventions", rule.Description);
        Assert.Equal("Use shared components.\n\nSecond paragraph.", rule.Body);
    }

    [Theory]
    [MemberData(nameof(Styles))]
    public void ComputeSourceHash_IsTheSameWhateverTheLineEndings(string style)
    {
        Assert.Equal(ContextExporter.ComputeSourceHash(Fixture("lf")), ContextExporter.ComputeSourceHash(Fixture(style)));
    }

    [Fact]
    public void ComputeSourceHash_ChangesWithTheContentOfALine()
    {
        var changed = Fixture("cr") with { Architecture = WithEndings("Layers.\n\n- Core\n- Other\n", "cr") };

        Assert.NotEqual(ContextExporter.ComputeSourceHash(Fixture("lf")), ContextExporter.ComputeSourceHash(changed));
    }

    [Theory]
    [MemberData(nameof(RenderCases))]
    public void Render_GivesTheSameFilesAsLfWhateverTheLineEndings(string style, string target, ExportLayout layout)
    {
        var expected = ExportTargets.Find(target)!.Render(Fixture("lf"), layout);

        var actual = ExportTargets.Find(target)!.Render(Fixture(style), layout);

        AssertSameOutputs(expected, actual);
        Assert.All(actual, o => Assert.Null(o.Problem));
    }

    [Theory]
    [MemberData(nameof(Styles))]
    public void Render_NestedAgentsFiles_AreTheSameWhateverTheLineEndings(string style)
    {
        Directory.CreateDirectory(Path.Combine(_root, "nested", "src", "ui"));
        var options = new RenderOptions(Path.Combine(_root, "nested"), NestedAgents: true);

        var expected = ExportTargets.Find("agents")!.Render(Fixture("lf"), ExportLayout.Split, options);
        var actual = ExportTargets.Find("agents")!.Render(Fixture(style), ExportLayout.Split, options);

        Assert.Contains(actual, o => o.RelativePath == "src/ui/AGENTS.md");
        AssertSameOutputs(expected, actual);
    }

    [Theory]
    [InlineData("demo\rteam")]
    [InlineData("demo\r\nteam")]
    [InlineData("demo\nteam")]
    public void Render_ProjectNameWithALineBreak_NeverPutsACrIntoAnyFile(string name)
    {
        var context = Fixture("lf") with { ProjectName = name };
        var lfName = Fixture("lf") with { ProjectName = "demo\nteam" };

        Assert.Equal(ContextExporter.ComputeSourceHash(lfName), ContextExporter.ComputeSourceHash(context));
        foreach (var target in ExportTargets.All)
        {
            foreach (var layout in new[] { ExportLayout.Single, ExportLayout.Split })
            {
                AssertSameOutputs(target.Render(lfName, layout), target.Render(context, layout));
            }
        }
    }

    [Fact]
    public void Render_RuleDescriptionWithALoneCr_IsTheSameAsWithLf()
    {
        var lf = Fixture("lf") with { Rules = [new RuleDocument("api", "API\ndesign", [], "Return JSON.")] };
        var cr = Fixture("lf") with { Rules = [new RuleDocument("api", "API\rdesign", [], "Return JSON.")] };

        Assert.Equal(ContextExporter.ComputeSourceHash(lf), ContextExporter.ComputeSourceHash(cr));
        foreach (var target in ExportTargets.All)
        {
            AssertSameOutputs(target.Render(lf, ExportLayout.Split), target.Render(cr, ExportLayout.Split));
        }
    }

    private string WriteProject(string name, string style)
    {
        var project = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(project, ".aicontext", "rules"));
        void Write(string relative, string content) =>
            File.WriteAllText(Path.Combine(project, ".aicontext", relative.Replace('/', Path.DirectorySeparatorChar)), WithEndings(content, style));

        Write("README.md", Readme);
        Write("architecture.md", Architecture);
        Write("decisions.md", Decisions);
        Write("patterns.md", Patterns);
        Write("constraints.md", Constraints);
        Write("glossary.md", Glossary);
        Write("rules/ui.md", UiRule);
        Write("rules/api.md", ApiRule);
        Write("rules/style.md", StyleRule);
        File.WriteAllText(
            Path.Combine(project, ".aicontext", "context.json"),
            """{ "specVersion": "0.1.0", "project": { "name": "demo" }, "exports": { "targets": ["agents", "copilot", "claude", "cursor", "windsurf", "devin", "aider"] } }""");
        return project;
    }

    private static Dictionary<string, string> ContentByPath(string project, ExportPlan plan) =>
        plan.Files.ToDictionary(f => Path.GetRelativePath(project, f.Path).Replace('\\', '/'), f => f.Content, StringComparer.Ordinal);

    // The whole path from the files of .aicontext/ to the generated files: loader, validator and exporter.
    [Theory]
    [MemberData(nameof(ProjectCases))]
    public void Plan_ProjectWithAnyLineEndings_GeneratesTheSameFilesAndHashAsLf(string style, ExportLayout layout)
    {
        var lfProject = WriteProject("lf-" + layout, "lf");
        var project = WriteProject(style + "-" + layout, style);

        var expected = ExportService.Plan(new ExportRequest(lfProject, "all", layout.ToString().ToLowerInvariant()));
        var actual = ExportService.Plan(new ExportRequest(project, "all", layout.ToString().ToLowerInvariant()));

        Assert.False(expected.HasErrors, string.Join("; ", expected.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));
        Assert.False(actual.HasErrors, string.Join("; ", actual.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));
        Assert.Equal(expected.Diagnostics.Select(d => d.Code), actual.Diagnostics.Select(d => d.Code));
        var expectedContent = ContentByPath(lfProject, expected);
        var actualContent = ContentByPath(project, actual);
        Assert.Equal(expectedContent.Keys.Order(StringComparer.Ordinal), actualContent.Keys.Order(StringComparer.Ordinal));
        foreach (var (relative, content) in expectedContent)
        {
            Assert.Equal(content, actualContent[relative]);
            Assert.DoesNotContain('\r', actualContent[relative]);
            Assert.Contains("Source-hash: sha256:", actualContent[relative]);
        }
    }

    // `export --check` and a second `export` rely on this: a generated file that an editor or git rewrote with other
    // line endings is the same file, not a reason to write it again.
    [Theory]
    [MemberData(nameof(Styles))]
    public void Plan_GeneratedFilesRewrittenWithOtherLineEndings_AreUnchanged(string style)
    {
        var project = WriteProject("regenerate-" + style, "lf");
        var request = new ExportRequest(project, "all", "split");
        Assert.False(ExportService.Apply(ExportService.Plan(request)).HasErrors);
        foreach (var file in ExportService.Plan(request).Files)
        {
            File.WriteAllText(file.Path, WithEndings(File.ReadAllText(file.Path), style));
        }

        var plan = ExportService.Plan(request);

        Assert.False(plan.HasErrors);
        Assert.All(plan.Files, f => Assert.Equal(ExportFileStatus.Unchanged, f.Status));
        Assert.All(plan.Files, f => Assert.True(ExportWriter.IsGenerated(File.ReadAllText(f.Path)), f.Path));
    }

    private const string SecretText = "Intro.\nSecond line.\ntoken ghp_0123456789abcdefghijklmnopqrstuvwxyzAB\nTODO: describe this.\nEnd.\n";

    // Findings carry a line number, and the validator reads files line by line while the library scans text, so the
    // two have to count the same breaks.
    [Theory]
    [MemberData(nameof(Styles))]
    public void Scanners_ReportTheSameLineWhateverTheLineEndings(string style)
    {
        var text = WithEndings(SecretText, style);
        var directory = Path.Combine(_root, "scan-" + style);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "notes.md"), text);

        Assert.Equal(3, Assert.Single(SecretScanner.Scan("notes.md", text)).Line);
        Assert.Equal(3, Assert.Single(SecretScanner.ScanDirectory(directory)).Line);
        Assert.Equal(4, Assert.Single(PlaceholderScanner.Scan("notes.md", text)).Line);
    }

    [Theory]
    [MemberData(nameof(Styles))]
    public void Validate_ProjectWithAnyLineEndings_ReportsTheSameFindingsAtTheSameLines(string style)
    {
        var lfProject = WriteProject("validate-lf-" + style, "lf");
        var project = WriteProject("validate-" + style, style);
        foreach (var path in new[] { lfProject, project })
        {
            File.AppendAllText(Path.Combine(path, ".aicontext", "glossary.md"), WithEndings("password = hunter2\nTODO: fill in\n", path == lfProject ? "lf" : style));
        }

        var expected = ContextValidator.ValidateDirectory(lfProject, ContextSchema.CanonicalUrl).Diagnostics;
        var actual = ContextValidator.ValidateDirectory(project, ContextSchema.CanonicalUrl).Diagnostics;

        Assert.Equal(expected.Select(d => (d.Code, d.Location)), actual.Select(d => (d.Code, d.Location)));
        Assert.Contains(actual, d => d.Code == DiagnosticCodes.PossibleSecret);
        Assert.Contains(actual, d => d.Code == DiagnosticCodes.PlaceholderContent);
    }
}
