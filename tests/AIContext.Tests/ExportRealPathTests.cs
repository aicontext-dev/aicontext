using AIContext;
using Xunit;

namespace AIContext.Tests;

// Reproductions of two weaknesses in how export decides that a generated file stays inside the project. Every
// test runs the real export (plan, then apply the way the CLI does) and checks what ended up on disk, not what the
// path resolver reports. The assertions describe the safe outcome, so a test fails on a host where the weakness
// is real; they are the evidence the planned fix is measured against and are not skipped for that reason.
public abstract class RealPathFixture : IDisposable
{
    protected static readonly string GeneratedFile = $"<!-- {ContextExporter.GeneratedMarker} --target copilot -->\nold\n";

    private readonly List<string> _links = [];

    protected RealPathFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "aicontext-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    protected string Root { get; }

    public void Dispose()
    {
        foreach (var link in Enumerable.Reverse(_links))
        {
            TestLinks.Remove(link);
        }

        Directory.Delete(Root, recursive: true);
    }

    protected static string Combine(string directory, string relative) =>
        Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar));

    protected static string CreateProject(string directory)
    {
        var init = ProjectInitializer.Initialize(directory, "demo", InitProfile.Core, ContextSchema.CanonicalUrl, "0.1.0", force: false);
        Assert.Empty(init.Conflicts);
        return directory;
    }

    // A junction on Windows, a symbolic link elsewhere; the target is absolute.
    protected void DirectoryLink(string link, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        TestLinks.CreateDirectoryLink(link, target);
        _links.Add(link);
    }

    // A symbolic link whose target text is kept as written.
    protected void DirectorySymlink(string link, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        TestLinks.CreateDirectorySymlink(link, target);
        _links.Add(link);
    }

    protected static void WriteGenerated(string path) => File.WriteAllText(path, GeneratedFile);

    protected static void AssertKept(string generatedFile) =>
        Assert.True(File.Exists(generatedFile), "Export deleted a generated file that lies outside the project or in a separate project.");

    // What the CLI does: a plan with errors is never applied. Any error must come from a link being refused;
    // another error would let a test pass without having tried to write anything.
    protected static ExportPlan Export(string project, string? layout = null)
    {
        var plan = ExportService.Plan(new ExportRequest(project, "copilot", layout));
        Assert.DoesNotContain(
            plan.Diagnostics,
            d => d.IsError && d.Code != DiagnosticCodes.PathOutsideProject && d.Code != DiagnosticCodes.PathInSeparateProject);
        if (!plan.HasErrors)
        {
            ExportService.Apply(plan);
        }

        return plan;
    }
}

// A link target such as "alias/../landing" is cancelled lexically to "landing" by the guard, but the file system
// follows alias (itself a link) first and then goes to the parent of where alias really leads. The guard then
// checks a different path than the one that is written. Needs symbolic links with relative targets.
[Trait("PlannedFix", "step-02")]
public sealed class ExportCompoundLinkTests : RealPathFixture
{
    private readonly string _project;
    private readonly string _outside;

    public ExportCompoundLinkTests()
    {
        _project = CreateProject(Combine(Root, "project"));
        _outside = Path.Combine(Root, "outside");
        Directory.CreateDirectory(Combine(_outside, "deep/dir"));
    }

    // project/landing is where the link seems to lead; <aliasTarget>/../landing is where it really leads.
    private void CreateAliasAndLink(string aliasTarget, string relativeLink)
    {
        Directory.CreateDirectory(Combine(_project, "landing"));
        DirectorySymlink(Combine(_project, "alias"), aliasTarget);

        var link = Combine(_project, relativeLink);
        var toProject = Path.GetRelativePath(Path.GetDirectoryName(link)!, _project);
        DirectorySymlink(link, Path.Combine(toProject, "alias", "..", "landing"));
    }

    private string CreateSeparateProject()
    {
        var child = Combine(_project, "pkg/child");
        Directory.CreateDirectory(Path.Combine(child, ".aicontext"));
        Directory.CreateDirectory(Path.Combine(child, "src"));
        Directory.CreateDirectory(Path.Combine(child, "landing"));
        return child;
    }

    [LinkFact(LinkCapability.DirectorySymlink)]
    public void Export_LinkTargetGoesBackUpFromAnAliasLeavingTheProject_CreatesNothingOutside()
    {
        var landing = Combine(_outside, "deep/landing");
        Directory.CreateDirectory(landing);
        CreateAliasAndLink(Combine(_outside, "deep/dir"), ".github");

        Export(_project);

        Assert.Empty(TestLinks.Entries(landing));
    }

