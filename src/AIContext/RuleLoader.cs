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

public sealed record RuleLoadResult(IReadOnlyList<RuleDocument> Rules, IReadOnlyList<Diagnostic> Diagnostics)
{
    public IReadOnlyList<string> Errors => Diagnostics.ErrorMessages();

    public IReadOnlyList<string> Warnings => Diagnostics.WarningMessages();
}

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
        var diagnostics = new List<Diagnostic>();

        var rulesDirectory = Path.Combine(contextDirectory, RulesDirectoryName);
        if (!Directory.Exists(rulesDirectory))
        {
            return new RuleLoadResult(rules, diagnostics);
        }

        string[] subdirectories;
        string[] files;
        try
        {
            subdirectories = Directory.EnumerateDirectories(rulesDirectory).ToArray();
            files = Directory.EnumerateFiles(rulesDirectory)
                .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(Diagnostic.Error(
                DiagnosticCodes.UnreadableRuleFile,
                $"{rulesDirectory} could not be listed ({ex.Message}). Check that you have permission to read it.",
                ".aicontext/rules"));
            return new RuleLoadResult(rules, diagnostics);
        }

        foreach (var directory in subdirectories)
        {
            var name = Path.GetFileName(directory);
            diagnostics.Add(Diagnostic.Warning(
                DiagnosticCodes.IgnoredRuleDirectory,
                $"rules/{name}/: subdirectories are ignored; rules must be directly in .aicontext/rules/.",
                $".aicontext/rules/{name}"));
        }

        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            if (!fileName.EndsWith(".md", StringComparison.Ordinal))
            {
                diagnostics.Add(Diagnostic.Warning(
                    DiagnosticCodes.IgnoredRuleFile,
                    $"rules/{fileName}: ignored (only .md files are rules).",
                    $".aicontext/rules/{fileName}"));
                continue;
            }

            var rule = LoadRule(file, fileName, diagnostics);
            if (rule is not null)
            {
                rules.Add(rule);
            }
        }

        return new RuleLoadResult(rules, diagnostics);
    }

    private static RuleDocument? LoadRule(string path, string fileName, List<Diagnostic> diagnostics)
    {
        var name = fileName[..^3];
        var label = $"rules/{fileName}";
        var location = $".aicontext/rules/{fileName}";
        var valid = true;

        if (!NamePattern.IsMatch(name))
        {
            diagnostics.Add(Diagnostic.Error(
                DiagnosticCodes.InvalidRuleName,
                $"{label}: file name must use lowercase letters, digits, '-' or '_' (it becomes part of generated file names).",
                location));
            valid = false;
        }
        else if (ReservedNames.Contains(name))
        {
            diagnostics.Add(Diagnostic.Error(
                DiagnosticCodes.ReservedRuleName,
                $"{label}: '{name}' is reserved for a built-in section; choose another file name.",
                location));
            valid = false;
        }

        var text = ContextFileReader.TryReadAllText(path, DiagnosticCodes.UnreadableRuleFile, location, diagnostics);
        if (text is null)
        {
            return null;
        }

        var parsed = Frontmatter.Parse(text);
        if (parsed.Error is not null)
        {
            diagnostics.Add(Diagnostic.Error(DiagnosticCodes.InvalidFrontmatter, $"{label}: {parsed.Error}", location));
            return null;
        }

        string? description = null;
        var paths = new List<string>();

        foreach (var (key, value) in parsed.Values)
        {
            if (!KnownKeys.Contains(key))
            {
                diagnostics.Add(Diagnostic.Warning(
                    DiagnosticCodes.UnknownFrontmatterKey,
                    $"{label}: unknown frontmatter key '{key}' is ignored.",
                    location));
            }
            else if (key == "description")
            {
                if (value.IsList)
                {
                    diagnostics.Add(Diagnostic.Error(
                        DiagnosticCodes.InvalidRuleDescription,
                        $"{label}: 'description' must be a single line of text.",
                        location));
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
                        diagnostics.Add(Diagnostic.Error(
                            DiagnosticCodes.InvalidRulePathSeparator,
                            $"{label}: path '{entry}' must use '/' as separator.",
                            location));
                        valid = false;
                    }

                    paths.Add(entry);
                }
            }
        }

        var body = parsed.Body.Trim();
        if (body.Length == 0)
        {
            diagnostics.Add(Diagnostic.Warning(DiagnosticCodes.EmptyRule, $"{label}: rule is empty and will be skipped.", location));
            return null;
        }

        return valid ? new RuleDocument(name, description, paths, body) : null;
    }
}
