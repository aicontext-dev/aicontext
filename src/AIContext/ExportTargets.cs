namespace AIContext;

public sealed record ExportTarget(string Name, string RelativePath, Func<ExportContext, string> Build)
{
    public string ResolvePath(string basePath)
    {
        var segments = new List<string> { basePath };
        segments.AddRange(RelativePath.Split('/'));
        return Path.Combine(segments.ToArray());
    }
}

public static class ExportTargets
{
    public static IReadOnlyList<ExportTarget> All { get; } =
    [
        new("agents", "AGENTS.md", ContextExporter.BuildAgents),
        new("copilot", ".github/copilot-instructions.md", ContextExporter.BuildCopilot),
        new("claude", "CLAUDE.md", ContextExporter.BuildClaude),
        new("cursor", ".cursor/rules/project.mdc", ContextExporter.BuildCursor),
        new("windsurf", ".windsurf/rules/project.md", ContextExporter.BuildWindsurf),
        new("aider", "CONVENTIONS.md", ContextExporter.BuildAider)
    ];

    public static IReadOnlyList<string> Names { get; } = All.Select(x => x.Name).ToArray();

    public static ExportTarget? Find(string name) =>
        All.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.Ordinal));
}
