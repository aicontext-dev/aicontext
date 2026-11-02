using System.Text.RegularExpressions;

namespace AIContext;

public sealed record SecretFinding(string File, int Line, string Kind);

public static class SecretScanner
{
    // Add this marker to a line to tell the scanner it is an intentional example.
    public const string AllowMarker = "aicontext:allow-secret";

    private const long MaxFileSize = 512 * 1024;
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

    public static IReadOnlyList<SecretFinding> ScanDirectory(string directory)
    {
        var findings = new List<SecretFinding>();
        if (!Directory.Exists(directory))
        {
            return findings;
        }

        var files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(f => ScannedExtensions.Contains(Path.GetExtension(f)))
            .OrderBy(f => f, StringComparer.Ordinal);

        foreach (var file in files)
        {
            if (new FileInfo(file).Length > MaxFileSize)
            {
                continue;
            }

            var relative = Path.GetRelativePath(directory, file).Replace('\\', '/');
            findings.AddRange(Scan(relative, File.ReadAllText(file)));
        }

        return findings;
    }

    public static IReadOnlyList<SecretFinding> Scan(string fileName, string content)
    {
        var findings = new List<SecretFinding>();
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Contains(AllowMarker, StringComparison.Ordinal))
            {
                continue;
            }

            var kind = DetectKind(line);
            if (kind is not null)
            {
                findings.Add(new SecretFinding(fileName, i + 1, kind));
            }
        }

        return findings;
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
