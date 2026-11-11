using System.Diagnostics;
using Xunit;

namespace AIContext.Tests;

// Creates the links the export safety tests need. Junctions work without privileges on Windows; symbolic links
// there need one. A test that needs a symbolic link or a case-sensitive directory declares it with
// [LinkFact(...)] (see TestCapabilities), so a host that cannot provide it reports the test as skipped. These
// helpers never skip and never return early: when a link cannot be created they fail.
internal static class TestLinks
{
    // A junction on Windows (absolute target only), a symbolic link elsewhere.
    public static void CreateDirectoryLink(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }

        var (exitCode, _) = Run("cmd.exe", "/c", "mklink", "/J", link, target);
        Assert.Equal(0, exitCode);
    }

    // A symbolic link to a directory that stores the target as written, so it may be relative and contain "..".
    // Needs LinkCapability.DirectorySymlink.
    public static void CreateDirectorySymlink(string link, string target) => Directory.CreateSymbolicLink(link, target);

    // Needs LinkCapability.FileSymlink. The target may not exist (a dangling link).
    public static void CreateFileLink(string link, string target) => File.CreateSymbolicLink(link, target);

    // A second name for an existing file: both names share one file, so a write through either is visible through
    // the other. Hard links are not in the .NET API; the platform command creates one. Needs
    // LinkCapability.HardLink and both names on the same volume.
    public static (int ExitCode, string Output) TryCreateHardLink(string link, string existing) =>
        OperatingSystem.IsWindows()
            ? Run("cmd.exe", "/c", "mklink", "/H", link, existing)
            : Run("ln", existing, link);

    public static void CreateHardLink(string link, string existing)
    {
        var (exitCode, output) = TryCreateHardLink(link, existing);
        Assert.True(exitCode == 0, output);
    }

    // Creates an empty directory in which paths that differ only by letter case are different entries. Windows
    // marks the one directory with fsutil (a per-directory NTFS attribute, no host setting and no elevation);
    // Linux directories already behave that way. Returns false and the reason when it cannot be done.
    public static bool TryCreateCaseSensitiveDirectory(string path, out string? reason)
    {
        Directory.CreateDirectory(path);
        reason = null;
        if (!OperatingSystem.IsWindows())
        {
            return true;
        }

        try
        {
            var (exitCode, output) = Run("fsutil.exe", "file", "setCaseSensitiveInfo", path, "enable");
            if (exitCode != 0)
            {
                reason = $"fsutil could not mark the directory case-sensitive ({output.Trim()})";
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            reason = $"fsutil is not available ({ex.Message})";
        }

        return reason is null;
    }

    // The names in a directory, sorted, so a failed assertion shows what appeared there.
    public static string[] Entries(string directory) =>
        Directory.GetFileSystemEntries(directory)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static (int ExitCode, string Output) Run(string fileName, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        output += process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }

    // Removes the link itself, never what it points to. Windows junctions are directories; on Unix every link,
    // including one to a directory, is removed like a file.
    public static void Remove(string link)
    {
        if (OperatingSystem.IsWindows() && Directory.Exists(link))
        {
            Directory.Delete(link, recursive: false);
        }
        else if (new FileInfo(link).LinkTarget is not null || File.Exists(link))
        {
            File.Delete(link);
        }
    }
}
