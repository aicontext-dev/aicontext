using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using AIContext;
using AIContext.Cli;
using Xunit;

namespace AIContext.Tests;

// F06 and F07: the examples a first-time user or an integrator copies have to work. This is deliberately not a
// system that runs the documentation. It covers the two things that went wrong: the example of the library README is
// real code (LibraryReadmeSample) that is compiled with the tests, run for each way a safe export can end, and kept
// identical to the README; and the commands that run the CLI from the repository name a framework, because the CLI
// targets several.
public sealed class DocumentationExamplesTests : IDisposable
{
    private static readonly string DocsDirectory = Path.Combine(AppContext.BaseDirectory, "docs");

    private readonly string _project = Path.Combine(Path.GetTempPath(), "aicontext-tests-" + Guid.NewGuid().ToString("N"));

    public DocumentationExamplesTests()
    {
        Directory.CreateDirectory(_project);
        var init = ProjectInitializer.Initialize(_project, "demo", InitProfile.Core, ContextSchema.CanonicalUrl, "0.1.0", force: false);
        Assert.Empty(init.Conflicts);
        File.WriteAllText(
            InProject(".aicontext/context.json"),
            """{ "specVersion": "0.1.0", "project": { "name": "demo" }, "exports": { "targets": ["agents", "copilot"] } }""");
    }

    public void Dispose() => Directory.Delete(_project, recursive: true);

    private string InProject(string relative) => Path.Combine(_project, relative.Replace('/', Path.DirectorySeparatorChar));

    private static string ReadDocument(string relative)
    {
        var path = Path.Combine(DocsDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"The document was not copied to the test output: {path}");
        return LineEndings.Normalize(File.ReadAllText(path));
    }

    // The lines between the markers of the compiled example, without their indentation.
    private static string CompiledSample()
    {
        var source = ReadDocument("sample/LibraryReadmeSample.cs.txt");
        var start = source.IndexOf("// <sample>\n", StringComparison.Ordinal) + "// <sample>\n".Length;
        var end = source.IndexOf("        // </sample>", StringComparison.Ordinal);
        Assert.True(start > 0 && end > start, "The sample markers are missing.");
        var lines = source[start..end].TrimEnd('\n').Split('\n');
        return string.Join('\n', lines.Select(line => line.Length >= 8 ? line[8..] : line));
    }

    [Fact]
    public void LibraryReadme_ShowsExactlyTheExampleThatIsCompiledAndRun()
    {
        var readme = ReadDocument("library/README.md");

        var block = Regex.Match(readme, "```csharp\n(?<code>.*?)\n```", RegexOptions.Singleline);

        Assert.True(block.Success, "The library README has no C# example.");
        Assert.Equal("using AIContext;\n\n" + CompiledSample(), block.Groups["code"].Value);
    }

    [Fact]
    public void Sample_ProjectThatCanBeExported_WritesEveryFileAndReturnsZero()
    {
        Assert.Equal(0, LibraryReadmeSample.Run(_project));

        Assert.True(File.Exists(InProject("AGENTS.md")));
        Assert.True(File.Exists(InProject(".github/copilot-instructions.md")));
        Assert.Equal(0, ExportService.Plan(new ExportRequest(_project, "all")).ChangedCount);
        Assert.Equal(0, LibraryReadmeSample.Run(_project));
    }

    [Fact]
    public void Sample_PlanWithErrors_ReturnsOneAndWritesNothing()
    {
        File.WriteAllText(InProject("AGENTS.md"), "written by hand");
        var before = FilesOnDisk();

        Assert.Equal(1, LibraryReadmeSample.Run(_project));

        Assert.Equal(before, FilesOnDisk());
        Assert.Equal("written by hand", File.ReadAllText(InProject("AGENTS.md")));
    }

    [Fact]
    public void Sample_InvalidContext_ReturnsOneAndWritesNothing()
    {
        File.WriteAllText(InProject(".aicontext/context.json"), """{ "specVersion": "9.9.9", "project": { "name": "demo" } }""");
        var before = FilesOnDisk();

        Assert.Equal(1, LibraryReadmeSample.Run(_project));

        Assert.Equal(before, FilesOnDisk());
    }

