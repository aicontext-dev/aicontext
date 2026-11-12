using System.Text;

namespace AIContext;

// A value in the frontmatter subset aicontext understands (SPEC 4.4.1). Parse reads the structure of the block and
// the syntax of each value, but does not decide what a key means: the unknown keys of a rule are ignored whatever
// their value looks like, so a problem is recorded on the value and reported only for a key the caller uses.
//
//   Scalar    the text of a single scalar, unquoted and without a comment; empty when the key has no value.
//   List      the items of a [flow] or a block list.
//   IsNested  the value has a nested or multi-line block that this subset does not interpret.
//   Problem   the syntax of the value is wrong (an unterminated quote, text after a closing quote).
//   Quoted    the scalar was written as one quoted string.
public sealed record FrontmatterValue(
    string? Scalar,
    IReadOnlyList<string>? List,
    bool IsNested = false,
    string? Problem = null,
    bool Quoted = false)
{
    public bool IsList => List is not null;
}

public sealed record FrontmatterResult(
    IReadOnlyDictionary<string, FrontmatterValue> Values,
    string Body,
    string? Error);

// Deliberately a small subset of YAML, not a YAML parser: authors often write unquoted globs such as **/*.ts,
// which strict YAML rejects (a leading '*' is an alias). The grammar is in SPEC 4.4.1.
public static class Frontmatter
{
    public static FrontmatterResult Parse(string text)
    {
        var normalized = LineEndings.Normalize(text);
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

        var index = 1;
        while (index < closing)
        {
            var line = lines[index];
            if (IsBlankOrComment(line))
            {
                index++;
                continue;
            }

            // A key line starts at column 0. Anything else here (an indented line or a list item with no key above
            // it, a line without a colon) is not part of the subset.
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (char.IsWhiteSpace(line[0]) || IsListItem(line) || colon <= 0)
            {
                return Failed($"unsupported frontmatter line '{SecretScanner.Redact(line.Trim())}' (line {index + 1}).");
            }

            var key = line[..colon].Trim();
            if (values.ContainsKey(key))
            {
                return Failed($"duplicate key '{SecretScanner.Redact(key)}' (line {index + 1}); a key may appear only once.");
            }

            // The value of a key continues on the following lines while they are indented or list items.
            var block = new List<string>();
            index++;
            while (index < closing)
            {
                var next = lines[index];
                if (IsBlankOrComment(next))
                {
                    index++;
                    continue;
                }

                if (!char.IsWhiteSpace(next[0]) && !IsListItem(next))
                {
                    break;
                }

                block.Add(next);
                index++;
            }

            values[key] = ReadValue(line[(colon + 1)..], block);
        }

        return new FrontmatterResult(values, body, null);

        FrontmatterResult Failed(string error) => new(values, body, error);
    }

    // The items of a list-valued key such as `paths`: the items of a [flow] or block list, or the parts of a
    // comma-separated string (commas inside {braces} do not separate). Null, with the problem, when the value is not
    // usable. Empty items are returned as empty strings; the caller decides what they mean.
    public static IReadOnlyList<string>? ReadItems(FrontmatterValue value, out string? problem)
    {
        problem = null;
        if (value.Problem is not null)
        {
            problem = value.Problem;
            return null;
        }

        if (value.IsNested)
        {
            problem = "expected a list or a comma-separated string, not a nested or multi-line value.";
            return null;
        }

        if (value.List is not null)
        {
            return value.List;
        }

        var parts = Split(value.Scalar ?? string.Empty, quotes: false);
        if (!value.Quoted && parts.Any(p => p.Length > 0 && IsQuote(p[0])))
        {
            problem = "a quoted item needs the whole list in [ ], or one quoted string around the whole value.";
            return null;
        }

        return parts;
    }

    // Splits on commas that are outside {braces} and outside a quoted item, so "*.{ts,tsx}" stays intact. A quote
    // opens a quoted item only at the start of an item, so an apostrophe inside a name does not swallow the commas.
    public static IReadOnlyList<string> SplitTopLevel(string value) => Split(value, quotes: true);

    private static List<string> Split(string value, bool quotes)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        var depth = 0;
        var quote = '\0';
        var itemStart = true;

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (quote != '\0')
            {
                current.Append(c);
                if (quote == '"' && c == '\\' && i + 1 < value.Length)
                {
                    current.Append(value[++i]);
                }
                else if (c == quote)
                {
                    if (quote == '\'' && i + 1 < value.Length && value[i + 1] == '\'')
                    {
                        current.Append(value[++i]);
                    }
                    else
                    {
                        quote = '\0';
                    }
                }

                continue;
            }

            if (quotes && itemStart && IsQuote(c))
            {
                quote = c;
                itemStart = false;
                current.Append(c);
                continue;
            }

