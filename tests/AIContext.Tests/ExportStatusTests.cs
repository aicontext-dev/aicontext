using AIContext;
using Xunit;

namespace AIContext.Tests;

public sealed class ExportStatusTests : IDisposable
{
    private readonly string _basePath = Path.Combine(Path.GetTempPath(), "aicontext-tests-" + Guid.NewGuid().ToString("N"));

    public ExportStatusTests()
    {
        var contextDirectory = Path.Combine(_basePath, ".aicontext");
        Directory.CreateDirectory(Path.Combine(contextDirectory, "rules"));
        File.WriteAllText(Path.Combine(contextDirectory, "README.md"), "# Project: demo\n\nSummary.");
        File.WriteAllText(Path.Combine(contextDirectory, "context.json"), "{\"specVersion\":\"0.1.0\",\"project\":{\"name\":\"Demo\"}}");
        File.WriteAllText(Path.Combine(contextDirectory, "architecture.md"), new string('a', 4000));
        File.WriteAllText(Path.Combine(contextDirectory, "constraints.md"), "Never do X.");
        File.WriteAllText(
            Path.Combine(contextDirectory, "rules", "ui.md"),
            "---\npaths: [\"src/ui/**\"]\n---\n" + new string('u', 2000));
    }

    public void Dispose()
    {
        Directory.Delete(_basePath, recursive: true);
    }

    private LoadedContext Load()
    {
        var result = ContextLoader.Load(_basePath);
        Assert.Empty(result.Errors);
        return result.Loaded!;
    }

    [Fact]
    public void Compute_SplitLayout_ReducesAlwaysOnContentForScopingTools()
    {
        var statuses = ExportStatus.Compute(Load(), ["agents", "cursor"], ExportLayout.Split)
            .ToDictionary(s => s.Target);

        var agents = statuses["agents"];
        var cursor = statuses["cursor"];

        Assert.Equal(0, agents.OnDemandChars);
        Assert.Equal(1, agents.FileCount);
        Assert.True(cursor.AlwaysOnChars < agents.AlwaysOnChars / 4);
        Assert.True(cursor.OnDemandChars > 6000);
        Assert.Equal(3, cursor.FileCount);
        Assert.True(cursor.AlwaysOnCharsInOtherLayout > cursor.AlwaysOnChars);
    }

    [Fact]
    public void Compute_ReportsSyncState()
    {
        var loaded = Load();
        var cursor = ExportTargets.Find("cursor")!;

        Assert.Equal(SyncState.NotExported, ExportStatus.Compute(loaded, ["cursor"], ExportLayout.Split).Single().State);

        foreach (var plan in ExportWriter.PlanTarget(cursor, _basePath, loaded.Context, ExportLayout.Split, force: false))
        {
            ExportWriter.Apply(plan);
        }

        Assert.Equal(SyncState.InSync, ExportStatus.Compute(loaded, ["cursor"], ExportLayout.Split).Single().State);

        File.AppendAllText(Path.Combine(_basePath, ".aicontext", "constraints.md"), "\nAnd never Y.");
        var changed = Load();
        Assert.Equal(SyncState.OutOfDate, ExportStatus.Compute(changed, ["cursor"], ExportLayout.Split).Single().State);

        File.WriteAllText(Path.Combine(_basePath, "AGENTS.md"), "hand written");
        Assert.Equal(SyncState.Conflict, ExportStatus.Compute(changed, ["agents"], ExportLayout.Split).Single().State);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(4, 1)]
    [InlineData(5, 2)]
    [InlineData(4000, 1000)]
    public void EstimateTokens_RoundsUp(int chars, int expected)
    {
        Assert.Equal(expected, ExportStatus.EstimateTokens(chars));
    }

    [Fact]
    public void ContextLoader_ReportsMissingFilesInvalidJsonAndRuleErrors()
    {
        var missing = ContextLoader.Load(Path.Combine(_basePath, "nowhere"));
        Assert.Null(missing.Loaded);
        Assert.Equal(2, missing.Errors.Count);

        File.WriteAllText(Path.Combine(_basePath, ".aicontext", "context.json"), "{ nope");
        Assert.Contains(ContextLoader.Load(_basePath).Errors, e => e.StartsWith("Invalid JSON", StringComparison.Ordinal));

        File.WriteAllText(Path.Combine(_basePath, ".aicontext", "context.json"), "{\"project\":{\"name\":\"Demo\"}}");
        File.WriteAllText(Path.Combine(_basePath, ".aicontext", "rules", "Bad Name.md"), "x");
        Assert.Contains(ContextLoader.Load(_basePath).Errors, e => e.StartsWith("rules/", StringComparison.Ordinal));
    }

    [Fact]
    public void ContextLoader_ContextWithoutRequiredProperties_IsRefused()
    {
        File.WriteAllText(Path.Combine(_basePath, ".aicontext", "context.json"), "{}");

        var result = ContextLoader.Load(_basePath);

        Assert.Null(result.Loaded);
        Assert.Contains(result.Errors, e => e.Contains("specVersion", StringComparison.Ordinal));
    }
}