    // A read-only file that has to change is known when the plan is made, so the example stops at the plan.
    [LinkFact(LinkCapability.ReadOnlyFile)]
    public void Sample_OutputThatCannotBeWritten_ReturnsOneAndChangesNothing()
    {
        Assert.Equal(0, LibraryReadmeSample.Run(_project));
        File.AppendAllText(InProject(".aicontext/README.md"), "\nA new line.\n");
        var agentsBefore = File.ReadAllText(InProject("AGENTS.md"));
        var copilotBefore = File.ReadAllText(InProject(".github/copilot-instructions.md"));
        Assert.Equal(2, ExportService.Plan(new ExportRequest(_project, "all")).ChangedCount);

        int exitCode;
        using (TestFiles.ReadOnlyFile(InProject(".github/copilot-instructions.md")))
        {
            exitCode = LibraryReadmeSample.Run(_project);
        }

        Assert.Equal(1, exitCode);
        Assert.Equal(agentsBefore, File.ReadAllText(InProject("AGENTS.md")));
        Assert.Equal(copilotBefore, File.ReadAllText(InProject(".github/copilot-instructions.md")));
    }

    // The first file is written and the second cannot be: Apply stops, the example reports it, and what was written
    // stays. The plan cannot know a directory without write permission, so this is the Apply branch of the example.
    [UnsupportedOSPlatform("windows")]
    [LinkFact(LinkCapability.ReadOnlyDirectory)]
    public void Sample_WriteFailsMidway_ReturnsTwoAndKeepsTheFilesAlreadyWritten()
    {
        Assert.Equal(0, LibraryReadmeSample.Run(_project));
        File.AppendAllText(InProject(".aicontext/README.md"), "\nA new line.\n");
        var copilotBefore = File.ReadAllText(InProject(".github/copilot-instructions.md"));

        int exitCode;
        using (TestFiles.ReadOnlyDirectory(InProject(".github")))
        {
            exitCode = LibraryReadmeSample.Run(_project);
        }

        Assert.Equal(2, exitCode);
        Assert.Contains("A new line.", File.ReadAllText(InProject("AGENTS.md")));
        Assert.Equal(copilotBefore, File.ReadAllText(InProject(".github/copilot-instructions.md")));
        Assert.Equal(0, LibraryReadmeSample.Run(_project));
        Assert.Contains("A new line.", File.ReadAllText(InProject(".github/copilot-instructions.md")));
    }

    private IReadOnlyList<string> FilesOnDisk() =>
        Directory.EnumerateFiles(_project, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(_project, file) + "|" + File.ReadAllText(file))
            .Order(StringComparer.Ordinal)
            .ToList();

