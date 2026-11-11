using AIContext.Cli;
using Xunit;

namespace AIContext.Tests;

// Step 05, technical audit §14 question 5: the files under .aicontext/ are read with the permissions of whoever runs
// the tool, and a cloned repository can contain symbolic links, so a source whose real location is outside the project
// used to end up in the generated files without a word (reproduced with a Windows junction and with Linux file and
// directory symlinks). It is refused now (AIC1008), the way export refuses to write there. Links that stay inside the
// project keep working. A hard link cannot be told from a regular file (see HardLinkTests).
public sealed class SourceLinkTests : IDisposable
{
    private const string OutsideMarker = "OUTSIDE-SOURCE-MARKER";
    private const string Readme = "# Project: demo\n\nA service. " + OutsideMarker + "\n";
    private const string ContextJson = """{ "specVersion": "0.1.0", "project": { "name": "demo" }, "exports": { "targets": ["agents"] } }""";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "aicontext-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _project;
    private readonly string _outside;
    private readonly List<string> _links = [];

    public SourceLinkTests()
    {
        _project = Path.Combine(_root, "project");
        _outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(_project);
        Directory.CreateDirectory(_outside);
        var init = ProjectInitializer.Initialize(_project, "demo", InitProfile.Core, ContextSchema.CanonicalUrl, "0.1.0", force: false);
        Assert.Empty(init.Conflicts);
        File.WriteAllText(Context("context.json"), ContextJson);
    }

    public void Dispose()
    {
        foreach (var link in Enumerable.Reverse(_links))
        {
            TestLinks.Remove(link);
        }

        Directory.Delete(_root, recursive: true);
    }

    private string Context(string relative) =>
        Path.Combine(_project, ".aicontext", relative.Replace('/', Path.DirectorySeparatorChar));

    private string InProject(string relative) =>
        Path.Combine(_project, relative.Replace('/', Path.DirectorySeparatorChar));

    private string Outside(string relative, string content)
    {
        var path = Path.Combine(_outside, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private void DirectoryLink(string link, string target)
    {
        TestLinks.CreateDirectoryLink(link, target);
        _links.Add(link);
    }

    private void FileLink(string link, string target)
    {
        TestLinks.CreateFileLink(link, target);
        _links.Add(link);
    }

    private IReadOnlyList<Diagnostic> Validate() =>
        ContextValidator.ValidateDirectory(_project, ContextSchema.CanonicalUrl).Diagnostics;

    private (int ExitCode, string Output, string Error) Cli(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = CliApp.Run(args, output, error, _project);
        return (exitCode, output.ToString(), error.ToString());
    }

    private static Diagnostic Refused(IEnumerable<Diagnostic> diagnostics, string location)
    {
        var refused = Assert.Single(diagnostics, d => d.Code == DiagnosticCodes.SourceOutsideProject);
        Assert.True(refused.IsError);
        Assert.Equal(location, refused.Location);
        Assert.Contains("outside the project", refused.Message);
        return refused;
    }

    private void AssertNothingExportedAndNothingLeaked()
    {
        var export = Cli("export", "--target", "agents");
        Assert.Equal(1, export.ExitCode);
        Assert.Contains("must be inside the project", export.Error);
        Assert.Contains("No files were written", export.Error);
        Assert.False(File.Exists(InProject("AGENTS.md")));
        Assert.DoesNotContain(OutsideMarker, export.Output + export.Error);
        Assert.Equal(1, Cli("validate").ExitCode);
        Assert.Equal(1, Cli("status").ExitCode);
    }

    [Fact]
    public void Validate_ContextDirectoryIsALinkOutsideTheProject_RefusesItAndReadsNothing()
    {
        Directory.Delete(InProject(".aicontext"), recursive: true);
        Outside("context/README.md", Readme);
        Outside("context/context.json", ContextJson);
        Outside("context/notes.txt", "-----BEGIN OPENSSH PRIVATE KEY-----\n");
        DirectoryLink(InProject(".aicontext"), Path.Combine(_outside, "context"));

        var diagnostics = Validate();

        Refused(diagnostics, ".aicontext");
        Assert.Single(diagnostics);
        AssertNothingExportedAndNothingLeaked();
    }

    [Fact]
    public void Validate_RulesDirectoryIsALinkOutsideTheProject_RefusesIt()
    {
        Outside("rules/ui.md", "---\ndescription: UI\n---\n" + OutsideMarker + "\n");
        DirectoryLink(Context("rules"), Path.Combine(_outside, "rules"));

        Refused(Validate(), ".aicontext/rules");
        AssertNothingExportedAndNothingLeaked();
    }

    [Fact]
    public void Validate_DirectoryLinkInsideTheContextLeadsOutside_IsNotScanned()
    {
        Outside("notes/key.txt", "-----BEGIN OPENSSH PRIVATE KEY-----\n");
        DirectoryLink(Context("notes"), Path.Combine(_outside, "notes"));

        var diagnostics = Validate();

        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCodes.PossibleSecret);
        var skipped = Assert.Single(diagnostics, d => d.Code == DiagnosticCodes.SecretScanIncomplete);
        Assert.Equal(".aicontext/notes", skipped.Location);
        Assert.Contains("outside the project", skipped.Message);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCodes.SourceOutsideProject);
    }

