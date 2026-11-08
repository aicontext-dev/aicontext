using AIContext;
using Xunit;

namespace AIContext.Tests;

public sealed class ToolOverlapTests
{
    private static TargetStatus Status(string target, int alwaysOnChars) =>
        new(target, 1, alwaysOnChars, 0, SyncState.InSync, alwaysOnChars);

    private static IReadOnlyList<ToolOverlap> Compute(params (string Target, int Chars)[] targets) =>
        ToolOverlaps.Compute(targets.Select(t => Status(t.Target, t.Chars)).ToList());

    [Fact]
    public void Compute_SingleTarget_HasNoOverlap()
    {
        Assert.Empty(Compute(("copilot", 1000)));
    }

    [Fact]
    public void Compute_CopilotWithAgentsAndClaude_SumsTheAlwaysOnContentOfAllThree()
    {
        var overlap = Assert.Single(Compute(("agents", 1000), ("copilot", 2000), ("claude", 4000)), o => o.Tool.StartsWith("Copilot"));

        Assert.Equal(["copilot", "agents", "claude"], overlap.Targets);
        Assert.Equal(7000, overlap.AlwaysOnChars);
    }

    [Fact]
    public void Compute_ToolWithoutItsOwnTarget_IsNotListed()
    {
        Assert.Empty(Compute(("agents", 1000), ("claude", 4000)));
    }

    [Fact]
    public void Compute_OnlyTargetsThatAreExportedAreCounted()
    {
        var overlap = Assert.Single(Compute(("agents", 1000), ("copilot", 2000)));

        Assert.Equal(["copilot", "agents"], overlap.Targets);
        Assert.Equal(3000, overlap.AlwaysOnChars);
    }

    [Fact]
    public void Compute_DevinWithWindsurfAndAgents_ListsAllThree()
    {
        var overlap = Assert.Single(Compute(("agents", 100), ("windsurf", 200), ("devin", 300)));

        Assert.Equal("Devin", overlap.Tool);
        Assert.Equal(["devin", "agents", "windsurf"], overlap.Targets);
        Assert.Equal(600, overlap.AlwaysOnChars);
    }

    [Fact]
    public void Compute_CursorWithAgents_ListsCursor()
    {
        var overlap = Assert.Single(Compute(("cursor", 500), ("agents", 1500)));

        Assert.Equal("Cursor", overlap.Tool);
        Assert.Equal(2000, overlap.AlwaysOnChars);
    }

    [Fact]
    public void Compute_ToolsWithoutDocumentedOverlap_AreNeverListed()
    {
        Assert.Empty(Compute(("aider", 1000), ("agents", 1000), ("windsurf", 1000)));
    }
}
