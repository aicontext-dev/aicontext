namespace AIContext;

public sealed record KeptRule(string Name, string Reason);

public sealed record NestedAgentsPlan(
    IReadOnlyDictionary<string, IReadOnlyList<RuleDocument>> ByDirectory,
    IReadOnlySet<string> NestedRuleNames,
    IReadOnlyList<KeptRule> Kept);

// Maps path-scoped rules to directories so they can live in nested AGENTS.md files.
// Only exact directory scopes are mapped; anything broader or narrower than a directory stays in the
// root file, because a nested file would otherwise apply to more (or fewer) files than the author intended.
public static class NestedAgentsPlanner
{
    private static readonly char[] GlobCharacters = ['*', '?', '[', ']', '{', '}', '!'];

    public static NestedAgentsPlan Plan(IEnumerable<RuleDocument> rules, string basePath)
    {
        var byDirectory = new SortedDictionary<string, List<RuleDocument>>(StringComparer.Ordinal);
        var nested = new HashSet<string>(StringComparer.Ordinal);
        var kept = new List<KeptRule>();

        foreach (var rule in rules.Where(r => r.Kind == RuleKind.Scoped).OrderBy(r => r.Name, StringComparer.Ordinal))
        {
            var directories = new SortedSet<string>(StringComparer.Ordinal);
            string? reason = null;

            foreach (var pattern in rule.Paths)
            {
                var directory = TryMapDirectory(pattern, basePath, out reason);
                if (directory is null)
                {
                    break;
                }

                directories.Add(directory);
            }

            if (reason is not null || directories.Count == 0)
            {
                kept.Add(new KeptRule(rule.Name, reason ?? "no directory scope"));
                continue;
            }

            nested.Add(rule.Name);
            foreach (var directory in directories)
            {
                if (!byDirectory.TryGetValue(directory, out var list))
                {
                    list = new List<RuleDocument>();
                    byDirectory[directory] = list;
                }

                list.Add(rule);
            }
        }

        return new NestedAgentsPlan(
            byDirectory.ToDictionary(x => x.Key, x => (IReadOnlyList<RuleDocument>)x.Value),
            nested,
            kept);
    }

    private static string? TryMapDirectory(string pattern, string basePath, out string? reason)
    {
        reason = null;
        var value = pattern.Trim();
        if (value.StartsWith("./", StringComparison.Ordinal))
        {
            value = value[2..];
        }

        string directory;
        if (value.EndsWith("/**/*", StringComparison.Ordinal))
        {
            directory = value[..^5];
        }
        else if (value.EndsWith("/**", StringComparison.Ordinal))
        {
            directory = value[..^3];
        }
        else if (value.EndsWith('/'))
        {
            directory = value.TrimEnd('/');
        }
        else
        {
            directory = value;
        }

        var segments = directory.Split('/');
        if (directory.Length == 0 ||
            directory.StartsWith('/') ||
            directory.Contains(':', StringComparison.Ordinal) ||
            directory.IndexOfAny(GlobCharacters) >= 0 ||
            segments.Any(s => s.Length == 0 || s == "." || s == ".."))
        {
            reason = $"pattern '{SecretScanner.Redact(pattern)}' is not a directory scope (use '<directory>/**')";
            return null;
        }

        // Defense in depth: the directory must resolve inside the repository.
        var root = Path.GetFullPath(basePath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(ExportTarget.ResolveRelative(basePath, directory)).StartsWith(root, StringComparison.Ordinal))
        {
            reason = $"pattern '{SecretScanner.Redact(pattern)}' points outside the repository";
            return null;
        }

        var shown = SecretScanner.Redact(directory);
        if (!Directory.Exists(ExportTarget.ResolveRelative(basePath, directory)))
        {
            reason = File.Exists(ExportTarget.ResolveRelative(basePath, directory))
                ? $"'{shown}' is a file, not a directory"
                : $"directory '{shown}' does not exist";
            return null;
        }

        var nestedProject = ProjectBoundary.FindNestedProject(basePath, directory);
        if (nestedProject is not null)
        {
            reason = $"'{shown}' belongs to a separate AIContext project ('{SecretScanner.Redact(nestedProject)}' has its own .aicontext/)";
            return null;
        }

        return directory;
    }
}
