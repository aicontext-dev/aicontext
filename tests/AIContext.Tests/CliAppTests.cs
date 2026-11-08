using AIContext.Cli;
using Xunit;

namespace AIContext.Tests;

public sealed class CliAppTests : IDisposable
{
    private readonly string _basePath = Path.Combine(Path.GetTempPath(), "aicontext-tests-" + Guid.NewGuid().ToString("N"));

    public CliAppTests()
    {
        Directory.CreateDirectory(_basePath);
    }

    public void Dispose()
    {
        Directory.Delete(_basePath, recursive: true);
    }

    private sealed record CliResult(int ExitCode, string Output, string Error);

    // The temp directory is the working directory, so a command without --path can never touch the repository checkout.
    private CliResult Run(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = CliApp.Run(args, output, error, _basePath);
        return new CliResult(exitCode, output.ToString(), error.ToString());
    }

    private void InitProject(string profile = "core")
    {
        Assert.Equal(0, Run("init", "--name", "demo", "--profile", profile).ExitCode);
    }

    private string PathOf(string relative) => Path.Combine(_basePath, relative);

    [Fact]
    public void Run_NoArguments_PrintsHelp()
    {
        var result = Run();

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Usage:", result.Output);
        Assert.Equal(string.Empty, result.Error);
    }

    [Fact]
    public void Run_Version_PrintsToolAndSpecificationVersion()
    {
        var result = Run("--version");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("aicontext ", result.Output);
        Assert.Contains("Specification version: 0.1.0", result.Output);
    }

    [Fact]
    public void Run_UnknownCommand_ReturnsError()
    {
        var result = Run("bogus");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Unknown command 'bogus'", result.Error);
    }

    [Theory]
    [InlineData("init", "--help")]
    [InlineData("validate", "--help")]
    [InlineData("export", "-h")]
    [InlineData("status", "--help")]
    public void Run_CommandHelp_PrintsUsageAndSucceeds(string command, string helpFlag)
    {
        var result = Run(command, helpFlag);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains($"aicontext {command} [--", result.Output);
        Assert.Equal(string.Empty, result.Error);
    }

    [Fact]
    public void Validate_UnknownOption_ReturnsErrorWithoutValidating()
    {
        InitProject();

        var result = Run("validate", "--strcit");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Unknown option '--strcit'", result.Error);
        Assert.Contains("--strict", result.Error);
        Assert.DoesNotContain("validation passed", result.Output);
    }

    [Fact]
    public void Validate_OptionOfAnotherCommand_ReturnsError()
    {
        InitProject();

        var result = Run("validate", "--force");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Unknown option '--force'", result.Error);
    }

    [Fact]
    public void Validate_PositionalArgument_ReturnsError()
    {
        InitProject();

        var result = Run("validate", "strict");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Unknown argument 'strict'", result.Error);
    }

    [Fact]
    public void Validate_StrictWithNonBooleanValue_ReturnsError()
    {
        InitProject();

        var result = Run("validate", "--strict", "no");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("'--strict' does not take a value", result.Error);
    }

    [Theory]
    [InlineData("--strict")]
    [InlineData("--STRICT")]
    public void Validate_StrictOnProjectWithWarnings_ReturnsError(string flag)
    {
        InitProject();

        var result = Run("validate", flag);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("treated as errors", result.Error);
    }

    [Fact]
    public void Validate_StrictExplicitTrue_TreatsWarningsAsErrors()
    {
        InitProject();

        Assert.Equal(1, Run("validate", "--strict", "true").ExitCode);
    }

    [Fact]
    public void Validate_StrictExplicitFalse_DoesNotTreatWarningsAsErrors()
    {
        InitProject();

        var result = Run("validate", "--strict", "false");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("validation passed", result.Output);
    }

    [Fact]
    public void Init_PathWithoutValue_ReturnsErrorAndLeavesExistingFilesUntouched()
    {
        InitProject();
        var readme = PathOf(Path.Combine(".aicontext", "README.md"));
        File.WriteAllText(readme, "hand-written content");

        var result = Run("init", "--path", "--force");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("'--path' requires a value", result.Error);
        Assert.Equal("hand-written content", File.ReadAllText(readme));
    }

    [Fact]
    public void Init_NameWithoutValue_ReturnsError()
    {
        var result = Run("init", "--name");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("'--name' requires a value", result.Error);
        Assert.False(Directory.Exists(PathOf(".aicontext")));
    }

    [Fact]
    public void Export_TargetWithoutValue_ReturnsError()
    {
        InitProject();

        var result = Run("export", "--target", "--dry-run");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("'--target' requires a value", result.Error);
    }

    [Fact]
    public void Export_DuplicateOption_ReturnsError()
    {
        InitProject();

        var result = Run("export", "--target", "agents", "--target", "claude");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("'--target' was specified more than once", result.Error);
        Assert.False(File.Exists(PathOf("AGENTS.md")));
    }

    [Fact]
    public void Export_MistypedCheckOption_WritesNothing()
    {
        InitProject();

        var result = Run("export", "--target", "agents", "--chek");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Unknown option '--chek'", result.Error);
        Assert.False(File.Exists(PathOf("AGENTS.md")));
    }

    [Fact]
    public void Status_UnknownOption_ReturnsError()
    {
        InitProject();

        var result = Run("status", "--strict");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Unknown option '--strict'", result.Error);
    }

    [Fact]
    public void Status_InitializedProject_ReportsNotExported()
    {
        InitProject();

        var result = Run("status");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("not exported", result.Output);
    }

    [Fact]
    public void InitValidateExportCheck_ExtendedProject_FollowsExitCodeContract()
    {
        Assert.Equal(0, Run("init", "--name", "demo", "--profile", "extended").ExitCode);
        Assert.Equal(0, Run("validate", "--strict").ExitCode);
        Assert.Equal(1, Run("export", "--target", "all", "--check").ExitCode);
        Assert.Equal(0, Run("export", "--target", "all").ExitCode);
        Assert.Equal(0, Run("export", "--target", "all", "--check").ExitCode);

        File.AppendAllText(PathOf(Path.Combine(".aicontext", "README.md")), "\nChanged.\n");
        var stale = Run("export", "--target", "all", "--check");

        Assert.Equal(1, stale.ExitCode);
        Assert.Contains("out of date", stale.Error);
    }

    [Fact]
    public void Export_HandWrittenFile_ReturnsConflictAndKeepsFile()
    {
        InitProject();
        File.WriteAllText(PathOf("AGENTS.md"), "hand written");

        var result = Run("export", "--target", "agents");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("No files were written", result.Error);
        Assert.Equal("hand written", File.ReadAllText(PathOf("AGENTS.md")));
    }
}
