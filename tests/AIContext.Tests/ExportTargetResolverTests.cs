using System.Text.Json;
using AIContext;
using Xunit;

namespace AIContext.Tests;

public class ExportTargetResolverTests
{
    private static readonly string[] SupportedTargets =
    [
        "agents",
        "copilot",
        "claude",
        "cursor",
        "windsurf",
        "aider"
    ];

    private static readonly HashSet<string> AllowedTargets = new(SupportedTargets, StringComparer.Ordinal);

    [Fact]
    public void ResolveTargetsForAll_UsesConfiguredTargets_WhenPresent()
    {
        using var document = JsonDocument.Parse("""
                                               {
                                                 "exports": {
                                                   "targets": ["agents", "copilot"]
                                                 }
                                               }
                                               """);

        var result = ExportTargetResolver.ResolveTargetsForAll(document.RootElement, SupportedTargets, AllowedTargets);

        Assert.Null(result.Error);
        Assert.Equal(["agents", "copilot"], result.Targets);
    }

    [Fact]
    public void ResolveTargetsForAll_FallsBackToSupportedTargets_WhenTargetsMissing()
    {
        using var document = JsonDocument.Parse("""
                                               {
                                                 "project": { "name": "sample" }
                                               }
                                               """);

        var result = ExportTargetResolver.ResolveTargetsForAll(document.RootElement, SupportedTargets, AllowedTargets);

        Assert.Null(result.Error);
        Assert.Equal(SupportedTargets, result.Targets);
    }

    [Fact]
    public void ResolveTargetsForAll_ReturnsError_WhenTargetsContainUnsupportedTarget()
    {
        using var document = JsonDocument.Parse("""
                                               {
                                                 "exports": {
                                                   "targets": ["agents", "unknown"]
                                                 }
                                               }
                                               """);

        var result = ExportTargetResolver.ResolveTargetsForAll(document.RootElement, SupportedTargets, AllowedTargets);

        Assert.Equal("exports.targets contains unsupported target 'unknown'.", result.Error);
        Assert.Empty(result.Targets);
    }
}