    private string CopyDemo(string name)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "demos", name, "project");
        Assert.True(Directory.Exists(source), $"The demo was not copied to the test output: {source}");
        var destination = InProject(name);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        return destination;
    }

    private static string RunDemo(string project, int expectedExitCode, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = CliApp.Run(args, output, error, project);
        var text = output.ToString() + error.ToString();
        Assert.True(exitCode == expectedExitCode, $"Expected {expectedExitCode}, got {exitCode}:\n{text}");
        return text;
    }

    private static string[] DemoSnapshot(string project) =>
        Directory.EnumerateFiles(project, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(file => $"{file}|{File.ReadAllText(file)}|{File.GetLastWriteTimeUtc(file).Ticks}")
            .ToArray();

    private static void ChangeDemoRule(string project)
    {
        var file = Path.Combine(project, ".aicontext", "constraints.md");
        var text = File.ReadAllText(file);
        Assert.Contains("Document the expected output for each command example.", text);
        File.WriteAllText(file, text.Replace(
            "Document the expected output for each command example.",
            "Document the expected output and exit code for each command example.",
            StringComparison.Ordinal));
    }

    [Fact]
    public void Demo_Basics_DetectsDriftAndPreservesFilesDuringChecksAndRepeatedExports()
    {
        var project = CopyDemo("01-basics");
        Assert.Contains("Core profile", RunDemo(project, 0, "validate"));
        Assert.Contains("2 changed", RunDemo(project, 0, "export", "--target", "all"));
        RunDemo(project, 0, "export", "--target", "all", "--check");

        ChangeDemoRule(project);
        var stale = DemoSnapshot(project);
        Assert.Contains("out of date (2 file(s))", RunDemo(project, 1, "export", "--target", "all", "--check"));
        Assert.Equal(stale, DemoSnapshot(project));

        Assert.Contains("2 changed", RunDemo(project, 0, "export", "--target", "all"));
        foreach (var file in new[] { "AGENTS.md", Path.Combine(".github", "copilot-instructions.md") })
            Assert.Contains("expected output and exit code", File.ReadAllText(Path.Combine(project, file)));

        var updated = DemoSnapshot(project);
        RunDemo(project, 0, "export", "--target", "all", "--check");
        Assert.Contains("0 changed, 2 unchanged", RunDemo(project, 0, "export", "--target", "all"));
        Assert.Equal(updated, DemoSnapshot(project));
    }

    [Fact]
    public void Demo_ScopedRules_PreviewsWithoutWritingAndPreservesTargetSpecificActivation()
    {
        var project = CopyDemo("02-scoped-rules");
        RunDemo(project, 0, "validate");
        Assert.Contains("2 changed", RunDemo(project, 0, "export", "--target", "all"));
        var config = Path.Combine(project, ".aicontext", "context.json");
        File.WriteAllText(config, File.ReadAllText(config).Replace("\"single\"", "\"split\"", StringComparison.Ordinal));
        var before = DemoSnapshot(project);
        Assert.Contains("5 file(s) would change", RunDemo(project, 0, "export", "--target", "all", "--dry-run"));
        Assert.Equal(before, DemoSnapshot(project));

        Assert.Contains("5 changed", RunDemo(project, 0, "export", "--target", "all"));
        var copilot = File.ReadAllText(Path.Combine(project, ".github", "instructions", "aicontext-docs.instructions.md"));
        Assert.Contains("applyTo: \"docs/**\"", copilot);
        Assert.DoesNotContain("\ndescription:", copilot);
        Assert.Contains("Description: Guidance for documentation pages", copilot);
        Assert.Contains("globs: docs/**",
            File.ReadAllText(Path.Combine(project, ".cursor", "rules", "aicontext-docs.mdc")));
        var copilotMain = File.ReadAllText(Path.Combine(project, ".github", "copilot-instructions.md"));
        var cursorMain = File.ReadAllText(Path.Combine(project, ".cursor", "rules", "project.mdc"));
        Assert.DoesNotContain("Start each documentation page", copilotMain);
        Assert.DoesNotContain("Start each documentation page", cursorMain);
        Assert.Contains("Use when: Checklist for reviewing documentation changes", copilotMain);
        Assert.DoesNotContain("Check that documented commands", cursorMain);
        var review = File.ReadAllText(Path.Combine(project, ".cursor", "rules", "aicontext-review.mdc"));
        Assert.Contains("description: \"Checklist for reviewing documentation changes\"", review);
        Assert.DoesNotContain("globs:", review);
        RunDemo(project, 0, "export", "--target", "all", "--check");
    }

    [Fact]
    public void Demo_CiChecks_DistinguishesPlaceholderWarningsFromStaleExports()
    {
        var project = CopyDemo("03-ci-drift-check");
        Assert.Contains("Extended profile", RunDemo(project, 0, "validate", "--strict"));
        Assert.Contains("1 changed", RunDemo(project, 0, "export", "--target", "all"));
        RunDemo(project, 0, "export", "--target", "all", "--check");
        var readme = Path.Combine(project, ".aicontext", "README.md");
        var original = File.ReadAllText(readme);
        File.AppendAllText(readme, "\nTODO: Document the review checklist.\n");
        var unfinished = DemoSnapshot(project);
        Assert.Contains("placeholder", RunDemo(project, 0, "validate"));
        Assert.Contains("placeholder", RunDemo(project, 1, "validate", "--strict"));
        Assert.Equal(unfinished, DemoSnapshot(project));
        File.WriteAllText(readme, original);
        ChangeDemoRule(project);
        RunDemo(project, 0, "validate", "--strict");
        var stale = DemoSnapshot(project);
        Assert.Contains("out of date (1 file(s))", RunDemo(project, 1, "export", "--target", "all", "--check"));
        Assert.Equal(stale, DemoSnapshot(project));
        Assert.Contains("1 changed", RunDemo(project, 0, "export", "--target", "all"));
        RunDemo(project, 0, "validate", "--strict");
        RunDemo(project, 0, "export", "--target", "all", "--check");
    }

    // The CLI projects target net9.0 and net10.0, and `dotnet run` refuses a project that targets several frameworks
    // unless one is named. CI runs the same form (see validate-aicontext.yml).
    [Theory]
    [InlineData("README.md")]
    [InlineData("library/README.md")]
    [InlineData("cli/README.md")]
    [InlineData("user-manual.md")]
    [InlineData("index.html")]
    public void Documents_CommandsThatRunTheCliFromTheRepository_NameAFramework(string document)
    {
        var commands = ReadDocument(document).Split('\n')
            .Select(line => Regex.Replace(line, "<[^>]+>", string.Empty).Trim())
            .Where(line => line.StartsWith("dotnet run", StringComparison.Ordinal))
            .ToList();

        Assert.All(commands, command => Assert.Matches(@"\s(-f|--framework)[ =]net\d+\.\d+\b", command));
    }
}
