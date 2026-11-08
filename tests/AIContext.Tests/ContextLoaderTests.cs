using System.Text.Json;
using AIContext;
using Xunit;

namespace AIContext.Tests;

// D13: export and status accept exactly what validate accepts; only errors stop them, never warnings.
public sealed class ContextLoaderTests : IDisposable
{
    private readonly string _basePath = Path.Combine(Path.GetTempPath(), "aicontext-tests-" + Guid.NewGuid().ToString("N"));

    public ContextLoaderTests()
    {
        Directory.CreateDirectory(Path.Combine(_basePath, ".aicontext"));
        WriteReadme("# Project: demo\n\nSummary.");
        WriteContext("""{ "specVersion": "0.1.0", "project": { "name": "demo" } }""");
    }

    public void Dispose()
    {
        Directory.Delete(_basePath, recursive: true);
    }

    private void WriteReadme(string content) =>
        File.WriteAllText(Path.Combine(_basePath, ".aicontext", "README.md"), content);

    private void WriteContext(string json) =>
        File.WriteAllText(Path.Combine(_basePath, ".aicontext", "context.json"), json);

    [Fact]
    public void Load_ValidContextWithOnlyWarnings_Loads()
    {
        var result = ContextLoader.Load(_basePath);

        Assert.Empty(result.Errors);
        Assert.NotNull(result.Loaded);
        Assert.Equal("demo", result.Loaded.Context.ProjectName);
    }

    [Fact]
    public void Load_UnsupportedSpecVersion_IsRefused()
    {
        WriteContext("""{ "specVersion": "999.0.0", "project": { "name": "demo" } }""");

        var result = ContextLoader.Load(_basePath);

        Assert.Null(result.Loaded);
        Assert.Contains(result.Errors, e => e.StartsWith("context.json /specVersion:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("""{ "specVersion": "0.1.0", "project": { "name": 42 } }""", "/project/name")]
    [InlineData("""{ "specVersion": "0.1.0", "project": { "name": "demo" }, "bogus": true }""", "/bogus")]
    [InlineData("[1, 2, 3]", "(root)")]
    public void Load_ContextThatViolatesTheSchema_IsRefused(string json, string location)
    {
        WriteContext(json);

        var result = ContextLoader.Load(_basePath);

        Assert.Null(result.Loaded);
        Assert.Contains(result.Errors, e => e.StartsWith($"context.json {location}:", StringComparison.Ordinal));
    }

    [Fact]
    public void Load_DefaultTargetNotListedInTargets_IsRefused()
    {
        WriteContext("""{ "specVersion": "0.1.0", "project": { "name": "demo" }, "exports": { "targets": ["agents"], "defaultTarget": "claude" } }""");

        var result = ContextLoader.Load(_basePath);

        Assert.Null(result.Loaded);
        Assert.Contains(result.Errors, e => e.Contains("defaultTarget", StringComparison.Ordinal));
    }

    [Fact]
    public void Load_EmptyReadme_IsRefused()
    {
        WriteReadme("  \n");

        var result = ContextLoader.Load(_basePath);

        Assert.Null(result.Loaded);
        Assert.Contains(result.Errors, e => e.Contains("is empty", StringComparison.Ordinal));
    }

    [Fact]
    public void Load_ReportsTheSameErrorsAsValidate()
    {
        WriteContext("""{ "specVersion": "999.0.0", "project": { "name": "demo" }, "bogus": true }""");
        File.WriteAllText(Path.Combine(_basePath, ".aicontext", "decisions.md"), "ghp_" + new string('a', 36));

        var validation = ContextValidator.ValidateDirectory(_basePath, ContextSchema.CanonicalUrl);
        var result = ContextLoader.Load(_basePath);

        Assert.NotEmpty(validation.Errors);
        Assert.Equal(validation.Errors, result.Errors);
    }

    [Fact]
    public void Load_PossibleSecretOrMissingRecommendedFiles_DoNotBlock()
    {
        File.WriteAllText(Path.Combine(_basePath, ".aicontext", "decisions.md"), "ghp_" + new string('a', 36));

        Assert.NotEmpty(ContextValidator.ValidateDirectory(_basePath, ContextSchema.CanonicalUrl).Warnings);
        Assert.NotNull(ContextLoader.Load(_basePath).Loaded);
    }

    [Fact]
    public void CanonicalUrl_MatchesTheEmbeddedSchemaId()
    {
        using var schema = JsonDocument.Parse(ContextSchema.ReadText());

        Assert.Equal(ContextSchema.CanonicalUrl, schema.RootElement.GetProperty("$id").GetString());
    }
}