    [LinkTheory(LinkCapability.FileSymlink)]
    [InlineData("README.md")]
    [InlineData("context.json")]
    [InlineData("architecture.md")]
    [InlineData("patterns.md")]
    [InlineData("glossary.md")]
    [InlineData("rules/ui.md")]
    public void Validate_SourceFileIsASymlinkOutsideTheProject_RefusesItAndExportsNothing(string source)
    {
        var content = source switch
        {
            "README.md" => Readme,
            "context.json" => ContextJson,
            "rules/ui.md" => "---\ndescription: UI\n---\n" + OutsideMarker + "\n",
            _ => "Section. " + OutsideMarker + "\n"
        };
        var outside = Outside(source.Replace('/', '-'), content);
        Directory.CreateDirectory(Path.GetDirectoryName(Context(source))!);
        File.Delete(Context(source));
        FileLink(Context(source), outside);

        var diagnostics = Validate();

        Refused(diagnostics, $".aicontext/{source}");
        AssertNothingExportedAndNothingLeaked();
    }

    [LinkFact(LinkCapability.FileSymlink)]
    public void Validate_SourceFileIsADanglingSymlinkOutsideTheProject_IsRefusedToo()
    {
        File.Delete(Context("README.md"));
        FileLink(Context("README.md"), Path.Combine(_outside, "not-there.md"));

        Refused(Validate(), ".aicontext/README.md");
    }

    [LinkFact(LinkCapability.FileSymlink)]
    public void Export_SourceFileIsASymlinkInsideTheProject_StillWorks()
    {
        Directory.CreateDirectory(InProject("docs"));
        File.WriteAllText(InProject("docs/readme-source.md"), Readme);
        File.Delete(Context("README.md"));
        FileLink(Context("README.md"), Path.Combine("..", "docs", "readme-source.md"));

        Assert.DoesNotContain(Validate(), d => d.Code == DiagnosticCodes.SourceOutsideProject);
        var export = Cli("export", "--target", "agents");

        Assert.Equal(0, export.ExitCode);
        Assert.Contains(OutsideMarker, File.ReadAllText(InProject("AGENTS.md")));
    }

    [Fact]
    public void Export_ContextDirectoryIsALinkInsideTheProject_StillWorks()
    {
        Directory.Move(InProject(".aicontext"), InProject("shared-context"));
        DirectoryLink(InProject(".aicontext"), InProject("shared-context"));
        File.WriteAllText(InProject("shared-context/README.md"), Readme);

        Assert.DoesNotContain(Validate(), d => d.Code == DiagnosticCodes.SourceOutsideProject);
        var export = Cli("export", "--target", "agents");

        Assert.Equal(0, export.ExitCode);
        Assert.Contains(OutsideMarker, File.ReadAllText(InProject("AGENTS.md")));
    }

    [Fact]
    public void Validate_ProjectReachedThroughALink_IsNotRefused()
    {
        var entry = Path.Combine(_root, "entry");
        DirectoryLink(entry, _project);

        var diagnostics = ContextValidator.ValidateDirectory(entry, ContextSchema.CanonicalUrl).Diagnostics;

        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCodes.SourceOutsideProject);
        Assert.DoesNotContain(diagnostics, d => d.IsError);
    }

    [Fact]
    public void Validate_DirectoryLinkInsideTheContextStaysInsideTheProject_IsScannedOnce()
    {
        var token = "ghp_" + "SOURCE0123456789ABCDEFGHIJKLMNOPQRSTUVW";
        Directory.CreateDirectory(InProject("shared-notes"));
        File.WriteAllText(InProject("shared-notes/token.txt"), $"api_key = {token}\n");
        DirectoryLink(Context("notes"), InProject("shared-notes"));

        var diagnostics = Validate();

        Assert.Single(diagnostics, d => d.Code == DiagnosticCodes.PossibleSecret);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCodes.SecretScanIncomplete);
    }

    [Fact]
    public void Validate_DirectoryLinkCycleInsideTheContext_Terminates()
    {
        DirectoryLink(Context("loop"), Context(string.Empty).TrimEnd(Path.DirectorySeparatorChar));

        var diagnostics = Validate();

        Assert.DoesNotContain(diagnostics, d => d.IsError);
    }
}
