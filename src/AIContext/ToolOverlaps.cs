namespace AIContext;

// A tool that reads more than its own export file, so it loads several generated files together.
public sealed record ToolOverlap(string Tool, IReadOnlyList<string> Targets, int AlwaysOnChars);

// Which generated files a tool reads, taken from the vendors' documentation (the VS Code custom instructions
// reference, the Cursor rules documentation and the Devin rules documentation). What a tool really loads depends
// on its version and settings, so the result is an estimate. A tool is only listed when its own target is exported
// together with at least one other target it also reads.
public static class ToolOverlaps
{
    private sealed record ToolReads(string Tool, string OwnTarget, string[] AlsoReads);

    private static readonly ToolReads[] Known =
    [
        new("Copilot (VS Code)", "copilot", ["agents", "claude"]),
        new("Cursor", "cursor", ["agents"]),
        new("Devin", "devin", ["agents", "windsurf"])
    ];

    // Tools that read the file of another target in addition to their own (for example Copilot reads AGENTS.md).
    public static IReadOnlyList<string> ReadersOf(string target) =>
        Known.Where(tool => tool.AlsoReads.Contains(target, StringComparer.Ordinal)).Select(tool => tool.Tool).ToList();

    public static IReadOnlyList<ToolOverlap> Compute(IReadOnlyList<TargetStatus> statuses)
    {
        var byTarget = statuses.ToDictionary(s => s.Target, StringComparer.Ordinal);
        var overlaps = new List<ToolOverlap>();

        foreach (var tool in Known)
        {
            if (!byTarget.ContainsKey(tool.OwnTarget))
            {
                continue;
            }

            var others = tool.AlsoReads.Where(byTarget.ContainsKey).ToList();
            if (others.Count == 0)
            {
                continue;
            }

            var targets = new List<string> { tool.OwnTarget };
            targets.AddRange(others);
            overlaps.Add(new ToolOverlap(tool.Tool, targets, targets.Sum(t => byTarget[t].AlwaysOnChars)));
        }

        return overlaps;
    }
}
