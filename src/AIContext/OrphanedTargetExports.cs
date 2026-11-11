namespace AIContext;

// Generated files of a target that is no longer exported (D17). Export only maintains the targets it is asked to
// export, so after a target is dropped from exports.targets its files stay on disk, are no longer updated, and the
// tool they were made for (or one that also reads them) keeps loading them. They are reported, never deleted.
internal static class OrphanedTargetExports
{
    private const int ListedFiles = 5;

    // One warning (AIC4201) for every target that is not in exportedTargets but still has files that carry the
    // generated header. Files behind a link that leaves the project are not opened.
    public static IReadOnlyList<Diagnostic> Find(string basePath, IReadOnlyCollection<string> exportedTargets)
    {
        var guard = new ExportPathGuard(basePath);
        var diagnostics = new List<Diagnostic>();

        foreach (var target in ExportTargets.All)
        {
            if (exportedTargets.Contains(target.Name))
            {
                continue;
            }

            var files = FindGeneratedFiles(guard, target, basePath);
            if (files.Count == 0)
            {
                continue;
            }

            var relative = files.Select(f => Path.GetRelativePath(basePath, f).Replace('\\', '/')).ToList();
            var listed = string.Join(", ", relative.Take(ListedFiles)) +
                         (relative.Count > ListedFiles ? $" and {relative.Count - ListedFiles} more" : string.Empty);
            var readers = ToolOverlaps.ReadersOf(target.Name);
            var readBy = readers.Count > 0 ? $" (also read by {string.Join(", ", readers)})" : string.Empty;

            diagnostics.Add(Diagnostic.Warning(
                DiagnosticCodes.OrphanedTargetExport,
                $"Target '{target.Name}' is not exported (exports.targets), but its generated files are still on disk: {listed}. " +
                $"They are no longer updated, and AI tools can still load them{readBy}. " +
                $"Delete them, or add '{target.Name}' to exports.targets.",
                relative[0]));
        }

        return diagnostics;
    }

    private static List<string> FindGeneratedFiles(ExportPathGuard guard, ExportTarget target, string basePath)
    {
        var candidates = new SortedSet<string>(StringComparer.Ordinal) { target.ResolvePath(basePath) };

        if (target.RuleDirectory is not null)
        {
            var directory = ExportTarget.ResolveRelative(basePath, target.RuleDirectory);
            if (Directory.Exists(directory))
            {
                try
                {
                    foreach (var file in Directory.EnumerateFiles(directory).ToList())
                    {
                        var name = Path.GetFileName(file);
                        if (name.StartsWith(ExportTargets.GeneratedRulePrefix, StringComparison.Ordinal) &&
                            name.EndsWith(target.RuleExtension, StringComparison.Ordinal))
                        {
                            candidates.Add(file);
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // This only reports leftovers of targets that are not exported and never blocks anything, so a
                    // directory that cannot be listed is skipped (the same as a file that cannot be read below).
                }
            }
        }

        if (target.NestedFileName is not null)
        {
            foreach (var file in ExportWriter.FindFilesByName(basePath, target.NestedFileName, 0))
            {
                candidates.Add(file);
            }
        }

        return candidates.Where(file => IsGeneratedAndSafeToRead(guard, file)).ToList();
    }

    private static bool IsGeneratedAndSafeToRead(ExportPathGuard guard, string file)
    {
        try
        {
            return File.Exists(file) && guard.FindProblem(file) is null && ExportWriter.IsGenerated(File.ReadAllText(file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
