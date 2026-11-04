using AIContext;
using Xunit;

namespace AIContext.Tests;

public sealed class ProjectInitializerTests : IDisposable
{
    private const string SchemaUrl = "https://aicontext.dev/schema/v1/context.schema.json";
    private const string SpecVersion = "0.1.0";

    private readonly string _basePath = Path.Combine(Path.GetTempPath(), "aicontext-tests-" + Guid.NewGuid().ToString("N"));

    public ProjectInitializerTests()
    {
        Directory.CreateDirectory(_basePath);
    }

    public void Dispose()
    {
        Directory.Delete(_basePath, recursive: true);
    }

    private InitResult Init(InitProfile profile, bool force = false) =>
        ProjectInitializer.Initialize(_basePath, "demo", profile, SchemaUrl, SpecVersion, force);

    [Theory]
    [InlineData(null, InitProfile.Core, true)]
    [InlineData("", InitProfile.Core, true)]
    [InlineData("core", InitProfile.Core, true)]
    [InlineData("Extended", InitProfile.Extended, true)]
    [InlineData("full", InitProfile.Core, false)]
    public void TryParseProfile_Value_ReturnsExpectedResult(string? value, InitProfile expected, bool parsed)
    {
        Assert.Equal(parsed, ProjectInitializer.TryParseProfile(value, out var profile));
        Assert.Equal(expected, profile);
    }

    [Fact]
    public void Initialize_CoreProfile_CreatesOnlyCoreFiles()
    {
        var result = Init(InitProfile.Core);

        Assert.Empty(result.Conflicts);
        Assert.Equal(2, result.Created.Count);
        Assert.True(File.Exists(Path.Combine(_basePath, ".aicontext", "README.md")));
        Assert.True(File.Exists(Path.Combine(_basePath, ".aicontext", "context.json")));
        Assert.False(File.Exists(Path.Combine(_basePath, ".aicontext", "architecture.md")));
    }

    [Fact]
    public void Initialize_CoreProfile_ProducesCoreProfileWithRecommendedWarnings()
    {
        Init(InitProfile.Core);

        var validation = ContextValidator.ValidateDirectory(_basePath, SchemaUrl);

        Assert.Empty(validation.Errors);
        Assert.False(validation.ExtendedProfile);
    }

    [Fact]
    public void Initialize_ExtendedProfile_PassesStrictValidationAsExtendedProfile()
    {
        var result = Init(InitProfile.Extended);

        Assert.Equal(5, result.Created.Count);
        var validation = ContextValidator.ValidateDirectory(_basePath, SchemaUrl);

        Assert.Empty(validation.Errors);
        Assert.Empty(validation.Warnings);
        Assert.True(validation.ExtendedProfile);
    }

    [Fact]
    public void Initialize_ExistingFileWithoutForce_ReportsConflictsAndWritesNothing()
    {
        var contextDirectory = Path.Combine(_basePath, ".aicontext");
        Directory.CreateDirectory(contextDirectory);
        var readmePath = Path.Combine(contextDirectory, "README.md");
        File.WriteAllText(readmePath, "custom");

        var result = Init(InitProfile.Extended);

        Assert.Empty(result.Created);
        Assert.Equal([readmePath], result.Conflicts);
        Assert.Equal("custom", File.ReadAllText(readmePath));
        Assert.False(File.Exists(Path.Combine(contextDirectory, "context.json")));
    }

    [Fact]
    public void Initialize_ExistingFileWithForce_OverwritesFiles()
    {
        var contextDirectory = Path.Combine(_basePath, ".aicontext");
        Directory.CreateDirectory(contextDirectory);
        var readmePath = Path.Combine(contextDirectory, "README.md");
        File.WriteAllText(readmePath, "custom");

        var result = Init(InitProfile.Core, force: true);

        Assert.Empty(result.Conflicts);
        Assert.Contains("# Project: demo", File.ReadAllText(readmePath));
    }
}
