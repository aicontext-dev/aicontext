using Xunit;

namespace AIContext.Tests;

// Link and file system features the safety tests need that not every host provides.
public enum LinkCapability
{
    // File symbolic links; on Windows they need an elevated shell or Developer Mode.
    FileSymlink,
    // Directory symbolic links with a relative target; Windows junctions cannot express them.
    DirectorySymlink,
    // A directory in which paths that differ only by letter case are different entries.
    CaseSensitiveDirectory,
    // A directory in which paths that differ only by letter case are the same entry (the default on Windows and macOS).
    CaseInsensitiveDirectory,
    // A file with the read-only attribute really cannot be opened for writing by this account. A privileged Unix
    // account ignores it.
    ReadOnlyFile,
    // A directory without write permission really does not allow creating a file in it (Unix permission bits; a
    // privileged account ignores them).
    ReadOnlyDirectory,
    // Unix permission bits (File.GetUnixFileMode); not available on Windows.
    UnixFileMode,
    // Hard links: the .NET API has none, so the platform command creates one (mklink /H on Windows, ln elsewhere).
    // Both names must be on the same volume.
    HardLink
}

// Probes the capabilities once per test run, in a temporary directory, without changing host settings.
internal static class TestCapabilities
{
    // CI sets this to a comma-separated list of capability names (see Name) the job must really provide.
    public const string RequiredVariable = "AICONTEXT_REQUIRE_LINK_CAPABILITIES";

    private static readonly Lazy<IReadOnlyDictionary<LinkCapability, string?>> Results = new(Probe);

    public static string Name(LinkCapability capability) => capability switch
    {
        LinkCapability.FileSymlink => "file-symlink",
        LinkCapability.DirectorySymlink => "directory-symlink",
        LinkCapability.CaseSensitiveDirectory => "case-sensitive-directory",
        LinkCapability.CaseInsensitiveDirectory => "case-insensitive-directory",
        LinkCapability.ReadOnlyFile => "read-only-file",
        LinkCapability.ReadOnlyDirectory => "read-only-directory",
        LinkCapability.UnixFileMode => "unix-file-mode",
        LinkCapability.HardLink => "hard-link",
        _ => throw new ArgumentOutOfRangeException(nameof(capability))
    };

    // Null when the capability is available; otherwise why it is not.
    public static string? Unavailable(LinkCapability capability) => Results.Value[capability];

    // The reason for skipping a test that needs all the given capabilities; null when it can run.
    public static string? SkipReason(IEnumerable<LinkCapability> required)
    {
        var reasons = required
            .Select(capability => Unavailable(capability) is { } reason ? $"{Name(capability)}: {reason}" : null)
            .OfType<string>()
            .ToList();
        return reasons.Count == 0 ? null : "Not executed on this host. " + string.Join("; ", reasons);
    }

