using System.Text.RegularExpressions;

namespace AIContext;

public sealed record SecretFinding(string File, int Line, string Kind);

// A file the scanner could not (completely) check. Such files are reported, never skipped silently.
public sealed record UnscannedFile(string File, string Reason);

public sealed record SecretScanReport(IReadOnlyList<SecretFinding> Findings, IReadOnlyList<UnscannedFile> Unscanned);

// A heuristic lint for common secret patterns in text files. It does not find everything and is not a guarantee
// that no secret is present.
public static class SecretScanner
{
    // Add this marker to a line to tell the scanner it is an intentional example.
    public const string AllowMarker = "aicontext:allow-secret";

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private static readonly HashSet<string> ScannedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".mdc", ".json", ".yml", ".yaml", ".txt"
    };

    private static readonly (string Kind, Regex Pattern)[] KnownPatterns =
    [
        ("private key", Create(@"-----BEGIN (?:RSA |EC |DSA |OPENSSH |PGP |ENCRYPTED )?PRIVATE KEY(?: BLOCK)?-----")),
        ("AWS access key id", Create(@"\b(?:AKIA|ASIA)[0-9A-Z]{16}\b")),
        ("GitHub token", Create(@"\bgh[pousr]_[A-Za-z0-9]{36,}\b|\bgithub_pat_[A-Za-z0-9_]{50,}\b")),
        ("Slack token", Create(@"\bxox[abprs]-[A-Za-z0-9-]{10,}")),
        ("Google API key", Create(@"\bAIza[0-9A-Za-z_\-]{35}\b")),
        ("Stripe live key", Create(@"\b[rs]k_live_[0-9A-Za-z]{16,}\b")),
        ("JSON Web Token", Create(@"\beyJ[A-Za-z0-9_-]{10,}\.eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}"))
    ];

    private static readonly Regex GenericAssignment = Create(
        @"(?<![A-Za-z0-9_])(?:password|passwd|pwd|secret|secret[_-]?key|private[_-]?key|api[_-]?key|access[_-]?token|auth[_-]?token|client[_-]?secret|token)(?![A-Za-z0-9_])[""']?\s*[:=]\s*[""']?(?<value>[^\s""'`<>{}$]{6,})",
        RegexOptions.IgnoreCase);

    private static readonly Regex UrlCredentials = Create(
        @"\b[a-z][a-z0-9+.\-]*://[^\s/:@]+:(?<value>[^\s/:@]+)@",
        RegexOptions.IgnoreCase);

    private static readonly string[] PlaceholderWords =
    [
        "your", "example", "changeme", "placeholder", "redacted", "todo", "dummy", "sample", "password", "secret", "token"
    ];

    public static IReadOnlyList<SecretFinding> ScanDirectory(string directory) =>
        ScanDirectoryReport(directory).Findings;

    // Files are read line by line, so there is no size limit. Only text files (ScannedExtensions) are scanned;
    // anything that cannot be read or checked completely is listed in Unscanned instead of being skipped silently.
    public static SecretScanReport ScanDirectoryReport(string directory)
    {
        var findings = new List<SecretFinding>();
        var unscanned = new List<UnscannedFile>();
        if (!Directory.Exists(directory))
        {
            return new SecretScanReport(findings, unscanned);
        }

        var files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(f => ScannedExtensions.Contains(Path.GetExtension(f)))
            .OrderBy(f => f, StringComparer.Ordinal);

        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(directory, file).Replace('\\', '/');
            try
            {
                ScanLines(relative, File.ReadLines(file), findings, out var skippedLine);
                if (skippedLine is not null)
                {
                    unscanned.Add(new UnscannedFile(relative, $"line {skippedLine} is too long or too complex to scan"));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                unscanned.Add(new UnscannedFile(relative, $"the file could not be read ({ex.Message})"));
            }
        }

        return new SecretScanReport(findings, unscanned);
    }

    // Lines the patterns cannot handle in time are not reported here; ScanDirectoryReport lists them.
    public static IReadOnlyList<SecretFinding> Scan(string fileName, string content)
    {
        var findings = new List<SecretFinding>();
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        ScanLines(fileName, lines, findings, out _);
        return findings;
    }

    private static void ScanLines(string fileName, IEnumerable<string> lines, List<SecretFinding> findings, out int? firstSkippedLine)
    {
        firstSkippedLine = null;
        var number = 0;

        foreach (var line in lines)
        {
            number++;
            if (line.Contains(AllowMarker, StringComparison.Ordinal))
            {
                continue;
            }

            string? kind;
            try
            {
                kind = DetectKind(line);
            }
            catch (RegexMatchTimeoutException)
            {
                firstSkippedLine ??= number;
                continue;
            }

            if (kind is not null)
            {
                findings.Add(new SecretFinding(fileName, number, kind));
            }
        }
    }

    private static string? DetectKind(string line)
    {
        foreach (var (kind, pattern) in KnownPatterns)
        {
            if (pattern.IsMatch(line))
            {
                return kind;
            }
        }

        foreach (Match match in UrlCredentials.Matches(line))
        {
            if (!IsPlaceholder(match.Groups["value"].Value))
            {
                return "credentials in URL";
            }
        }

        foreach (Match match in GenericAssignment.Matches(line))
        {
            var value = match.Groups["value"].Value;
            if (!IsPlaceholder(value) && LooksLikeSecretValue(value))
            {
                return "hard-coded credential";
            }
        }

        return null;
    }

    // Plain words ("password: required") are documentation, not secrets.
    private static bool LooksLikeSecretValue(string value) =>
        value.Any(char.IsDigit) || value.IndexOfAny(['!', '@', '#', '%', '^', '&', '*', '+', '=', '/', '\\', '|', '~']) >= 0;

    private static bool IsPlaceholder(string value)
    {
        if (value.All(c => c is '*' or 'x' or 'X' or '.' or '•'))
        {
            return true;
        }

        return PlaceholderWords.Any(word => value.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    private static Regex Create(string pattern, RegexOptions options = RegexOptions.None) =>
        new(pattern, options | RegexOptions.CultureInvariant, RegexTimeout);
}
