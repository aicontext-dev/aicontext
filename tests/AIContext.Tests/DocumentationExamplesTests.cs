using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using AIContext;
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

    // The plan is fine; Apply finds that a file cannot be written and refuses before it writes anything.
    [LinkFact(LinkCapability.ReadOnlyFile)]
    public void Sample_ApplyRefusesTheWholePlan_ReturnsTwoAndChangesNothing()
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

        Assert.Equal(2, exitCode);
        Assert.Equal(agentsBefore, File.ReadAllText(InProject("AGENTS.md")));
        Assert.Equal(copilotBefore, File.ReadAllText(InProject(".github/copilot-instructions.md")));
    }

    // The first file is written and the second cannot be: the example reports the failure, and what was written stays.
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
