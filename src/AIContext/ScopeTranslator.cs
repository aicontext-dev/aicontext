namespace AIContext;

// The patterns of a scoped rule as one tool can be told them, or the reason it cannot be told them without changing
// what they match.
internal sealed record ScopeTranslation(IReadOnlyList<string> Patterns, string? Problem);

// Turns the source patterns of a rule (SPEC 4.4.2) into what a tool's own format has to contain. A translation is
// made only where the tool's documentation shows that it reads that form the way the source means it; anything else
// is a problem, never a guess, and the caller refuses the export instead of widening or shifting the scope.
//
// What each tool documents (checked against the vendors' pages on 2026-11-11, not run in the tools):
// - Copilot (VS Code `applyTo`) and Cursor (`globs`): one comma-separated string; a pattern without '/' (`*.py`) is
//   root-level only; no way to escape a comma is documented.
// - Claude (`paths`): a YAML list; `*.md` matches at any depth and `/*.md` only in the root; a comma in a list item
//   is part of the pattern.
// - Windsurf and Devin (`globs`): the documentation shows one pattern with a '/' in it and says nothing about several
//   patterns, a separator or a pattern without '/'.
// Braces are expanded before this point, and none of the four documents dot names or letter case, which the
// specification and the user manual list as limits.
internal static class ScopeTranslator
{
    public static ScopeTranslation Translate(ExportTarget target, string ruleName, IReadOnlyList<string> items)
    {
        var patterns = new List<string>();
        foreach (var item in items)
        {
            var parse = PortableGlob.Parse(item);
            if (!parse.IsValid)
            {
                return Refused($"rule '{ruleName}': path '{SecretScanner.Redact(item)}' is not a pattern the specification defines ({parse.Problem})");
            }

            foreach (var pattern in parse.Patterns.Where(p => !patterns.Contains(p, StringComparer.Ordinal)))
            {
                patterns.Add(pattern);
            }
        }

        return target.Style switch
        {
            RuleStyle.Claude => new ScopeTranslation(patterns.Select(RootLevelForClaude).ToList(), null),
            RuleStyle.Copilot or RuleStyle.Cursor => CommaSeparated(target, ruleName, patterns),
            RuleStyle.Windsurf => SinglePattern(target, ruleName, patterns),
            _ => new ScopeTranslation(patterns, null)
        };
    }

    // `*.md` would mean "at any depth" to Claude, while the source means the root only.
    private static string RootLevelForClaude(string pattern) =>
        pattern.Contains('/', StringComparison.Ordinal) ? pattern : "/" + pattern;

    private static ScopeTranslation CommaSeparated(ExportTarget target, string ruleName, List<string> patterns)
    {
        var withComma = patterns.FirstOrDefault(p => p.Contains(',', StringComparison.Ordinal));
        return withComma is null
            ? new ScopeTranslation(patterns, null)
            : Refused($"rule '{ruleName}': pattern '{SecretScanner.Redact(withComma)}' contains a comma, but {target.Name} reads its patterns as one comma-separated text and documents no way to escape a comma, so it would read several patterns instead of one");
    }

    private static ScopeTranslation SinglePattern(ExportTarget target, string ruleName, List<string> patterns)
    {
        if (patterns.Count != 1)
        {
            return Refused($"rule '{ruleName}': {target.Name} documents a single pattern only, and this rule has {patterns.Count} (after brace groups are expanded); use one pattern, or export without {target.Name}");
        }

        var pattern = patterns[0];
        if (!pattern.Contains('/', StringComparison.Ordinal))
        {
            return Refused($"rule '{ruleName}': pattern '{SecretScanner.Redact(pattern)}' matches root-level files only, but {target.Name} does not document how it reads a pattern without '/'; write '**/{SecretScanner.Redact(pattern)}' if any depth is meant, or export without {target.Name}");
        }

        return CommaSeparated(target, ruleName, patterns);
    }

    private static ScopeTranslation Refused(string problem) => new([], problem);
}
