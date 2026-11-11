using AIContext;
using Xunit;

namespace AIContext.Tests;

// Tests of how export decides that a generated file stays inside the project, through links that lead somewhere
// else than their text suggests. Every test runs the real export (plan, then apply the way the CLI does) and checks
// what ended up on disk, not what the path resolver reports; a refusal is checked on top of that. They reproduced
// the weaknesses before the guard was fixed and fail again if one of them returns.
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

    protected static void AssertRefused(ExportPlan plan, string code) =>
        Assert.Contains(plan.Diagnostics, d => d.IsError && d.Code == code);

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

// A link target such as "alias/../landing" can mean two things: the file system follows alias (itself a link) first
// and then goes to the parent of where alias really leads (POSIX), or "alias/.." is cancelled in the text first
// (what Windows is believed to do). The guard used to cancel it in the text on every platform, so on POSIX it
// checked a different path than the one that was written. It now accepts a path only when it is safe under both
// readings. Needs symbolic links with relative targets.
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

        var plan = Export(_project);

        Assert.Empty(TestLinks.Entries(landing));
        AssertRefused(plan, DiagnosticCodes.PathOutsideProject);
    }

    [LinkFact(LinkCapability.DirectorySymlink)]
    public void Export_LinkTargetGoesBackUpFromAnAliasLeavingTheProject_DoesNotUpdateTheFileOutside()
    {
        var landing = Combine(_outside, "deep/landing");
        Directory.CreateDirectory(landing);
        var external = Path.Combine(landing, "copilot-instructions.md");
        WriteGenerated(external);
        CreateAliasAndLink(Combine(_outside, "deep/dir"), ".github");

        var plan = Export(_project);

        Assert.Equal(GeneratedFile, File.ReadAllText(external));
        AssertRefused(plan, DiagnosticCodes.PathOutsideProject);
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

        // Only the file system result is asserted: where the platform reads "alias/.." textually (Windows), the
        // orphan is not even visible through the link, so there is no destination for the guard to refuse.
        AssertKept(orphan);
    }

    [LinkFact(LinkCapability.DirectorySymlink)]
    public void Export_LinkTargetGoesBackUpFromAnAliasIntoASeparateProject_CreatesNothingThere()
    {
        var child = CreateSeparateProject();
        CreateAliasAndLink(Path.Combine(child, "src"), ".github");

        var plan = Export(_project);

        Assert.Empty(TestLinks.Entries(Path.Combine(child, "landing")));
        AssertRefused(plan, DiagnosticCodes.PathInSeparateProject);
    }

    [LinkFact(LinkCapability.DirectorySymlink)]
    public void Export_LinkTargetGoesBackUpFromAnAliasIntoASeparateProject_DoesNotDeleteItsGeneratedFile()
    {
        var child = CreateSeparateProject();
        var orphan = Path.Combine(child, "landing", "aicontext-old.instructions.md");
        WriteGenerated(orphan);
        CreateAliasAndLink(Path.Combine(child, "src"), ".github/instructions");

        Export(_project, "split");

        // See the delete test above: no refusal is asserted because the orphan is not visible on every platform.
        AssertKept(orphan);
    }

    [LinkFact(LinkCapability.DirectorySymlink)]
    public void Plan_RuleScopedToCompoundLinkIntoASeparateProject_StaysInRootFile()
    {
        var child = CreateSeparateProject();
        CreateAliasAndLink(Path.Combine(child, "src"), "scoped");
        var rule = new RuleDocument("scoped", null, ["scoped/**"], "Body.");

        var plan = NestedAgentsPlanner.Plan([rule], _project);

        Assert.Empty(plan.ByDirectory);
        Assert.Contains("'pkg/child'", Assert.Single(plan.Kept).Reason);
    }

    [LinkFact(LinkCapability.DirectorySymlink)]
    public void Apply_LinkBecameCompoundTargetLeavingTheProjectAfterPlanning_WritesNothingOutside()
    {
        var landing = Combine(_outside, "deep/landing");
        Directory.CreateDirectory(landing);
        Directory.CreateDirectory(Combine(_project, ".github"));
        var plan = ExportService.Plan(new ExportRequest(_project, "copilot"));
        Assert.False(plan.HasErrors);
        Directory.Delete(Combine(_project, ".github"));
        CreateAliasAndLink(Combine(_outside, "deep/dir"), ".github");

        var result = ExportService.Apply(plan);

        Assert.Equal(DiagnosticCodes.PathOutsideProject, Assert.Single(result.Diagnostics).Code);
        Assert.Empty(TestLinks.Entries(landing));
    }

    [LinkFact(LinkCapability.DirectorySymlink)]
    public void Apply_LinkBecameCompoundTargetIntoASeparateProjectAfterPlanning_WritesNothingThere()
    {
        var child = CreateSeparateProject();
        Directory.CreateDirectory(Combine(_project, ".github"));
        var plan = ExportService.Plan(new ExportRequest(_project, "copilot"));
        Assert.False(plan.HasErrors);
        Directory.Delete(Combine(_project, ".github"));
        CreateAliasAndLink(Path.Combine(child, "src"), ".github");

        var result = ExportService.Apply(plan);

        Assert.Equal(DiagnosticCodes.PathInSeparateProject, Assert.Single(result.Diagnostics).Code);
        Assert.Empty(TestLinks.Entries(Path.Combine(child, "landing")));
    }

    // Supported links must keep working: here both readings of the target end inside the project, so the file is
    // written there whichever one the platform applies.
    [LinkFact(LinkCapability.DirectorySymlink)]
    public void Export_CompoundTargetThatStaysInsideUnderBothReadings_IsAllowed()
    {
        Directory.CreateDirectory(Combine(_project, "a/b"));
        Directory.CreateDirectory(Combine(_project, "a/landing"));
        CreateAliasAndLink(Combine(_project, "a/b"), ".github");

        var plan = Export(_project);

        Assert.False(plan.HasErrors);
        var written = new[] { Combine(_project, "a/landing"), Combine(_project, "landing") }
            .Where(directory => File.Exists(Path.Combine(directory, "copilot-instructions.md")))
            .ToList();
        Assert.Single(written);
    }

    [LinkFact(LinkCapability.DirectorySymlink)]
    public void Export_RelativeLinkWithLeadingDotDotInsideTheProject_StillDeletesTheGeneratedFileBehindIt()
    {
        Directory.CreateDirectory(Combine(_project, "shared-rules"));
        var orphan = Combine(_project, "shared-rules/aicontext-old.instructions.md");
        WriteGenerated(orphan);
        DirectorySymlink(Combine(_project, ".github/instructions"), Path.Combine("..", "shared-rules"));

        var plan = Export(_project, "split");

        Assert.False(plan.HasErrors);
        Assert.False(File.Exists(orphan));
    }
}

