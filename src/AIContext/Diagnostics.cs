namespace AIContext;

public enum DiagnosticSeverity
{
    Error,
    Warning
}

// A machine-readable problem report. Code is a stable identifier: it is never reused or renumbered, so tools
// can match on it. Message is meant for people and may be reworded. Location is a project-relative path with
// '/' separators, optionally followed by ':<line>' or '#<json-pointer>'.
public sealed record Diagnostic(string Code, DiagnosticSeverity Severity, string Message, string? Location = null)
{
    public bool IsError => Severity == DiagnosticSeverity.Error;

    public static Diagnostic Error(string code, string message, string? location = null) =>
        new(code, DiagnosticSeverity.Error, message, location);

    public static Diagnostic Warning(string code, string message, string? location = null) =>
        new(code, DiagnosticSeverity.Warning, message, location);
}

// Codes are grouped by area: 1xxx context files, 2xxx rules, 3xxx secret scan, 4xxx export. In 1xxx and 2xxx the
// 0xx codes are errors and the 1xx codes warnings; in 4xxx the 1xx codes describe a single planned file.
// A new diagnostic needs a new code here and a row in the user manual.
public static class DiagnosticCodes
{
    public const string MissingRequiredFile = "AIC1001";
    public const string EmptyReadme = "AIC1002";
    public const string InvalidJson = "AIC1003";
    public const string SchemaViolation = "AIC1004";
    public const string ProjectNameWhitespace = "AIC1005";
    public const string DefaultTargetNotInTargets = "AIC1006";
    public const string MissingSchemaProperty = "AIC1101";
    public const string SchemaUrlMismatch = "AIC1102";
    public const string WindsurfDevinOverlap = "AIC1103";
    public const string NestedAgentsWithoutSplit = "AIC1104";
    public const string RecommendedFileMissing = "AIC1105";
    public const string RecommendedFileEmpty = "AIC1106";
    public const string PlaceholderContent = "AIC1107";

    public const string InvalidRuleName = "AIC2001";
    public const string ReservedRuleName = "AIC2002";
    public const string InvalidFrontmatter = "AIC2003";
    public const string InvalidRuleDescription = "AIC2004";
    public const string InvalidRulePathSeparator = "AIC2005";
    public const string IgnoredRuleDirectory = "AIC2101";
    public const string IgnoredRuleFile = "AIC2102";
    public const string UnknownFrontmatterKey = "AIC2103";
    public const string EmptyRule = "AIC2104";

    public const string PossibleSecret = "AIC3001";
    public const string SecretScanIncomplete = "AIC3002";

    public const string UnsupportedTarget = "AIC4001";
    public const string MissingTarget = "AIC4002";
    public const string UnsupportedDefaultTarget = "AIC4003";
    public const string InvalidTargetsConfiguration = "AIC4004";
    public const string UnsupportedLayout = "AIC4005";
    public const string OverwriteConflict = "AIC4101";
    public const string PathOutsideProject = "AIC4102";
}

internal static class DiagnosticExtensions
{
    public static IReadOnlyList<string> ErrorMessages(this IEnumerable<Diagnostic> diagnostics) =>
        diagnostics.Where(d => d.IsError).Select(d => d.Message).ToList();

    public static IReadOnlyList<string> WarningMessages(this IEnumerable<Diagnostic> diagnostics) =>
        diagnostics.Where(d => !d.IsError).Select(d => d.Message).ToList();
}
