using System.Text.Json;
using AIContext;
using Xunit;

namespace AIContext.Tests;

public class ContextValidatorTests
{
    private static readonly HashSet<string> AllowedTargets = new(StringComparer.Ordinal)
    {
        "agents",
        "copilot",
        "claude",
        "cursor",
        "windsurf",
        "aider"
    };

    private const string CanonicalSchemaUrl = "https://aicontext.dev/schema/v1/context.schema.json";
    private const string ExpectedSpecVersion = "0.1.0";

    [Fact]
    public void ValidateDirectory_WithValidContext_ReturnsNoErrors()
    {
        var root = CreateTempDirectory();
        try
        {
            WriteContextFiles(root, new
            {
                @schema = CanonicalSchemaUrl,
                specVersion = ExpectedSpecVersion,
                project = new { name = "sample" },
                exports = new
                {
                    targets = new[] { "agents", "copilot" },
                    defaultTarget = "agents"
                },
                metadata = new
                {
                    lastReviewed = "2026-11-01"
                }
            });
            WriteSchemaFile(root);

            var result = ContextValidator.ValidateDirectory(root, CanonicalSchemaUrl, ExpectedSpecVersion, AllowedTargets);

            Assert.Empty(result.Errors);
            Assert.Empty(result.Warnings);
            Assert.NotNull(result.LocalSchemaPath);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ValidateDirectory_WithoutSchemaFile_ReturnsSchemaWarning()
    {
        var root = CreateTempDirectory();
        try
        {
            WriteContextFiles(root, new
            {
                @schema = CanonicalSchemaUrl,
                specVersion = ExpectedSpecVersion,
                project = new { name = "sample" }
            });

            var result = ContextValidator.ValidateDirectory(root, CanonicalSchemaUrl, ExpectedSpecVersion, AllowedTargets);

            Assert.Empty(result.Errors);
            Assert.Contains(
                "Local schema file not found at schema/v1/context.schema.json in current directory or parent directories.",
                result.Warnings);
            Assert.Null(result.LocalSchemaPath);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ValidateDirectory_WhenTargetsAreInvalid_ReturnsExpectedErrors()
    {
        var root = CreateTempDirectory();
        try
        {
            WriteContextFiles(root, new
            {
                @schema = CanonicalSchemaUrl,
                specVersion = ExpectedSpecVersion,
                project = new { name = "sample" },
                exports = new
                {
                    targets = new object[] { "agents", "agents", "unknown", 123 },
                    defaultTarget = "claude"
                }
            });
            WriteSchemaFile(root);

            var result = ContextValidator.ValidateDirectory(root, CanonicalSchemaUrl, ExpectedSpecVersion, AllowedTargets);

            Assert.Contains("exports.targets contains duplicate target 'agents'.", result.Errors);
            Assert.Contains("exports.targets contains unsupported target 'unknown'.", result.Errors);
            Assert.Contains("exports.targets must contain only strings.", result.Errors);
            Assert.Contains("exports.defaultTarget must be 'all' or one of exports.targets.", result.Errors);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ValidateDirectory_WhenLastReviewedHasInvalidFormat_ReturnsError()
    {
        var root = CreateTempDirectory();
        try
        {
            WriteContextFiles(root, new
            {
                @schema = CanonicalSchemaUrl,
                specVersion = ExpectedSpecVersion,
                project = new { name = "sample" },
                metadata = new
                {
                    lastReviewed = "01-11-2026"
                }
            });
            WriteSchemaFile(root);

            var result = ContextValidator.ValidateDirectory(root, CanonicalSchemaUrl, ExpectedSpecVersion, AllowedTargets);

            Assert.Contains("metadata.lastReviewed must use ISO date format yyyy-MM-dd.", result.Errors);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"aicontext-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteContextFiles(string rootPath, object contextJson)
    {
        var contextDirectory = Path.Combine(rootPath, ".aicontext");
        Directory.CreateDirectory(contextDirectory);

        var readmePath = Path.Combine(contextDirectory, "README.md");
        File.WriteAllText(readmePath, "# Project: sample\n\nSummary.");

        var contextPath = Path.Combine(contextDirectory, "context.json");
        var json = JsonSerializer.Serialize(contextJson, new JsonSerializerOptions { WriteIndented = true })
            .Replace("\"schema\":", "\"$schema\":", StringComparison.Ordinal);
        File.WriteAllText(contextPath, json);
    }

    private static void WriteSchemaFile(string rootPath)
    {
        var schemaDirectory = Path.Combine(rootPath, "schema", "v1");
        Directory.CreateDirectory(schemaDirectory);
        File.WriteAllText(Path.Combine(schemaDirectory, "context.schema.json"), "{}");
    }
}
