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
}
