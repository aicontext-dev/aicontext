using System.Text.RegularExpressions;

namespace AIContext;

public sealed record PlaceholderFinding(string File, int Line);

// Finds obvious placeholder text such as the TODO stubs that 'aicontext init' writes. It is deliberately narrow:
// a marker must look like a placeholder, not just contain the word, and code (fenced blocks and inline code) is
// ignored. It says nothing about whether the rest of the content is useful or up to date.
public static class PlaceholderScanner
{
    // Add this marker to a line to tell the scanner the text is intentional.
    public const string AllowMarker = "aicontext:allow-placeholder";

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private static readonly string[] SectionFiles =
        ["README.md", "architecture.md", "decisions.md", "patterns.md", "constraints.md", "glossary.md"];

    // "TODO:" anywhere (a stub such as "TODO: describe ..."), or a value that is only "TODO" ("**Tech stack:** TODO",
    // "- TODO"), or the literal summary stub that 'init' writes.
    private static readonly Regex[] Patterns =
    [
        new(@"\bTODO:", RegexOptions.CultureInvariant, RegexTimeout),
        new(@"(?:(?::|\*\*)\s*|^\s*(?:[-*+]\s+)?)TODO\s*$", RegexOptions.CultureInvariant, RegexTimeout),
        new(@"^\s*Brief project summary\.\s*$", RegexOptions.CultureInvariant, RegexTimeout)
    ];

    private static readonly Regex InlineCode = new("`[^`]*`", RegexOptions.CultureInvariant, RegexTimeout);

    public static IReadOnlyList<PlaceholderFinding> ScanDirectory(string contextDirectory)
    {
        var findings = new List<PlaceholderFinding>();
        var files = SectionFiles.Select(name => (Name: name, Path: Path.Combine(contextDirectory, name))).ToList();

        var rulesDirectory = Path.Combine(contextDirectory, RuleLoader.RulesDirectoryName);
        if (Directory.Exists(rulesDirectory))
        {
            files.AddRange(Directory.EnumerateFiles(rulesDirectory, "*.md")
                .OrderBy(f => f, StringComparer.Ordinal)
                .Select(f => ($"{RuleLoader.RulesDirectoryName}/{System.IO.Path.GetFileName(f)}", f)));
        }

        foreach (var (name, path) in files)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                findings.AddRange(Scan(name, File.ReadAllText(path)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The secret scan reports a file that cannot be read.
            }
        }

        return findings;
    }

    public static IReadOnlyList<PlaceholderFinding> Scan(string fileName, string content)
    {
        var findings = new List<PlaceholderFinding>();
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var inFence = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal) ||
                line.TrimStart().StartsWith("~~~", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }

            if (inFence || line.Contains(AllowMarker, StringComparison.Ordinal))
            {
                continue;
            }

            var text = InlineCode.Replace(line, string.Empty);
            if (Patterns.Any(p => IsMatch(p, text)))
            {
                findings.Add(new PlaceholderFinding(fileName, i + 1));
            }
        }

        return findings;
    }

    private static bool IsMatch(Regex pattern, string text)
    {
        try
        {
            return pattern.IsMatch(text);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