    [LinkFact(LinkCapability.DirectorySymlink)]
    public void Export_LinkTargetGoesBackUpFromAnAliasLeavingTheProject_DoesNotUpdateTheFileOutside()
    {
        var landing = Combine(_outside, "deep/landing");
        Directory.CreateDirectory(landing);
        var external = Path.Combine(landing, "copilot-instructions.md");
        WriteGenerated(external);
        CreateAliasAndLink(Combine(_outside, "deep/dir"), ".github");

        Export(_project);

        Assert.Equal(GeneratedFile, File.ReadAllText(external));
    }

    [LinkFact(LinkCapability.DirectorySymlink)]
    public void Export_LinkTargetGoesBackUpFromAnAliasLeavingTheProject_DoesNotDeleteTheGeneratedFileOutside()
    {
        var landing = Combine(_outside, "deep/landing");
        Directory.CreateDirectory(landing);
        var orphan = Path.Combine(landing, "aicontext-old.instructions.md");
        WriteGenerated(orphan);
        CreateAliasAndLink(Combine(_outside, "deep/dir"), ".github/instructions");

        Export(_project, "split");

        AssertKept(orphan);
    }

    [LinkFact(LinkCapability.DirectorySymlink)]
    public void Export_LinkTargetGoesBackUpFromAnAliasIntoASeparateProject_CreatesNothingThere()
    {
        var child = CreateSeparateProject();
        CreateAliasAndLink(Path.Combine(child, "src"), ".github");

        Export(_project);

        Assert.Empty(TestLinks.Entries(Path.Combine(child, "landing")));
    }

    [LinkFact(LinkCapability.DirectorySymlink)]
    public void Export_LinkTargetGoesBackUpFromAnAliasIntoASeparateProject_DoesNotDeleteItsGeneratedFile()
    {
        var child = CreateSeparateProject();
        var orphan = Path.Combine(child, "landing", "aicontext-old.instructions.md");
        WriteGenerated(orphan);
        CreateAliasAndLink(Path.Combine(child, "src"), ".github/instructions");

        Export(_project, "split");

        AssertKept(orphan);
    }
}

// Paths that differ only by letter case are the same path on a case-insensitive volume and different ones inside a
// case-sensitive directory, which Windows and macOS can have next to case-insensitive ones. The guard decides by
// operating system only, so on Windows a link to "Proj" looks like a link into the project "proj".
[Trait("PlannedFix", "step-02")]
public sealed class ExportCaseSensitivePathTests : RealPathFixture
{
    private readonly string _project;
    private readonly string _sibling;

    public ExportCaseSensitivePathTests()
    {
        var directory = Path.Combine(Root, "case-sensitive");
        Assert.True(TestLinks.TryCreateCaseSensitiveDirectory(directory, out var reason), reason);
        _project = CreateProject(Path.Combine(directory, "proj"));
        _sibling = Path.Combine(directory, "Proj");
        Directory.CreateDirectory(_sibling);
        Assert.Equal(2, Directory.GetDirectories(directory).Length);
    }

    [LinkFact(LinkCapability.CaseSensitiveDirectory)]
    public void Export_OutputDirectoryLinksToSiblingDifferingOnlyByCase_CreatesNothingThere()
    {
        DirectoryLink(Combine(_project, ".github"), _sibling);

        Export(_project);

        Assert.Empty(TestLinks.Entries(_sibling));
    }

    [LinkFact(LinkCapability.CaseSensitiveDirectory)]
    public void Export_OutputDirectoryLinksToSiblingDifferingOnlyByCase_DoesNotUpdateItsFile()
    {
        var external = Path.Combine(_sibling, "copilot-instructions.md");
        WriteGenerated(external);
        DirectoryLink(Combine(_project, ".github"), _sibling);

        Export(_project);

        Assert.Equal(GeneratedFile, File.ReadAllText(external));
    }

    [LinkFact(LinkCapability.CaseSensitiveDirectory)]
    public void Export_RuleDirectoryLinksToSiblingDifferingOnlyByCase_DoesNotDeleteItsGeneratedFile()
    {
        var orphan = Path.Combine(_sibling, "aicontext-old.instructions.md");
        WriteGenerated(orphan);
        DirectoryLink(Combine(_project, ".github/instructions"), _sibling);

        Export(_project, "split");

        AssertKept(orphan);
    }
}