    // The capabilities the environment demands, or null when it demands none.
    public static IReadOnlyList<string>? RequiredByEnvironment()
    {
        var value = Environment.GetEnvironmentVariable(RequiredVariable);
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static IReadOnlyDictionary<LinkCapability, string?> Probe()
    {
        var directory = Path.Combine(Path.GetTempPath(), "aicontext-capabilities-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            return new Dictionary<LinkCapability, string?>
            {
                [LinkCapability.FileSymlink] = ProbeFileSymlink(directory),
                [LinkCapability.DirectorySymlink] = ProbeDirectorySymlink(directory),
                [LinkCapability.CaseSensitiveDirectory] = ProbeCaseSensitiveDirectory(directory),
                [LinkCapability.CaseInsensitiveDirectory] = ProbeCaseInsensitiveDirectory(directory),
                [LinkCapability.ReadOnlyFile] = ProbeReadOnlyFile(directory),
                [LinkCapability.ReadOnlyDirectory] = ProbeReadOnlyDirectory(directory),
                [LinkCapability.UnixFileMode] = OperatingSystem.IsWindows() ? "Windows has no Unix permission bits" : null,
                [LinkCapability.HardLink] = ProbeHardLink(directory)
            };
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // The message without the probe's temporary path, which only makes the skip reason different on every run.
    private static string Cause(Exception ex)
    {
        var message = ex.Message.TrimEnd();
        var path = message.IndexOf(" : '", StringComparison.Ordinal);
        return (path < 0 ? message : message[..path]).TrimEnd('.');
    }

    private static string? ProbeFileSymlink(string directory)
    {
        var target = Path.Combine(directory, "file-target.txt");
        var link = Path.Combine(directory, "file-link.txt");
        File.WriteAllText(target, "probe");
        try
        {
            File.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return $"file symbolic links cannot be created ({Cause(ex)})";
        }

        var follows = File.ReadAllText(link) == "probe";
        TestLinks.Remove(link);
        return follows ? null : "a file symbolic link does not lead to its target";
    }

    private static string? ProbeDirectorySymlink(string directory)
    {
        var target = Path.Combine(directory, "directory-target");
        var link = Path.Combine(directory, "directory-link");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "probe.txt"), "probe");
        try
        {
            Directory.CreateSymbolicLink(link, "directory-target");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return $"directory symbolic links cannot be created ({Cause(ex)})";
        }

        var follows = File.Exists(Path.Combine(link, "probe.txt"));
        TestLinks.Remove(link);
        return follows ? null : "a directory symbolic link does not lead to its target";
    }

    private static string? ProbeCaseSensitiveDirectory(string directory)
    {
        var probe = Path.Combine(directory, "case-sensitive");
        if (!TestLinks.TryCreateCaseSensitiveDirectory(probe, out var reason))
        {
            return reason;
        }

        Directory.CreateDirectory(Path.Combine(probe, "a"));
        Directory.CreateDirectory(Path.Combine(probe, "A"));
        return Directory.GetDirectories(probe).Length == 2
            ? null
            : "the temporary directory treats paths that differ only by letter case as the same";
    }

    // The default temporary directory, without making anything case-sensitive.
    private static string? ProbeCaseInsensitiveDirectory(string directory)
    {
        var probe = Path.Combine(directory, "case-insensitive");
        Directory.CreateDirectory(Path.Combine(probe, "a"));
        return Directory.Exists(Path.Combine(probe, "A"))
            ? null
            : "the temporary directory distinguishes paths that differ only by letter case";
    }

    private static string? ProbeHardLink(string directory)
    {
        var original = Path.Combine(directory, "hard-original.txt");
        var link = Path.Combine(directory, "hard-link.txt");
        File.WriteAllText(original, "probe");
        try
        {
            var (exitCode, output) = TestLinks.TryCreateHardLink(link, original);
            if (exitCode != 0)
            {
                return $"hard links cannot be created ({output.Trim()})";
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return $"the command that creates hard links is not available ({ex.Message})";
        }

        File.AppendAllText(original, "!");
        return File.ReadAllText(link) == "probe!" ? null : "a hard link does not share the content of its original";
    }

    private static string? ProbeReadOnlyFile(string directory)
    {
        var file = Path.Combine(directory, "read-only.txt");
        File.WriteAllText(file, "probe");
        File.SetAttributes(file, FileAttributes.ReadOnly);
        try
        {
            using (new FileStream(file, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
            {
            }

            return "this account can write to a read-only file";
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
    }

    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static string? ProbeReadOnlyDirectoryUnix(string directory)
    {
        var probe = Path.Combine(directory, "read-only-directory");
        Directory.CreateDirectory(probe);
        File.SetUnixFileMode(probe, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            using (new FileStream(Path.Combine(probe, "probe.txt"), FileMode.CreateNew, FileAccess.Write))
            {
            }

            return "this account can create files in a directory without write permission";
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            File.SetUnixFileMode(probe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static string? ProbeReadOnlyDirectory(string directory) =>
        OperatingSystem.IsWindows()
            ? "directories have no write permission bit on Windows"
            : ProbeReadOnlyDirectoryUnix(directory);
}

// xunit 2.x has no way to skip from inside a test; the reason is decided while the test is discovered, so a
// missing capability is reported as a skipped test with a reason and never as a pass.
[AttributeUsage(AttributeTargets.Method)]
public sealed class LinkFactAttribute : FactAttribute
{
    public LinkFactAttribute(params LinkCapability[] required)
    {
        Skip = TestCapabilities.SkipReason(required);
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class LinkTheoryAttribute : TheoryAttribute
{
    public LinkTheoryAttribute(params LinkCapability[] required)
    {
        Skip = TestCapabilities.SkipReason(required);
    }
}

// Runs only when the environment names capabilities it must provide (CI); otherwise it is reported as skipped.
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequiredCapabilitiesFactAttribute : FactAttribute
{
    public RequiredCapabilitiesFactAttribute()
    {
        if (TestCapabilities.RequiredByEnvironment() is null)
        {
            Skip = $"{TestCapabilities.RequiredVariable} is not set; CI sets it to the capabilities the job must provide.";
        }
    }
}
