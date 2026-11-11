using System.Text;

namespace AIContext;

// One item of `paths` read as a pattern of the portable subset (SPEC 4.4.2). Patterns holds the item with its brace
// group expanded (one pattern when there is none). Problem says why the item is outside the subset; it never repeats
// the item, so the caller decides how to show text that came from a project file.
public sealed record GlobParse(IReadOnlyList<string> Patterns, string? Problem)
{
    public bool IsValid => Problem is null;
}

// The meaning of a path pattern, the same for every tool (SPEC 4.4.2). Deliberately not a glob library: only `*`, `**`
// as a whole segment and one level of `{a,b}` are defined, and everything else that some tool reads as syntax is
// refused instead of being passed on to be read differently.
public static class PortableGlob
{
    // A brace group is a shorthand for separate patterns, so the number of them has to stay bounded.
    public const int MaxExpansions = 64;

    private static readonly char[] Reserved = ['?', '[', ']', '(', ')', '!'];

    public static GlobParse Parse(string item)
    {
        if (string.IsNullOrWhiteSpace(item))
        {
            return Invalid("the pattern is empty");
        }

        if (item.Contains('\\', StringComparison.Ordinal))
        {
            return Invalid("'/' is the only path separator; '\\' is not allowed");
        }

        var reserved = item.IndexOfAny(Reserved);
        if (reserved >= 0)
        {
            return Invalid($"'{item[reserved]}' is reserved: tools read it as glob syntax in different ways, so write the pattern without it (use '*' for part of a name)");
        }

        var expanded = ExpandBraces(item, out var problem);
        if (expanded is null)
        {
            return Invalid(problem!);
        }

        foreach (var pattern in expanded)
        {
            problem = CheckStructure(pattern);
            if (problem is not null)
            {
                return Invalid(problem);
            }
        }

        return new GlobParse(expanded, null);
    }

    // Whether the pattern matches a file. The path is relative to the project root and uses '/' as the separator.
    // An item that is outside the subset matches nothing.
    public static bool IsMatch(string item, string path)
    {
        var parse = Parse(item);
        return parse.IsValid && parse.Patterns.Any(pattern => MatchesExpanded(pattern, path));
    }

    private static GlobParse Invalid(string problem) => new([], problem);

    private static List<string>? ExpandBraces(string item, out string? problem)
    {
        problem = null;
        var parts = new List<List<string>>();
        var literal = new StringBuilder();
        var count = 1;

        for (var i = 0; i < item.Length; i++)
        {
            var c = item[i];
            if (c == '}')
            {
                problem = "'}' has no matching '{'";
                return null;
            }

            if (c != '{')
            {
                literal.Append(c);
                continue;
            }

            var close = item.IndexOf('}', i + 1);
            if (close < 0)
            {
                problem = "'{' is not closed";
                return null;
            }

            var alternatives = item.Substring(i + 1, close - i - 1).Split(',');
            if (alternatives.Any(a => a.Contains('{', StringComparison.Ordinal)))
            {
                problem = "brace groups cannot be nested";
                return null;
            }

            if (alternatives.Length < 2)
            {
                problem = "a brace group needs at least two alternatives, as in {ts,tsx}";
                return null;
            }

            if (alternatives.Any(a => a.Length == 0 || a != a.Trim()))
            {
                problem = "an alternative of a brace group is empty or has spaces around it; write {ts,tsx}";
                return null;
            }

            count *= alternatives.Length;
            if (count > MaxExpansions)
            {
                problem = $"the brace groups expand to more than {MaxExpansions} patterns; list fewer alternatives";
                return null;
            }

            parts.Add([literal.ToString()]);
            literal.Clear();
            parts.Add(alternatives.ToList());
            i = close;
        }

        parts.Add([literal.ToString()]);

        IEnumerable<string> results = [string.Empty];
        foreach (var part in parts)
        {
            results = results.SelectMany(prefix => part.Select(alternative => prefix + alternative)).ToList();
        }

        return results.Distinct(StringComparer.Ordinal).ToList();
    }

    private static string? CheckStructure(string pattern)
    {
        if (pattern.StartsWith('/'))
        {
            return "a pattern must not start with '/': it is relative to the project root";
        }

        if (pattern.EndsWith('/'))
        {
            return "a pattern must not end with '/': write 'dir/**' for everything below a directory";
        }

        var segments = pattern.Split('/');
        foreach (var segment in segments)
        {
            if (segment.Length == 0)
            {
                return "a pattern must not have an empty path segment ('//')";
            }

            if (segment is "." or "..")
            {
                return "a pattern must not contain '.' or '..' segments";
            }

            if (segment != "**" && segment.Contains("**", StringComparison.Ordinal))
            {
                return "'**' must be a whole path segment";
            }
        }

        return segments is ["**"]
            ? "'**' alone is not a pattern: write '**/*' for every file, or remove 'paths' to make the rule always on"
            : null;
    }

    private static bool MatchesExpanded(string pattern, string path) =>
        path.Length > 0 && MatchSegments(pattern.Split('/'), 0, path.Split('/'), 0);

    // '**' in the middle or at the start matches zero or more segments; as the last segment it matches one or more
    // (everything below the directory in front of it, never the directory itself).
    private static bool MatchSegments(string[] pattern, int p, string[] path, int s)
    {
        if (p == pattern.Length)
        {
            return s == path.Length;
        }

        if (pattern[p] == "**")
        {
            if (p == pattern.Length - 1)
            {
                return s < path.Length;
            }

            for (var skip = s; skip < path.Length; skip++)
            {
                if (MatchSegments(pattern, p + 1, path, skip))
                {
                    return true;
                }
            }

            return false;
        }

        return s < path.Length && MatchSegment(pattern[p], path[s]) && MatchSegments(pattern, p + 1, path, s + 1);
    }

    // '*' matches any run of characters in one segment, also a name that starts with a dot. Case is significant.
    private static bool MatchSegment(string pattern, string text)
    {
        int p = 0, t = 0, star = -1, mark = 0;
        while (t < text.Length)
        {
            if (p < pattern.Length && pattern[p] == '*')
            {
                star = p++;
                mark = t;
            }
            else if (p < pattern.Length && pattern[p] == text[t])
            {
                p++;
                t++;
            }
            else if (star >= 0)
            {
                p = star + 1;
                t = ++mark;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*')
        {
            p++;
        }

        return p == pattern.Length;
    }
}
