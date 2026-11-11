namespace AIContext;

// A value in the frontmatter subset aicontext understands: a scalar or a list of scalars.
public sealed record FrontmatterValue(string? Scalar, IReadOnlyList<string>? List)
{
    public bool IsList => List is not null;
}

public sealed record FrontmatterResult(
    IReadOnlyDictionary<string, FrontmatterValue> Values,
    string Body,
    string? Error);

// Deliberately lenient subset of YAML: authors often write unquoted globs such as **/*.ts,
// which strict YAML rejects (a leading '*' is an alias).
public static class Frontmatter
{
    public static FrontmatterResult Parse(string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var values = new Dictionary<string, FrontmatterValue>(StringComparer.Ordinal);

        if (!normalized.StartsWith("---\n", StringComparison.Ordinal))
        {
            return new FrontmatterResult(values, normalized, null);
        }

        var lines = normalized.Split('\n');
        var closing = -1;
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].TrimEnd() == "---")
            {
                closing = i;
                break;
            }
        }

        if (closing < 0)
        {
            return new FrontmatterResult(values, normalized, "frontmatter is not closed with '---'.");
        }

        var body = string.Join('\n', lines.Skip(closing + 1));
        string? currentListKey = null;
        List<string>? currentList = null;

        void FlushList()
        {
            if (currentListKey is not null && currentList is { Count: > 0 })
            {
                values[currentListKey] = new FrontmatterValue(null, currentList);
            }

            currentListKey = null;
            currentList = null;
        }

        for (var i = 1; i < closing; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            var trimmed = line.TrimStart();
            if (currentList is not null && trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                currentList.Add(Unquote(trimmed[2..].Trim()));
                continue;
            }

            FlushList();

            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (char.IsWhiteSpace(line[0]) || colon <= 0)
            {
                return new FrontmatterResult(values, body, $"unsupported frontmatter line '{SecretScanner.Redact(line.Trim())}'.");
            }

            var key = line[..colon].Trim();
            var rest = line[(colon + 1)..].Trim();

            if (rest.Length == 0)
            {
                currentListKey = key;
                currentList = new List<string>();
                values[key] = new FrontmatterValue(string.Empty, null);
            }
            else if (rest.StartsWith('[') && rest.EndsWith(']'))
            {
                var items = SplitTopLevel(rest[1..^1]).Select(Unquote).Where(x => x.Length > 0).ToList();
                values[key] = new FrontmatterValue(null, items);
            }
            else
            {
                values[key] = new FrontmatterValue(Unquote(rest), null);
            }
        }

        FlushList();
        return new FrontmatterResult(values, body, null);
    }

    // Splits on commas that are outside quotes and outside {braces}, so "*.{ts,tsx}" stays intact.
    public static IReadOnlyList<string> SplitTopLevel(string value)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        var depth = 0;
        char? quote = null;

        foreach (var c in value)
        {
            if (quote is not null)
            {
                current.Append(c);
                if (c == quote)
                {
                    quote = null;
                }

                continue;
            }

            switch (c)
            {
                case '"' or '\'':
                    quote = c;
                    current.Append(c);
                    break;
                case '{':
                    depth++;
                    current.Append(c);
                    break;
                case '}':
                    depth = Math.Max(0, depth - 1);
                    current.Append(c);
                    break;
                case ',' when depth == 0:
                    parts.Add(current.ToString().Trim());
                    current.Clear();
                    break;
                default:
                    current.Append(c);
                    break;
            }
        }

        parts.Add(current.ToString().Trim());
        return parts;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            return value[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
        }

        if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
        {
            return value[1..^1].Replace("''", "'", StringComparison.Ordinal);
        }

        return value;
    }
}