            switch (c)
            {
                case '{':
                    depth++;
                    itemStart = false;
                    current.Append(c);
                    break;
                case '}':
                    depth = Math.Max(0, depth - 1);
                    itemStart = false;
                    current.Append(c);
                    break;
                case ',' when depth == 0:
                    parts.Add(current.ToString().Trim());
                    current.Clear();
                    itemStart = true;
                    break;
                default:
                    if (!char.IsWhiteSpace(c))
                    {
                        itemStart = false;
                    }

                    current.Append(c);
                    break;
            }
        }

        parts.Add(current.ToString().Trim());
        return parts;
    }

    private static FrontmatterValue ReadValue(string rest, List<string> block)
    {
        var inline = ReadInline(rest);
        if (block.Count == 0)
        {
            return inline;
        }

        // `key:` followed by `- item` lines is a block list; any other following lines are a nested or multi-line
        // value, which this subset does not interpret.
        if (inline is { Scalar: "", List: null, Problem: null } && block.All(IsListItem))
        {
            var items = new List<string>();
            foreach (var line in block)
            {
                var item = ReadScalarText(ItemText(line));
                if (item.Problem is not null)
                {
                    return new FrontmatterValue(null, null, Problem: item.Problem);
                }

                items.Add(item.Scalar!);
            }

            return new FrontmatterValue(null, items);
        }

        return new FrontmatterValue(null, null, IsNested: true);
    }

    // The value written after `key:` on the same line.
    private static FrontmatterValue ReadInline(string rest)
    {
        var text = rest.Trim();
        if (text.Length == 0)
        {
            return new FrontmatterValue(string.Empty, null);
        }

        if (IsQuote(text[0]))
        {
            return ReadScalarText(text);
        }

        text = CutComment(text, flow: text[0] == '[');
        if (text.Length == 0)
        {
            return new FrontmatterValue(string.Empty, null);
        }

        if (text[0] == '[' && text[^1] == ']')
        {
            var items = new List<string>();
            foreach (var raw in SplitTopLevel(text[1..^1]))
            {
                var item = ReadFlowItem(raw);
                if (item.Problem is not null)
                {
                    return new FrontmatterValue(null, null, Problem: item.Problem);
                }

                items.Add(item.Scalar!);
            }

            return new FrontmatterValue(null, items);
        }

        return new FrontmatterValue(text, null);
    }

    // One scalar: a quoted string (followed at most by a comment) or plain text up to a comment.
    private static FrontmatterValue ReadScalarText(string text)
    {
        text = text.Trim();
        if (text.Length == 0)
        {
            return new FrontmatterValue(string.Empty, null);
        }

        if (!IsQuote(text[0]))
        {
            return new FrontmatterValue(CutComment(text, flow: false), null);
        }

        var close = FindClosingQuote(text);
        if (close < 0)
        {
            return new FrontmatterValue(null, null, Problem: "the quote that opens the value is not closed.");
        }

        var after = text[(close + 1)..];
        var trailing = after.TrimStart();
        if (trailing.Length > 0 && !(char.IsWhiteSpace(after[0]) && trailing[0] == '#'))
        {
            return new FrontmatterValue(
                null,
                null,
                Problem: "there is text after the closing quote; quote the whole value, or use a [ ] list for several items.");
        }

        return new FrontmatterValue(Unquote(text[..(close + 1)]), null, Quoted: true);
    }

    private static FrontmatterValue ReadFlowItem(string raw)
    {
        var text = raw.Trim();
        if (text.Length == 0 || !IsQuote(text[0]))
        {
            return new FrontmatterValue(text, null);
        }

        var close = FindClosingQuote(text);
        if (close < 0)
        {
            return new FrontmatterValue(null, null, Problem: "the quote that opens a list item is not closed.");
        }

        if (text[(close + 1)..].Trim().Length > 0)
        {
            return new FrontmatterValue(null, null, Problem: "there is text after the closing quote of a list item.");
        }

        return new FrontmatterValue(Unquote(text), null, Quoted: true);
    }

    // Removes a comment: a '#' at the start of the text or after whitespace, outside a quoted item. Quoted items exist
    // only in a [flow] list and are recognized at the start of an item.
    private static string CutComment(string text, bool flow)
    {
        var quote = '\0';
        var itemStart = true;
        var depth = 0;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quote != '\0')
            {
                if (quote == '"' && c == '\\')
                {
                    i++;
                }
                else if (c == quote)
                {
                    if (quote == '\'' && i + 1 < text.Length && text[i + 1] == '\'')
                    {
                        i++;
                    }
                    else
                    {
                        quote = '\0';
                    }
                }

                continue;
            }

            if (c == '#' && (i == 0 || char.IsWhiteSpace(text[i - 1])))
            {
                return text[..i].TrimEnd();
            }

            if (char.IsWhiteSpace(c))
            {
                continue;
            }

            if (flow && itemStart && IsQuote(c))
            {
                quote = c;
                itemStart = false;
                continue;
            }

            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth = Math.Max(0, depth - 1);
            }

            itemStart = flow && ((c == '[' && i == 0) || (c == ',' && depth == 0));
        }

        return text.TrimEnd();
    }

    private static int FindClosingQuote(string text)
    {
        var quote = text[0];
        for (var i = 1; i < text.Length; i++)
        {
            if (quote == '"' && text[i] == '\\')
            {
                i++;
            }
            else if (text[i] == quote)
            {
                if (quote == '\'' && i + 1 < text.Length && text[i + 1] == '\'')
                {
                    i++;
                    continue;
                }

                return i;
            }
        }

        return -1;
    }

    // The text of a complete quoted string (it starts and ends with the same quote). Double quotes know the escapes
    // \" and \\ (any other backslash is literal); single quotes know '' for a quote.
    private static string Unquote(string quoted)
    {
        var quote = quoted[0];
        var result = new StringBuilder();
        for (var i = 1; i < quoted.Length - 1; i++)
        {
            var c = quoted[i];
            if (quote == '"' && c == '\\' && i + 1 < quoted.Length - 1 && quoted[i + 1] is '"' or '\\')
            {
                result.Append(quoted[++i]);
            }
            else if (quote == '\'' && c == '\'' && i + 1 < quoted.Length - 1 && quoted[i + 1] == '\'')
            {
                result.Append('\'');
                i++;
            }
            else
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }

    private static bool IsQuote(char c) => c is '"' or '\'';

    private static bool IsBlankOrComment(string line) =>
        string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#');

    // A list item is `-` followed by a space (or alone on its line).
    private static bool IsListItem(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed == "-" || trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("-\t", StringComparison.Ordinal);
    }

    private static string ItemText(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.Length > 1 ? trimmed[1..] : string.Empty;
    }
}
