namespace AIContext;

// Decides whether a generated file may be written or deleted (D12): the real location of the file, with every
// symlink and junction on the way resolved, must be inside the real project root. Links that stay inside the
// root are allowed. Both sides are resolved, so a project that is itself reached through a link still works.
internal sealed class ExportPathGuard
{
    private const int MaxLinkDepth = 40;

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static readonly char[] Separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    private readonly string _root;
    private string? _realRoot;

    public ExportPathGuard(string root)
    {
        _root = root;
    }

    // Null when the path is safe; otherwise a short description of why it must not be touched.
    public string? FindProblem(string path)
    {
        try
        {
            _realRoot ??= ResolveRealPath(_root);
            var real = ResolveRealPath(path);
            return IsInside(_realRoot, real)
                ? null
                : $"resolves to '{real}', outside the project, through a link";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"could not be checked for links ({ex.Message})";
        }
    }

    private static string ResolveRealPath(string path) => Resolve(Path.GetFullPath(path), 0);

    // Walks the path from its root and restarts with the link target whenever a component is a link.
    // Components that do not exist yet cannot be links, so the rest of the path is kept as is.
    private static string Resolve(string fullPath, int depth)
    {
        if (depth > MaxLinkDepth)
        {
            throw new IOException($"Too many levels of links while resolving '{fullPath}'.");
        }

        var root = Path.GetPathRoot(fullPath) ?? string.Empty;
        var segments = fullPath[root.Length..].Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        var current = root;

        for (var i = 0; i < segments.Length; i++)
        {
            current = Path.Combine(current, segments[i]);
            var target = ReadLinkTarget(current);
            if (target is null)
            {
                continue;
            }

            var rest = segments.Skip(i + 1).Prepend(target).ToArray();
            return Resolve(Path.GetFullPath(Path.Combine(rest)), depth + 1);
        }

        return current;
    }

    // One hop of a symlink or junction, as an absolute path; null when the path is not a link or does not exist.
    private static string? ReadLinkTarget(string path)
    {
        var target = new DirectoryInfo(path).LinkTarget ?? new FileInfo(path).LinkTarget;
        if (target is null)
        {
            return null;
        }

        // Windows can store a link target with the extended-length prefix.
        if (target.StartsWith(@"\\?\", StringComparison.Ordinal) && !target.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            target = target[4..];
        }

        return Path.GetFullPath(target, Path.GetDirectoryName(path)!);
    }

    private static bool IsInside(string root, string path)
    {
        var trimmedRoot = root.TrimEnd(Separators);
        return string.Equals(path.TrimEnd(Separators), trimmedRoot, PathComparison) ||
               path.StartsWith(trimmedRoot + Path.DirectorySeparatorChar, PathComparison);
    }
}
