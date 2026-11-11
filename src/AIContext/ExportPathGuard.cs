namespace AIContext;

// A path that must not be touched. Code is the diagnostic code that describes it.
internal sealed record PathProblem(string Code, string Description);

// How ".." inside the target of a link is understood. A POSIX kernel applies it to the real directory reached so
// far, so in the target "alias/../x" it leaves the directory alias points to (Real). Windows is believed to cancel
// "alias/.." in the text before it follows anything (Textual); only the result of the compound-link tests there is
// known, not the mechanism. The guard does not decide by operating system name: a path is accepted only when it is
// safe under both readings.
internal enum LinkTargetDots
{
    Real,
    Textual
}

// Decides whether a generated file may be written or deleted (D12): the real location of the file, with every
// symlink and junction on the way resolved in the order the file system follows them, must be inside the real
// project root, and not inside a separate project (D11) that a link leads to. Links that stay inside the root are
// allowed. Both sides are resolved, so a project that is itself reached through a link still works.
internal sealed class ExportPathGuard
{
    private const int MaxLinkDepth = 40;

    private const string ReadingsDifferNote =
        "; a link target contains '..' after another link, which platforms resolve differently";

    internal static readonly LinkTargetDots[] Readings = [LinkTargetDots.Real, LinkTargetDots.Textual];

    private static readonly char[] Separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    private readonly string _root;
    private readonly Dictionary<LinkTargetDots, string> _realRoots = [];

    public ExportPathGuard(string root)
    {
        _root = root;
    }

    // Null when the path is safe; otherwise why it must not be touched.
    public PathProblem? FindProblem(string path) => Find(path, containmentOnly: false);

    // For the files that are read as the context of the project: null when the real location of the path, with every
    // link resolved, is inside the real project root. Unlike FindProblem it does not care about separate projects, and
    // only link targets are looked at; nothing is opened.
    public PathProblem? FindSourceProblem(string path) => Find(path, containmentOnly: true);

