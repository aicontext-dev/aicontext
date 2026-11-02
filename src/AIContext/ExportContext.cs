namespace AIContext;

public sealed record ExportContext(
    string ProjectName,
    string ReadmeContent,
    string? Architecture,
    string? Decisions,
    string? Patterns,
    string? Constraints,
    string? Glossary,
    IReadOnlyList<RuleDocument>? Rules = null);
