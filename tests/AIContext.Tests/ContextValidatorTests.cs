using System.Text.Json;
using AIContext;
using Xunit;

namespace AIContext.Tests;

public sealed class ContextValidatorTests : IDisposable
{
    private const string CanonicalSchemaUrl = "https://aicontext.dev/schema/v1/context.schema.json";
    private const string ExpectedSpecVersion = "0.1.0";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"aicontext-tests-{Guid.NewGuid():N}");

    public ContextValidatorTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".aicontext"));
    }

    public void Dispose()
    {
        Directory.Delete(_root, true);
    }

    [Fact]
    public void ValidateDirectory_WithValidContext_ReturnsNoErrors_AndWarnsAboutRecommendedFiles()
    {
        WriteContext(new
        {
            @schema = CanonicalSchemaUrl,
            specVersion = ExpectedSpecVersion,
            project = new { name = "sample", description = "Demo", stack = new[] { ".NET 10" } },
            exports = new { targets = new[] { "agents", "copilot" }, defaultTarget = "agents" },
            metadata = new { lastReviewed = "2026-11-01" }
        });

        var result = Validate();

        Assert.Empty(result.Errors);
        Assert.False(result.ExtendedProfile);
        Assert.Contains("Recommended file missing: .aicontext/architecture.md.", result.Warnings);
        Assert.Contains("Recommended file missing: .aicontext/decisions.md.", result.Warnings);
        Assert.Contains("Recommended file missing: .aicontext/constraints.md.", result.Warnings);
    }

    [Fact]
    public void ValidateDirectory_WithAllRecommendedFiles_IsExtendedProfileWithoutWarnings()
    {
        WriteMinimalContext();
        WriteFile("architecture.md", "Why this architecture.");
        WriteFile("decisions.md", "Decision 1.");
        WriteFile("constraints.md", "Never do X.");

        var result = Validate();

        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
        Assert.True(result.ExtendedProfile);
    }

    [Fact]
    public void ValidateDirectory_WithEmptyRecommendedFile_WarnsAndIsNotExtended()
    {
        WriteMinimalContext();
        WriteFile("architecture.md", "Why.");
        WriteFile("decisions.md", "Decision.");
        WriteFile("constraints.md", "   \n");

        var result = Validate();

        Assert.Contains("Recommended file is empty: .aicontext/constraints.md.", result.Warnings);
        Assert.False(result.ExtendedProfile);
    }

    [Fact]
    public void ValidateDirectory_WithoutSchemaProperty_Warns()
    {
        WriteContext(new { specVersion = ExpectedSpecVersion, project = new { name = "sample" } });

        var result = Validate();

        Assert.Empty(result.Errors);
        Assert.Contains("context.json is missing '$schema'.", result.Warnings);
    }

    [Fact]
    public void ValidateDirectory_ChecksContextJsonAgainstSchema()
    {
        WriteContext(new
        {
            @schema = CanonicalSchemaUrl,
            specVersion = "0.2.0",
            project = new { name = "sample", description = "", stack = new[] { "a", "a" }, bogus = 1 },
            extra = true
        });

        var errors = Validate().Errors;

        Assert.Contains(errors, e => e.StartsWith("context.json /specVersion:", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.StartsWith("context.json /project/description:", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.StartsWith("context.json /project/stack:", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.StartsWith("context.json /project/bogus:", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.StartsWith("context.json /extra:", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateDirectory_WithMissingRequiredProperties_ReturnsErrors()
    {
        WriteContext(new { @schema = CanonicalSchemaUrl });

        var errors = Validate().Errors;

        Assert.Contains(errors, e => e.Contains("specVersion", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("project", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateDirectory_WhenTargetsAreInvalid_ReturnsExpectedErrors()
    {
        WriteContext(new
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

        var errors = Validate().Errors;

        Assert.Contains(errors, e => e.StartsWith("context.json /exports/targets:", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.StartsWith("context.json /exports/targets/2:", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.StartsWith("context.json /exports/targets/3:", StringComparison.Ordinal));
        Assert.Contains("exports.defaultTarget must be 'all' or one of exports.targets.", errors);
    }

    [Fact]
    public void ValidateDirectory_WhenLastReviewedHasInvalidFormat_ReturnsError()
    {
        WriteContext(new
        {
            @schema = CanonicalSchemaUrl,
            specVersion = ExpectedSpecVersion,
            project = new { name = "sample" },
            metadata = new { lastReviewed = "01-11-2026" }
        });

        var errors = Validate().Errors;

        Assert.Contains(errors, e => e.StartsWith("context.json /metadata/lastReviewed:", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateDirectory_WithWhitespaceProjectName_ReturnsError()
    {
        WriteContext(new { @schema = CanonicalSchemaUrl, specVersion = ExpectedSpecVersion, project = new { name = "  " } });

        Assert.Contains("project.name must not be whitespace only.", Validate().Errors);
    }

    [Fact]
    public void ValidateDirectory_WithInvalidJson_ReturnsErrorAndDoesNotThrow()
    {
        WriteFile("README.md", "# Project: sample\n\nSummary.");
        WriteFile("context.json", "{ not json");

        var result = Validate();

        Assert.Contains(result.Errors, e => e.StartsWith("Invalid JSON in", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateDirectory_WithSecret_WarnsWithoutRevealingValue()
    {
        WriteMinimalContext();
        WriteFile("README.md", "# Project: sample\n\npassword = hunter2\n");

        var warnings = Validate().Warnings;

        var warning = Assert.Single(warnings, w => w.StartsWith("Possible secret", StringComparison.Ordinal));
        Assert.Contains(".aicontext/README.md:3", warning);
        Assert.DoesNotContain("hunter2", warning);
    }

    [Fact]
    public void ValidateDirectory_WithWindsurfAndDevinTargets_Warns()
    {
        WriteContext(new
        {
            @schema = CanonicalSchemaUrl,
            specVersion = ExpectedSpecVersion,
            project = new { name = "sample" },
            exports = new { targets = new[] { "windsurf", "devin" }, layout = "split" }
        });

        var result = Validate();

        Assert.Empty(result.Errors);
        Assert.Contains(result.Warnings, w => w.Contains("both 'windsurf' and 'devin'", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateDirectory_WithUnknownLayout_ReturnsError()
    {
        WriteContext(new
        {
            @schema = CanonicalSchemaUrl,
            specVersion = ExpectedSpecVersion,
            project = new { name = "sample" },
            exports = new { layout = "weird" }
        });

        Assert.Contains(Validate().Errors, e => e.StartsWith("context.json /exports/layout:", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateDirectory_ReportsRuleProblems()
    {
        WriteMinimalContext();
        Directory.CreateDirectory(Path.Combine(_root, ".aicontext", "rules"));
        File.WriteAllText(Path.Combine(_root, ".aicontext", "rules", "Bad Name.md"), "Body");
        File.WriteAllText(Path.Combine(_root, ".aicontext", "rules", "ok.md"), "---\nowner: me\n---\nBody");

        var result = Validate();

        Assert.Contains(result.Errors, e => e.StartsWith("rules/Bad Name.md:", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, w => w.Contains("unknown frontmatter key 'owner'", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateDirectory_ScansRuleFilesForSecrets()
    {
        WriteMinimalContext();
        Directory.CreateDirectory(Path.Combine(_root, ".aicontext", "rules"));
        File.WriteAllText(Path.Combine(_root, ".aicontext", "rules", "ci.md"), "Use\npassword = hunter2\n");

        Assert.Contains(Validate().Warnings, w => w.Contains(".aicontext/rules/ci.md:2", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateDirectory_SecretInLargeFile_Warns()
    {
        WriteMinimalContext();
        WriteFile("glossary.md", string.Concat(Enumerable.Repeat("filler line\n", 50_000)) + "password = hunter2\n");

        Assert.Contains(Validate().Warnings, w => w.Contains(".aicontext/glossary.md:50001", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateDirectory_FileThatCannotBeRead_WarnsThatTheScanIsIncomplete()
    {
        WriteMinimalContext();
        WriteFile("glossary.md", "Terms.");

        using var locked = new FileStream(Path.Combine(_root, ".aicontext", "glossary.md"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var warnings = Validate().Warnings;

        Assert.Contains(warnings, w => w.StartsWith("Secret scan incomplete for .aicontext/glossary.md:", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateDirectory_WithNestedAgentsWithoutSplit_Warns()
    {
        WriteContext(new
        {
            @schema = CanonicalSchemaUrl,
            specVersion = ExpectedSpecVersion,
            project = new { name = "sample" },
            exports = new { nestedAgents = true }
        });

        var result = Validate();

        Assert.Empty(result.Errors);
        Assert.Contains(result.Warnings, w => w.Contains("nestedAgents has no effect", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateDirectory_WithNestedAgentsAndSplit_DoesNotWarn()
    {
        WriteContext(new
        {
            @schema = CanonicalSchemaUrl,
            specVersion = ExpectedSpecVersion,
            project = new { name = "sample" },
            exports = new { layout = "split", nestedAgents = true }
        });

        Assert.DoesNotContain(Validate().Warnings, w => w.Contains("nestedAgents", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateDirectory_WithNonBooleanNestedAgents_ReturnsError()
    {
        WriteContext(new
        {
            @schema = CanonicalSchemaUrl,
            specVersion = ExpectedSpecVersion,
            project = new { name = "sample" },
            exports = new { nestedAgents = "yes" }
        });

        Assert.Contains(Validate().Errors, e => e.StartsWith("context.json /exports/nestedAgents:", StringComparison.Ordinal));
    }

    private ContextValidationResult Validate() => ContextValidator.ValidateDirectory(_root, CanonicalSchemaUrl);

    private void WriteMinimalContext()
    {
        WriteContext(new { @schema = CanonicalSchemaUrl, specVersion = ExpectedSpecVersion, project = new { name = "sample" } });
    }

    private void WriteContext(object contextJson)
    {
        WriteFile("README.md", "# Project: sample\n\nSummary.");
        var json = JsonSerializer.Serialize(contextJson, new JsonSerializerOptions { WriteIndented = true })
            .Replace("\"schema\":", "\"$schema\":", StringComparison.Ordinal);
        WriteFile("context.json", json);
    }

    private void WriteFile(string name, string content)
    {
        File.WriteAllText(Path.Combine(_root, ".aicontext", name), content);
    }
}