// Paths that differ only by letter case are the same path on a case-insensitive volume and different ones inside a
// case-sensitive directory, which Windows and macOS can have next to case-insensitive ones. The guard used to decide
// by operating system name, so on Windows a link to "Proj" looked like a link into the project "proj". It now asks
// the directory listing which entry a spelling stands for.
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

        var plan = Export(_project);

        Assert.Empty(TestLinks.Entries(_sibling));
        AssertRefused(plan, DiagnosticCodes.PathOutsideProject);
    }

    [LinkFact(LinkCapability.CaseSensitiveDirectory)]
    public void Export_OutputDirectoryLinksToSiblingDifferingOnlyByCase_DoesNotUpdateItsFile()
    {
        var external = Path.Combine(_sibling, "copilot-instructions.md");
        WriteGenerated(external);
        DirectoryLink(Combine(_project, ".github"), _sibling);

        var plan = Export(_project);

        Assert.Equal(GeneratedFile, File.ReadAllText(external));
        AssertRefused(plan, DiagnosticCodes.PathOutsideProject);
    }

    [LinkFact(LinkCapability.CaseSensitiveDirectory)]
    public void Export_RuleDirectoryLinksToSiblingDifferingOnlyByCase_DoesNotDeleteItsGeneratedFile()
    {
        var orphan = Path.Combine(_sibling, "aicontext-old.instructions.md");
        WriteGenerated(orphan);
        DirectoryLink(Combine(_project, ".github/instructions"), _sibling);

        var plan = Export(_project, "split");

        AssertKept(orphan);
        AssertRefused(plan, DiagnosticCodes.PathOutsideProject);
    }

    [LinkFact(LinkCapability.CaseSensitiveDirectory)]
    public void Apply_DirectoryBecameLinkToSiblingDifferingOnlyByCaseAfterPlanning_WritesNothingThere()
    {
        var plan = ExportService.Plan(new ExportRequest(_project, "copilot"));
        Assert.False(plan.HasErrors);
        DirectoryLink(Combine(_project, ".github"), _sibling);

        var result = ExportService.Apply(plan);

        Assert.Equal(DiagnosticCodes.PathOutsideProject, Assert.Single(result.Diagnostics).Code);
        Assert.Empty(TestLinks.Entries(_sibling));
    }

    [LinkFact(LinkCapability.CaseSensitiveDirectory)]
    public void Export_LinkInsideTheProjectInTheSameCase_IsAllowed()
    {
        Directory.CreateDirectory(Combine(_project, "real-docs"));
        DirectoryLink(Combine(_project, ".github"), Combine(_project, "real-docs"));

        var plan = Export(_project);

        Assert.False(plan.HasErrors);
        Assert.True(File.Exists(Combine(_project, "real-docs/copilot-instructions.md")));
    }
}

