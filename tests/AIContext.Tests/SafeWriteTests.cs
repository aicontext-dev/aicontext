using System.Runtime.Versioning;
using System.Security.Cryptography;
using AIContext;
using Xunit;

namespace AIContext.Tests;

// How a single file is written: an existing regular file is replaced through a temporary file so that a failure never
// leaves it half-written (a link is the exception: it is overwritten in place), and a new file is created exclusively
// and removed again if it could not be written completely.
// The failure after the temporary (or new) file exists is injected through the seam the writer offers for tests.
public sealed class SafeWriteTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "aicontext-tests-" + Guid.NewGuid().ToString("N"));

    public SafeWriteTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Target => Path.Combine(_directory, "target.md");

    private string[] Entries => TestLinks.Entries(_directory);

    [Fact]
    public void ReplaceExisting_NewContent_ReplacesTheFileAndLeavesNoTemporaryFile()
    {
        File.WriteAllText(Target, "old");

        ExportWriter.ReplaceExisting(Target, "new");

        Assert.Equal("new", File.ReadAllText(Target));
        Assert.Equal(["target.md"], Entries);
    }

    [Fact]
    public void ReplaceExisting_FailureAfterTheTemporaryFileWasWritten_KeepsTheOriginalAndRemovesTheTemporaryFile()
    {
        File.WriteAllText(Target, "old");
        var entriesWhileWriting = 0;

        var failure = Assert.Throws<IOException>(() => ExportWriter.ReplaceExisting(
            Target,
            "new",
            afterTemporaryFileWritten: () =>
            {
                entriesWhileWriting = Entries.Length;
                throw new IOException("No space left on device.");
            }));

        Assert.Equal("No space left on device.", failure.Message);
        Assert.Equal(2, entriesWhileWriting);
        Assert.Equal("old", File.ReadAllText(Target));
        Assert.Equal(["target.md"], Entries);
    }

    [Fact]
    public void ReplaceExisting_FileChangedSincePlanning_RefusesAndKeepsTheNewerContent()
    {
        File.WriteAllText(Target, "planned");
        var plannedHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Target)));
        File.WriteAllText(Target, "edited by someone else");

        var stale = Assert.Throws<StaleExportPlanException>(() => ExportWriter.ReplaceExisting(Target, "new", plannedHash));

        Assert.Contains("changed after the export was planned", stale.Message);
        Assert.Equal("edited by someone else", File.ReadAllText(Target));
        Assert.Equal(["target.md"], Entries);
    }

    [Fact]
    public void ReplaceExisting_FileIsLocked_FailsWithoutCreatingAnythingAndKeepsTheContent()
    {
        File.WriteAllText(Target, "old");

        using (TestFiles.Lock(Target))
        {
            Assert.ThrowsAny<IOException>(() => ExportWriter.ReplaceExisting(Target, "new"));
        }

        Assert.Equal("old", File.ReadAllText(Target));
        Assert.Equal(["target.md"], Entries);
    }

    // A read-only file in a writable directory would be replaced by a rename; it must not be, as it would not be
    // overwritten in place either.
    [LinkFact(LinkCapability.ReadOnlyFile)]
    public void ReplaceExisting_ReadOnlyFile_IsNotReplaced()
    {
        File.WriteAllText(Target, "old");

        using (TestFiles.ReadOnlyFile(Target))
        {
            Assert.Throws<UnauthorizedAccessException>(() => ExportWriter.ReplaceExisting(Target, "new"));
        }

        Assert.Equal("old", File.ReadAllText(Target));
        Assert.Equal(["target.md"], Entries);
    }

    // Replacing the link itself by a regular file would change where the content lands, so a link is written through.
    [LinkFact(LinkCapability.FileSymlink)]
    public void ReplaceExisting_FileIsALink_WritesThroughItAndKeepsTheLink()
    {
        var real = Path.Combine(_directory, "real.md");
        var link = Path.Combine(_directory, "link.md");
        File.WriteAllText(real, "old");
        TestLinks.CreateFileLink(link, real);

        try
        {
            ExportWriter.ReplaceExisting(link, "new");

            Assert.NotNull(new FileInfo(link).LinkTarget);
            Assert.Equal("new", File.ReadAllText(real));
            Assert.Equal(["link.md", "real.md"], Entries);
        }
        finally
        {
            TestLinks.Remove(link);
        }
    }

    [UnsupportedOSPlatform("windows")]
    [LinkFact(LinkCapability.UnixFileMode)]
    public void ReplaceExisting_UnixFile_KeepsItsPermissionBits()
    {
        File.WriteAllText(Target, "old");
        var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead;
        File.SetUnixFileMode(Target, mode);

        ExportWriter.ReplaceExisting(Target, "new");

        Assert.Equal(mode, File.GetUnixFileMode(Target));
    }

    [Fact]
    public void TryCreateNew_WriteFailsAfterTheFileWasCreated_RemovesTheIncompleteFile()
    {
        Assert.Throws<IOException>(() => ExportWriter.TryCreateNew(
            Target,
            "content",
            afterWritten: () => throw new IOException("No space left on device.")));

        Assert.False(File.Exists(Target));
        Assert.Empty(Entries);
    }

    [Fact]
    public void TryCreateNew_PathIsTaken_ReturnsFalseAndKeepsWhatIsThere()
    {
        File.WriteAllText(Target, "mine");

        Assert.False(ExportWriter.TryCreateNew(Target, "content"));

        Assert.Equal("mine", File.ReadAllText(Target));
    }
}
