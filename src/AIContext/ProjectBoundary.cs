namespace AIContext;

// A directory that has its own .aicontext/ is a separate project (D11). Export of an enclosing project
// must neither plan, write nor remove files inside it, however the directory is reached: a junction or symlink
// in the enclosing project can point straight into the separate project, so the real location counts too.
internal static class ProjectBoundary
{
    private const string ContextDirectoryName = ".aicontext";

    private static readonly char[] Separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    public static bool IsProjectRoot(string directory) =>
        Directory.Exists(Path.Combine(directory, ContextDirectoryName));

    // The first directory on the way from basePath down to relativeDirectory (itself included) that is a separate
    // project, as a '/'-separated path relative to basePath; null when there is none. The path as written is
    // checked first, then the real location with every link resolved (then relative to the real root), under every
    // reading of ".." in a link target (see LinkTargetDots), because a link can lead into the separate project under
    // only one of them.
    public static string? FindNestedProject(string basePath, string relativeDirectory)
    {
        var current = basePath;
        var relative = new List<string>();
        foreach (var segment in relativeDirectory.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            relative.Add(segment);
            if (IsProjectRoot(current))
            {
                return string.Join('/', relative);
            }
        }

        try
        {
            foreach (var dots in ExportPathGuard.Readings)
            {
                var found = FindProjectBetween(
                    ExportPathGuard.ResolveRealPath(basePath, dots),
                    ExportPathGuard.ResolveRealPath(ExportTarget.ResolveRelative(basePath, relativeDirectory), dots));
                if (found is not null)
                {
                    return found;
                }
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // The first directory below realRoot, down to realDirectory (itself included), that is a separate project, as a
    // '/'-separated path relative to realRoot. Null when there is none or realDirectory is not inside realRoot.
    // The root itself is the project being exported and never counts. Both paths must already be resolved.
    public static string? FindProjectBetween(string realRoot, string realDirectory)
    {
        if (!ExportPathGuard.IsInside(realRoot, realDirectory))
        {
            return null;
        }

        var current = realRoot.TrimEnd(Separators);
        var relative = new List<string>();
        foreach (var segment in realDirectory.TrimEnd(Separators)[current.Length..].Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            relative.Add(segment);
            if (IsProjectRoot(current))
            {
                return string.Join('/', relative);
            }
        }

        return null;
    }
}