    private PathProblem? Find(string path, bool containmentOnly)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var readings = Readings.Select(dots => (Root: RealRoot(dots), Real: Resolve(full, dots))).Distinct().ToList();
            foreach (var (realRoot, real) in readings)
            {
                var problem = Check(realRoot, real, readings.Count > 1, containmentOnly);
                if (problem is not null)
                {
                    return problem;
                }
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new PathProblem(DiagnosticCodes.PathOutsideProject, $"could not be checked for links ({ex.Message})");
        }
    }

    // FindProblem plus the collisions that make the file impossible to write. Used for every file that is about to be
    // written, so such a destination is known before anything is written. The collision is looked at first: it only
    // asks whether something exists, and its message is the one that tells the user what to move.
    public PathProblem? FindWriteProblem(string path) => FindCollision(path) ?? FindProblem(path);

    // A destination that cannot become a regular file: a directory is already there, or one of the directories above
    // it is a file. What is really on disk decides, not links (FindProblem covers those).
    public PathProblem? FindCollision(string path)
    {
        var full = Path.GetFullPath(path);
        if (Directory.Exists(full))
        {
            return new PathProblem(DiagnosticCodes.DestinationNotAFile, "is a directory, but a file has to be written there");
        }

        for (var directory = Path.GetDirectoryName(full); directory is not null; directory = Path.GetDirectoryName(directory))
        {
            // A link is not a file here: File.Exists is also true for a dangling link on Unix, and where a link
            // leads is decided by FindProblem.
            if (File.Exists(directory) && new FileInfo(directory).LinkTarget is null)
            {
                var shown = Path.GetRelativePath(_root, directory).Replace('\\', '/');
                return new PathProblem(DiagnosticCodes.DestinationNotAFile, $"cannot be written because '{shown}' is a file, not a directory");
            }
        }

        return null;
    }

    private string RealRoot(LinkTargetDots dots)
    {
        if (!_realRoots.TryGetValue(dots, out var realRoot))
        {
            realRoot = ResolveRealPath(_root, dots);
            _realRoots[dots] = realRoot;
        }

        return realRoot;
    }

    private static PathProblem? Check(string realRoot, string real, bool readingsDiffer, bool containmentOnly)
    {
        var note = readingsDiffer ? ReadingsDifferNote : string.Empty;
        if (!IsInside(realRoot, real))
        {
            return new PathProblem(
                DiagnosticCodes.PathOutsideProject,
                $"resolves to '{real}', outside the project, through a link{note}");
        }

        if (containmentOnly)
        {
            return null;
        }

        var directory = Path.GetDirectoryName(real);
        var separateProject = directory is null ? null : ProjectBoundary.FindProjectBetween(realRoot, directory);
        return separateProject is null
            ? null
            : new PathProblem(
                DiagnosticCodes.PathInSeparateProject,
                $"resolves to '{real}', inside the separate project '{separateProject}' (it has its own .aicontext/){note}");
    }

    // The path is first made absolute the way .NET does before any file operation (".." in the text is cancelled), so
    // what is resolved here is the path the writer really hands to the operating system.
    internal static string ResolveRealPath(string path, LinkTargetDots dots) => Resolve(Path.GetFullPath(path), dots);

    // Walks the path one component at a time, from its root. A component that is a link is replaced by the components
    // of its target, which are then walked in turn, so ".." is applied to the directory really reached so far, never
    // to the text of an earlier component (unless the Textual reading asks for that). Components that do not exist
    // yet cannot be links; they are kept as they are.
    private static string Resolve(string fullPath, LinkTargetDots dots)
    {
        var root = Path.GetPathRoot(fullPath) ?? string.Empty;
        var resolved = new List<string>();
        var pending = new Stack<string>(Enumerable.Reverse(Split(fullPath[root.Length..])));
        var followed = 0;

        while (pending.Count > 0)
        {
            var name = pending.Pop();
            if (name == ".")
            {
                continue;
            }

            if (name == "..")
            {
                if (resolved.Count > 0)
                {
                    resolved.RemoveAt(resolved.Count - 1);
                }

                continue;
            }

            var target = ReadLinkTarget(Join(root, resolved.Append(name)));
            if (target is null)
            {
                resolved.Add(name);
                continue;
            }

            if (++followed > MaxLinkDepth)
            {
                throw new IOException($"Too many levels of links while resolving '{fullPath}'.");
            }

            var (targetRoot, parts) = SplitTarget(target, root);
            if (targetRoot is not null)
            {
                root = targetRoot;
                resolved.Clear();
            }

            if (dots == LinkTargetDots.Textual)
            {
                parts = CancelDots(parts, resolved);
            }

            foreach (var part in Enumerable.Reverse(parts))
            {
                pending.Push(part);
            }
        }

        return Join(root, resolved);
    }

    private static string[] Split(string path) => path.Split(Separators, StringSplitOptions.RemoveEmptyEntries);

    private static string Join(string root, IEnumerable<string> components) => Path.Combine(components.Prepend(root).ToArray());

    // Cancels "x/.." in the text of a link target. A ".." with nothing left in the target to cancel goes to the
    // directory that contains the link, which is a real directory and already resolved, so it is applied to it.
    private static string[] CancelDots(string[] parts, List<string> resolved)
    {
        var kept = new List<string>();
        foreach (var part in parts)
        {
            if (part == ".")
            {
                continue;
            }

            if (part != "..")
            {
                kept.Add(part);
            }
            else if (kept.Count > 0)
            {
                kept.RemoveAt(kept.Count - 1);
            }
            else if (resolved.Count > 0)
            {
                resolved.RemoveAt(resolved.Count - 1);
            }
        }

        return kept.ToArray();
    }

    // The text of a symlink or junction target exactly as stored; null when the path is not a link or does not exist.
    private static string? ReadLinkTarget(string path) => new DirectoryInfo(path).LinkTarget ?? new FileInfo(path).LinkTarget;

    // The components of a link target, and the root they start from when the target is absolute (null when the
    // target is relative to the directory that contains the link).
    private static (string? Root, string[] Parts) SplitTarget(string target, string currentRoot)
    {
        // Windows can store a link target with the extended-length prefix.
        if (target.StartsWith(@"\\?\", StringComparison.Ordinal) && !target.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            target = target[4..];
        }

        if (!Path.IsPathRooted(target))
        {
            return (null, Split(target));
        }

        var root = Path.GetPathRoot(target)!;
        if (Path.IsPathFullyQualified(target))
        {
            return (root, Split(target[root.Length..]));
        }

        // "\dir" on Windows means the root of the current drive; "C:dir" depends on a per-drive working directory
        // that cannot be known here, so such a link is refused instead of guessed.
        if (root.Length == 1 && Separators.Contains(root[0]))
        {
            return (currentRoot, Split(target[1..]));
        }

        throw new IOException($"The link target '{target}' is relative to the current directory of a drive and cannot be checked.");
    }

    // Whether path is root or lies below it. Components are compared exactly; two spellings that differ only by
    // letter case are the same entry on a case-insensitive volume and two entries in a case-sensitive directory,
    // which a host can have next to each other, so the operating system's name says nothing. The directory listing
    // decides (see OnDiskName), and only when the spellings differ, so the usual case costs nothing. Anything that
    // cannot be shown to be the same entry counts as outside.
    internal static bool IsInside(string root, string path)
    {
        var rootStart = Path.GetPathRoot(root) ?? string.Empty;
        var pathStart = Path.GetPathRoot(path) ?? string.Empty;
        if (!string.Equals(rootStart, pathStart, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rootParts = Split(root[rootStart.Length..]);
        var pathParts = Split(path[pathStart.Length..]);
        if (pathParts.Length < rootParts.Length)
        {
            return false;
        }

        var directory = rootStart;
        for (var i = 0; i < rootParts.Length; i++)
        {
            if (!string.Equals(rootParts[i], pathParts[i], StringComparison.Ordinal) &&
                !SameEntry(directory, rootParts[i], pathParts[i]))
            {
                return false;
            }

            directory = Path.Combine(directory, rootParts[i]);
        }

        return true;
    }

    private static bool SameEntry(string directory, string first, string second) =>
        string.Equals(first, second, StringComparison.OrdinalIgnoreCase) &&
        OnDiskName(directory, first) is { } name &&
        string.Equals(name, OnDiskName(directory, second), StringComparison.Ordinal);

    // The name under which an entry of the directory is stored: the exact spelling when it exists, otherwise the one
    // entry that matches ignoring case; null when there is none. Several entries that differ only by case and none
    // of them exact cannot be told apart, which is reported as an error.
    private static string? OnDiskName(string directory, string name)
    {
        string? match = null;
        var matches = 0;
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            var entryName = Path.GetFileName(entry);
            if (string.Equals(entryName, name, StringComparison.Ordinal))
            {
                return entryName;
            }

            if (string.Equals(entryName, name, StringComparison.OrdinalIgnoreCase))
            {
                match = entryName;
                matches++;
            }
        }

        return matches > 1
            ? throw new IOException($"'{name}' in '{directory}' matches several entries that differ only by letter case.")
            : match;
    }
}
