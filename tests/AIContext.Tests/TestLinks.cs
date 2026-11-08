using System.Diagnostics;
using Xunit;

namespace AIContext.Tests;

// Creates the links the export safety tests need. Junctions work without privileges on Windows; file symlinks
// there need one, so those tests simply return when the link cannot be created.
internal static class TestLinks
{
    public static void CreateDirectoryLink(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }

        var startInfo = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo)!;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    public static bool TryCreateFileLink(string link, string target)
    {
        try
        {
            File.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return false;
        }
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
