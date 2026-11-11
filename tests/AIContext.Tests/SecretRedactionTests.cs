using System.Text;
using AIContext.Cli;
using Xunit;

namespace AIContext.Tests;

// Technical audit §14 question 6: the secret scanner never prints the value it found, but that does not show that no
// other message of the tool prints it. Each scenario puts a synthetic token (it matches the scanner's GitHub token
// pattern and contains a marker) into one place of an otherwise valid project, runs every command that reads the
// project and the library calls behind it, and looks for the marker in everything that was reported: output, error
// output, and the message and location of every diagnostic. The token is never a real credential.
public sealed class SecretRedactionTests : IDisposable
{
    private const string Marker = "SYNTH0123456789";
    private const string Token = "ghp_" + Marker + "ABCDEFGHIJKLMNOPQRSTUV";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "aicontext-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static string ContextJson(string exports = """{ "targets": ["agents"], "layout": "split", "nestedAgents": true }""") =>
        $$"""{ "$schema": "{{ContextSchema.CanonicalUrl}}", "specVersion": "0.1.0", "project": { "name": "demo" }, "exports": {{exports}} }""";

    // Scenario name -> how the synthetic token gets into the project (the project dir, already valid).
    private static readonly Dictionary<string, Action<string>> Scenarios = new()
    {
        ["readme-body"] = p => Write(p, "README.md", $"# Project: demo\n\nA service.\n\ntoken = {Token}\n"),
        ["text-file"] = p => Write(p, "notes.txt", $"api_key = {Token}\n"),
        ["rule-body"] = p => Write(p, "rules/ui.md", $"---\ndescription: UI\n---\nUse {Token}\n"),
        ["invalid-json"] = p => Write(p, "context.json", $$"""{ "project": { "name": {{Token}} } }"""),
        ["property-name"] = p => Write(p, "context.json", $$"""{ "specVersion": "0.1.0", "project": { "name": "demo" }, "{{Token}}": true }"""),
        ["property-value"] = p => Write(p, "context.json", $$"""{ "specVersion": "0.1.0", "project": { "name": "demo" }, "owner": "{{Token}}" }"""),
        ["spec-version"] = p => Write(p, "context.json", $$"""{ "specVersion": "{{Token}}", "project": { "name": "demo" } }"""),
        ["schema-url-credentials"] = p => Write(p, "context.json", $$"""{ "$schema": "https://user:{{Token}}@example.com/schema.json", "specVersion": "0.1.0", "project": { "name": "demo" } }"""),
        ["target-name"] = p => Write(p, "context.json", ContextJson($$"""{ "targets": ["{{Token}}"] }""")),
        ["default-target"] = p => Write(p, "context.json", ContextJson($$"""{ "defaultTarget": "{{Token}}" }""")),
        ["layout-value"] = p => Write(p, "context.json", ContextJson($$"""{ "layout": "{{Token}}" }""")),
        ["frontmatter-line"] = p => Write(p, "rules/ui.md", $"---\ndescription: UI\nAPI_KEY={Token}\n---\nBody.\n"),
        ["frontmatter-key"] = p => Write(p, "rules/ui.md", $"---\ndescription: UI\n{Token}: x\n---\nBody.\n"),
        ["rule-path-separator"] = p => Write(p, "rules/ui.md", $"---\npaths: [src\\{Token}/**]\n---\nBody.\n"),
        ["rule-scope-pattern"] = p => Write(p, "rules/ui.md", $"---\npaths: [\"{Token}/**/*.cs\"]\n---\nBody.\n"),
        ["rule-pattern-outside-subset"] = p => Write(p, "rules/ui.md", $"---\npaths: [\"{Token}/[x]/**\"]\n---\nBody.\n"),
        ["rule-pattern-comma"] = p =>
        {
            Write(p, "context.json", ContextJson("""{ "targets": ["copilot", "windsurf"], "layout": "split" }"""));
            Write(p, "rules/ui.md", $"---\npaths: [\"{Token},x/**\"]\n---\nBody.\n");
        },
        ["rule-pattern-root-level"] = p =>
        {
            Write(p, "context.json", ContextJson("""{ "targets": ["windsurf"], "layout": "split" }"""));
            Write(p, "rules/ui.md", $"---\npaths: [\"{Token}\"]\n---\nBody.\n");
        },
        ["rule-description"] = p => Write(p, "rules/ui.md", $"---\ndescription: Use {Token}\n---\nBody.\n"),
        ["project-name"] = p => Write(p, "context.json", $$"""{ "specVersion": "0.1.0", "project": { "name": "{{Token}}" }, "exports": { "targets": ["agents"] } }""")
    };

    public static TheoryData<string> ScenarioNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in Scenarios.Keys)
        {
            data.Add(name);
        }

        return data;
    }

    private static void Write(string project, string relative, string content)
    {
        var path = Path.Combine(project, ".aicontext", relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private string CreateProject(string scenario)
    {
        var project = Path.Combine(_root, scenario);
        Directory.CreateDirectory(project);
        ProjectInitializer.Initialize(project, "demo", InitProfile.Extended, ContextSchema.CanonicalUrl, "0.1.0", force: false);
        Write(project, "README.md", "# Project: demo\n\nA service.\n");
        Write(project, "architecture.md", "Two layers.");
        Write(project, "decisions.md", "### D1. Use PostgreSQL\n**Decision:** PostgreSQL.");
        Write(project, "constraints.md", "- Never log customer data.");
        Write(project, "context.json", ContextJson());
        Scenarios[scenario](project);
        return project;
    }

    // Everything the tool reports about the project, in one string.
    private static string EverythingReported(string project)
    {
        var text = new StringBuilder();
        string[][] commands =
        [
            ["validate"], ["validate", "--strict"], ["status"], ["status", "--layout", "single"],
            ["export", "--target", "all", "--dry-run"], ["export", "--target", "all", "--check"], ["export", "--target", "all"]
        ];
        foreach (var args in commands)
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            CliApp.Run(args, output, error, project);
            text.AppendLine(output.ToString()).AppendLine(error.ToString());
        }

        static void Add(StringBuilder text, IEnumerable<Diagnostic> diagnostics)
        {
            foreach (var d in diagnostics)
            {
                text.AppendLine($"{d.Code} {d.Message} {d.Location}");
            }
        }

        Add(text, ContextValidator.ValidateDirectory(project, ContextSchema.CanonicalUrl).Diagnostics);
        Add(text, ExportService.Plan(new ExportRequest(project, "all")).Diagnostics);
        Add(text, ExportService.Status(project).Diagnostics);
        var plan = NestedAgentsPlanner.Plan(RuleLoader.Load(Path.Combine(project, ".aicontext")).Rules, project);
        foreach (var kept in plan.Kept)
        {
            text.AppendLine(kept.Reason);
        }

        return text.ToString();
    }

    [Theory]
    [MemberData(nameof(ScenarioNames))]
    public void Commands_SyntheticTokenInTheProject_IsNeverPrinted(string scenario)
    {
        var project = CreateProject(scenario);

        var reported = EverythingReported(project);

        Assert.DoesNotContain(Marker, reported);
    }

    // The scanner still does its job in the scenarios where the token is in a text the scanner reads.
    [Theory]
    [InlineData("readme-body", "README.md:5")]
    [InlineData("text-file", "notes.txt:1")]
    [InlineData("rule-body", "rules/ui.md:4")]
    public void Validate_SyntheticTokenInAContextFile_IsReportedByFileAndLineOnly(string scenario, string location)
    {
        var project = CreateProject(scenario);

        var finding = Assert.Single(
            ContextValidator.ValidateDirectory(project, ContextSchema.CanonicalUrl).Diagnostics,
            d => d.Code == DiagnosticCodes.PossibleSecret);

        Assert.Equal($".aicontext/{location}", finding.Location);
        Assert.DoesNotContain(Marker, finding.Message);
    }

    [Fact]
    public void Frontmatter_LineWithSyntheticToken_IsNotRepeatedInTheError()
    {
        var parsed = Frontmatter.Parse($"---\ndescription: UI\nAPI_KEY={Token}\n---\nBody.\n");

        Assert.NotNull(parsed.Error);
        Assert.DoesNotContain(Marker, parsed.Error);
        Assert.Contains(SecretScanner.RedactedText, parsed.Error);
    }

    // Only text that looks like a secret is held back; every other message keeps quoting what is wrong, so the
    // messages stay as useful as they were.
    [Fact]
    public void Frontmatter_OrdinaryUnsupportedLine_IsStillQuoted()
    {
        var parsed = Frontmatter.Parse("---\ndescription: UI\nthis is not a key value pair\n---\nBody.\n");

        Assert.Contains("'this is not a key value pair'", parsed.Error);
    }

    [Theory]
    [InlineData("src/ui/**")]
    [InlineData("description")]
    [InlineData("password: required")]
    [InlineData("api_key = your-api-key-here")]
    public void Redact_TextThatDoesNotLookLikeASecret_IsReturnedUnchanged(string text)
    {
        Assert.Equal(text, SecretScanner.Redact(text));
    }

    [Theory]
    [InlineData(Token)]
    [InlineData("API_KEY=" + Token)]
    [InlineData("src\\" + Token + "/**")]
    [InlineData("password = hunter2!")]
    [InlineData("https://user:s3cr3tPass@example.com/x")]
    [InlineData("-----BEGIN PRIVATE KEY-----")]
    public void Redact_TextThatLooksLikeASecret_IsReplaced(string text)
    {
        Assert.Equal(SecretScanner.RedactedText, SecretScanner.Redact(text));
    }
}