// The other half of the case policy: on a case-insensitive volume a link target or a project path spelled with other
// letter case than the one on disk still leads into the same project, and must not be refused.
public sealed class ExportCaseInsensitivePathTests : RealPathFixture
{
    private readonly string _project;

    public ExportCaseInsensitivePathTests()
    {
        _project = CreateProject(Combine(Root, "Project"));
        Directory.CreateDirectory(Combine(_project, "real-docs"));
    }

    [LinkFact(LinkCapability.CaseInsensitiveDirectory)]
    public void Export_LinkTargetSpelledWithOtherCaseThanTheProject_IsAllowed()
    {
        DirectoryLink(Combine(_project, ".github"), Combine(Root, "PROJECT/real-docs"));

        var plan = Export(_project);

        Assert.False(plan.HasErrors);
        Assert.True(File.Exists(Combine(_project, "real-docs/copilot-instructions.md")));
    }

    [LinkFact(LinkCapability.CaseInsensitiveDirectory)]
    public void Export_ProjectPathSpelledWithOtherCaseThanTheLinkTarget_IsAllowed()
    {
        DirectoryLink(Combine(_project, ".github"), Combine(_project, "real-docs"));

        var plan = Export(Combine(Root, "PROJECT"));

        Assert.False(plan.HasErrors);
        Assert.True(File.Exists(Combine(_project, "real-docs/copilot-instructions.md")));
    }

    [LinkFact(LinkCapability.CaseInsensitiveDirectory)]
    public void Export_LinkIntoSeparateProjectSpelledWithOtherCase_IsStillBlocked()
    {
        Directory.CreateDirectory(Combine(_project, "pkg/child/.aicontext"));
        DirectoryLink(Combine(_project, ".github"), Combine(Root, "PROJECT/PKG/CHILD"));

        var plan = Export(_project);

        AssertRefused(plan, DiagnosticCodes.PathInSeparateProject);
        Assert.Equal([".aicontext"], TestLinks.Entries(Combine(_project, "pkg/child")));
    }
}
