using System.Text.Json;
using AIContext;
using Xunit;

namespace AIContext.Tests;

// Runs the language-neutral conformance suite (conformance/v0.1) against the reference implementation. The suite
// files are copied next to the test assembly by the project file, so no test reads the repository checkout.
public sealed class ConformanceTests : IDisposable
{
    private static readonly string SuiteDirectory = Path.Combine(AppContext.BaseDirectory, "conformance", "v0.1");

    private readonly string _project = Path.Combine(Path.GetTempPath(), "aicontext-tests-" + Guid.NewGuid().ToString("N"));

    public ConformanceTests()
    {
        Directory.CreateDirectory(Path.Combine(_project, ".aicontext"));
        File.WriteAllText(Path.Combine(_project, ".aicontext", "README.md"), "# Project: demo\n\nSummary.");
    }

    public void Dispose()
    {
        Directory.Delete(_project, true);
    }

    public static IEnumerable<object[]> Cases()
    {
        using var manifest = ReadManifest();
        foreach (var testCase in manifest.RootElement.GetProperty("cases").EnumerateArray())
        {
            yield return
            [
                testCase.GetProperty("file").GetString()!,
                testCase.GetProperty("valid").GetBoolean(),
                testCase.TryGetProperty("pointer", out var pointer) ? pointer.GetString()! : string.Empty
            ];
        }
    }

    private static JsonDocument ReadManifest()
    {
        var path = Path.Combine(SuiteDirectory, "manifest.json");
        Assert.True(File.Exists(path), $"The conformance manifest was not copied to the test output: {path}");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Case_BehavesAsTheManifestSays(string file, bool valid, string pointer)
    {
        File.Copy(Path.Combine(SuiteDirectory, file.Replace('/', Path.DirectorySeparatorChar)), Path.Combine(_project, ".aicontext", "context.json"));

        var result = ContextValidator.ValidateDirectory(_project, ContextSchema.CanonicalUrl);

        if (valid)
        {
            Assert.Empty(result.Errors);
        }
        else
        {
            Assert.Contains(result.Diagnostics, d => d.IsError && d.Location is not null && d.Location.EndsWith("#" + pointer, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Manifest_ListsEveryFixtureAndEveryListedFileExists()
    {
        using var manifest = ReadManifest();
        var listed = manifest.RootElement.GetProperty("cases").EnumerateArray()
            .Select(c => c.GetProperty("file").GetString()!)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        var onDisk = new[] { "valid", "invalid" }
            .SelectMany(directory => Directory.EnumerateFiles(Path.Combine(SuiteDirectory, directory), "*.json")
                .Select(f => $"{directory}/{Path.GetFileName(f)}"))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(onDisk, listed);
        Assert.Equal(listed.Count, listed.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Manifest_DeclaresTheSpecificationVersionTheValidatorSupports()
    {
        using var manifest = ReadManifest();

        Assert.Equal("0.1.0", manifest.RootElement.GetProperty("specVersion").GetString());
        Assert.Equal("v0.1", Path.GetFileName(SuiteDirectory));
    }

    [Fact]
    public void Manifest_EveryCaseNamesASpecSectionThatExists()
    {
        var headings = ReadSpecHeadings();
        using var manifest = ReadManifest();

        foreach (var testCase in manifest.RootElement.GetProperty("cases").EnumerateArray())
        {
            var file = testCase.GetProperty("file").GetString()!;
            Assert.True(testCase.TryGetProperty("spec", out var spec), $"{file} does not name a SPEC section.");
            Assert.Contains(spec.GetString()!, headings);
        }
    }

    [Fact]
    public void Manifest_BeyondSchemaCasesAreInvalidOnes()
    {
        using var manifest = ReadManifest();

        foreach (var testCase in manifest.RootElement.GetProperty("cases").EnumerateArray()
                     .Where(c => c.TryGetProperty("beyondSchema", out var beyond) && beyond.GetBoolean()))
        {
            Assert.False(testCase.GetProperty("valid").GetBoolean());
            Assert.Equal("5.2", testCase.GetProperty("spec").GetString());
        }
    }

    // Drift guard: a property the schema defines must be described in the property table of SPEC 5.1.
    [Fact]
    public void Spec_DescribesEveryPropertyOfTheSchema()
    {
        var spec = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "spec", "SPEC.md"));
        using var schema = JsonDocument.Parse(ContextSchema.ReadText());
        var properties = schema.RootElement.GetProperty("properties");

        var paths = new List<string>();
        foreach (var property in properties.EnumerateObject())
        {
            if (property.Value.TryGetProperty("properties", out var children))
            {
                paths.AddRange(children.EnumerateObject().Select(child => $"{property.Name}.{child.Name}"));
            }
            else
            {
                paths.Add(property.Name);
            }
        }

        Assert.NotEmpty(paths);
        foreach (var path in paths)
        {
            Assert.True(spec.Contains($"| `{path}` |", StringComparison.Ordinal), $"SPEC 5.1 does not describe '{path}'.");
        }
    }

    // Section numbers of the headings in SPEC.md, for example "5.1" for "### 5.1 Properties" and "5" for "## 5. ...".
    private static HashSet<string> ReadSpecHeadings()
    {
        var headings = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "spec", "SPEC.md")))
        {
            var match = System.Text.RegularExpressions.Regex.Match(line, @"^#{2,4} (\d+(?:\.\d+)*)\.? ");
            if (match.Success)
            {
                headings.Add(match.Groups[1].Value);
            }
        }

        return headings;
    }

    [Fact]
    public void Manifest_EveryInvalidCaseNamesAPointer()
    {
        using var manifest = ReadManifest();

        foreach (var testCase in manifest.RootElement.GetProperty("cases").EnumerateArray())
        {
            Assert.Equal(!testCase.GetProperty("valid").GetBoolean(), testCase.TryGetProperty("pointer", out _));
        }
    }
}
