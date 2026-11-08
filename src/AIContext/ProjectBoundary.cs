namespace AIContext;

// A directory that has its own .aicontext/ is a separate project (D11). Export of an enclosing project
// must neither plan, write nor remove files inside it.
internal static class ProjectBoundary
{
    private const string ContextDirectoryName = ".aicontext";

    public static bool IsProjectRoot(string directory) =>
        Directory.Exists(Path.Combine(directory, ContextDirectoryName));

    // The first directory on the way from basePath down to relativeDirectory (itself included) that is a separate
    // project, as a '/'-separated path relative to basePath; null when there is none.
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

        return null;
    }
}
