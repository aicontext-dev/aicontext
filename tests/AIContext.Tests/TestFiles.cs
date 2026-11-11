namespace AIContext.Tests;

// Makes files unwritable or unreadable the way a locked file or a missing permission does, so that the tests can
// check what export and init report. Everything is undone on Dispose, also when an assertion fails, so the temporary
// directory can be deleted afterwards.
internal static class TestFiles
{
    // Holds the file open exclusively (FileShare.None): while it is held, no other open of the file succeeds, which
    // is what the code under test sees when another program has the file locked. Works on Windows and, because .NET
    // takes an advisory lock for it, on Unix.
    public static IDisposable Lock(string path) =>
        new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

    // Sets the read-only attribute (on Unix it removes the write permission bits). Needs LinkCapability.ReadOnlyFile.
    public static IDisposable ReadOnlyFile(string path)
    {
        File.SetAttributes(path, FileAttributes.ReadOnly);
        return new Restore(() => File.SetAttributes(path, FileAttributes.Normal));
    }

    // Removes the write permission of a directory, so that no file can be created in it. Needs
    // LinkCapability.ReadOnlyDirectory.
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public static IDisposable ReadOnlyDirectory(string path)
    {
        var original = File.GetUnixFileMode(path);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        return new Restore(() => File.SetUnixFileMode(path, original));
    }

    private sealed class Restore(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }
}
