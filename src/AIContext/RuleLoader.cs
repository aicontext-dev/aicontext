using System.Text.RegularExpressions;

namespace AIContext;

public enum RuleKind
{
    // No scope: always part of the context.
    Always,
    // Description only: loaded when the agent decides it is relevant.
    OnDemand,
    // Paths given: loaded when working with matching files.
    Scoped
}

public sealed record RuleDocument(string Name, string? Description, IReadOnlyList<string> Paths, string Body)
{
    public RuleKind Kind =>
        Paths.Count > 0 ? RuleKind.Scoped :
        !string.IsNullOrWhiteSpace(Description) ? RuleKind.OnDemand :
        RuleKind.Always;
}

public sealed record RuleLoadResult(
    IReadOnlyList<RuleDocument> Rules,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public static class RuleLoader
{
    public const string RulesDirectoryName = "rules";

    // Names that would collide with generated files for the built-in sections.
    public static readonly IReadOnlySet<string> ReservedNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "architecture", "decisions", "patterns", "constraints", "glossary", "project"
    };

    private static readonly Regex NamePattern = new("^[a-z0-9][a-z0-9_-]*$", RegexOptions.CultureInvariant);
    private static readonly HashSet<string> KnownKeys = new(StringComparer.Ordinal) { "description", "paths" };

    public static RuleLoadResult Load(string contextDirectory)
    {
        var rules = new List<RuleDocument>();
        var errors = new List<string>();
        var warnings = new List<string>();

        var rulesDirectory = Path.Combine(contextDirectory, RulesDirectoryName);
        if (!Directory.Exists(rulesDirectory))
        {
            return new RuleLoadResult(rules, errors, warnings);
        }

        foreach (var directory in Directory.EnumerateDirectories(rulesDirectory))
        {
            warnings.Add($"rules/{Path.GetFileName(directory)}/: subdirectories are ignored; rules must be directly in .aicontext/rules/.");
        }

        var files = Directory.EnumerateFiles(rulesDirectory)
            .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal);

        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            if (!fileName.EndsWith(".md", StringComparison.Ordinal))
            {
                warnings.Add($"rules/{fileName}: ignored (only .md files are rules).");
                continue;
            }

            var rule = LoadRule(file, fileName, errors, warnings);
            if (rule is not null)
            {
                rules.Add(rule);
            }
        }

        return new RuleLoadResult(rules, errors, warnings);
    }

    private static RuleDocument? LoadRule(string path, string fileName, List<string> errors, List<string> warnings)
    {
        var name = fileName[..^3];
        var label = $"rules/{fileName}";
        var valid = true;

        if (!NamePattern.IsMatch(name))
        {
            errors.Add($"{label}: file name must use lowercase letters, digits, '-' or '_' (it becomes part of generated file names).");
            valid = false;
        }
        else if (ReservedNames.Contains(name))
        {
            errors.Add($"{label}: '{name}' is reserved for a built-in section; choose another file name.");
            valid = false;
        }

        var parsed = Frontmatter.Parse(File.ReadAllText(path));
        if (parsed.Error is not null)
        {
            errors.Add($"{label}: {parsed.Error}");
            return null;
        }

        string? description = null;
        var paths = new List<string>();

        foreach (var (key, value) in parsed.Values)
        {
            if (!KnownKeys.Contains(key))
            {
                warnings.Add($"{label}: unknown frontmatter key '{key}' is ignored.");
            }
            else if (key == "description")
            {
                if (value.IsList)
                {
                    errors.Add($"{label}: 'description' must be a single line of text.");
                    valid = false;
                }
                else if (!string.IsNullOrWhiteSpace(value.Scalar))
                {
                    description = value.Scalar!.Trim();
                }
            }
            else
            {
                var entries = value.IsList
                    ? value.List!
                    : Frontmatter.SplitTopLevel(value.Scalar ?? string.Empty);

                foreach (var entry in entries)
                {
                    if (entry.Length == 0)
                    {
                        continue;
                    }

                    if (entry.Contains('\\', StringComparison.Ordinal))
                    {
                        errors.Add($"{label}: path '{entry}' must use '/' as separator.");
                        valid = false;
                    }

                    paths.Add(entry);
                }
            }
        }

        var body = parsed.Body.Trim();
        if (body.Length == 0)
        {
            warnings.Add($"{label}: rule is empty and will be skipped.");
            return null;
        }

        return valid ? new RuleDocument(name, description, paths, body) : null;
    }
}
