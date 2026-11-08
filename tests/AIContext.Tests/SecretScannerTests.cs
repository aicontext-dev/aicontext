using AIContext;
using Xunit;

namespace AIContext.Tests;

public class SecretScannerTests
{
    [Theory]
    [InlineData("-----BEGIN RSA PRIVATE KEY-----", "private key")]
    [InlineData("key id AKIAIOSFODNN7EXAMPLE", "AWS access key id")]
    [InlineData("token ghp_0123456789abcdefghijklmnopqrstuvwxyzAB", "GitHub token")]
    [InlineData("xoxb-1234567890-abcdefghij", "Slack token")]
    [InlineData("password = hunter2", "hard-coded credential")]
    [InlineData("\"api_key\": \"a1b2c3d4e5f6\"", "hard-coded credential")]
    [InlineData("DB_URL=postgres://admin:S3cret99@db.local/app", "credentials in URL")]
    public void Scan_DetectsSecret(string line, string expectedKind)
    {
        var finding = Assert.Single(SecretScanner.Scan("README.md", $"intro\n{line}\n"));

        Assert.Equal(expectedKind, finding.Kind);
        Assert.Equal(2, finding.Line);
        Assert.Equal("README.md", finding.File);
    }

    [Theory]
    [InlineData("password: required")]
    [InlineData("api_key: <your-key>")]
    [InlineData("token = ${GITHUB_TOKEN}")]
    [InlineData("password = changeme123")]
    [InlineData("postgres://user:password@localhost/db")]
    [InlineData("Never commit a password or an API key.")]
    [InlineData("secret: ********")]
    public void Scan_IgnoresPlaceholdersAndProse(string line)
    {
        Assert.Empty(SecretScanner.Scan("README.md", line));
    }

    [Fact]
    public void Scan_RespectsAllowMarker()
    {
        var content = $"password = hunter2 <!-- {SecretScanner.AllowMarker} -->";

        Assert.Empty(SecretScanner.Scan("README.md", content));
    }

    [Fact]
    public void ScanDirectory_ScansNestedFiles_AndReportsRelativePaths()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aicontext-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "rules"));
        try
        {
            File.WriteAllText(Path.Combine(root, "rules", "ui.md"), "ok\npassword = hunter2\n");
            File.WriteAllText(Path.Combine(root, "image.png"), "password = hunter2");

            var finding = Assert.Single(SecretScanner.ScanDirectory(root));

            Assert.Equal("rules/ui.md", finding.File);
            Assert.Equal(2, finding.Line);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ScanDirectoryReport_FileLargerThanFormerLimit_IsScannedWithCorrectLineNumber()
    {
        using var directory = new TempDirectory();
        var content = string.Concat(Enumerable.Repeat("filler line\n", 60_000)) + "password = hunter2\n";
        File.WriteAllText(Path.Combine(directory.Path, "big.md"), content);
        Assert.True(new FileInfo(Path.Combine(directory.Path, "big.md")).Length > 512 * 1024);

        var report = SecretScanner.ScanDirectoryReport(directory.Path);

        var finding = Assert.Single(report.Findings);
        Assert.Equal("big.md", finding.File);
        Assert.Equal(60_001, finding.Line);
        Assert.Empty(report.Unscanned);
    }

    [Fact]
    public void ScanDirectoryReport_VeryLongSingleLine_IsScanned()
    {
        using var directory = new TempDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "wide.json"), new string('a', 1_000_000) + " password = hunter2");

        var report = SecretScanner.ScanDirectoryReport(directory.Path);

        Assert.Equal(1, Assert.Single(report.Findings).Line);
        Assert.Empty(report.Unscanned);
    }

    [Fact]
    public void ScanDirectoryReport_UnreadableFile_IsReportedInsteadOfSkipped()
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Path, "locked.md");
        File.WriteAllText(path, "password = hunter2");
        File.WriteAllText(Path.Combine(directory.Path, "ok.md"), "password = hunter3");

        using var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var report = SecretScanner.ScanDirectoryReport(directory.Path);

        var unscanned = Assert.Single(report.Unscanned);
        Assert.Equal("locked.md", unscanned.File);
        Assert.Contains("could not be read", unscanned.Reason);
        Assert.Equal("ok.md", Assert.Single(report.Findings).File);
    }

    [Fact]
    public void ScanDirectoryReport_FilesWithOtherExtensions_AreNotScannedAndNotReported()
    {
        using var directory = new TempDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "image.png"), "password = hunter2");
        File.WriteAllText(Path.Combine(directory.Path, "prod.env"), "password = hunter2");

        var report = SecretScanner.ScanDirectoryReport(directory.Path);

        Assert.Empty(report.Findings);
        Assert.Empty(report.Unscanned);
    }

    [Fact]
    public void ScanDirectoryReport_MissingDirectory_ReturnsEmptyReport()
    {
        var report = SecretScanner.ScanDirectoryReport(Path.Combine(Path.GetTempPath(), $"aicontext-tests-{Guid.NewGuid():N}"));

        Assert.Empty(report.Findings);
        Assert.Empty(report.Unscanned);
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"aicontext-tests-{Guid.NewGuid():N}");

        public TempDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            Directory.Delete(Path, true);
        }
    }
}
