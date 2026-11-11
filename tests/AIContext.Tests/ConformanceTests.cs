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

    public static IEnumerable<object[]> RuleCases()
    {
        using var manifest = ReadManifest();
        foreach (var testCase in manifest.RootElement.GetProperty("ruleCases").EnumerateArray())
        {
            yield return [testCase.GetProperty("file").GetString()!];
        }
    }

    [Theory]
    [MemberData(nameof(RuleCases))]
    public void RuleCase_BehavesAsTheManifestSays(string file)
    {
        using var manifest = ReadManifest();
        var testCase = manifest.RootElement.GetProperty("ruleCases").EnumerateArray()
            .Single(c => c.GetProperty("file").GetString() == file);
        var installAs = testCase.TryGetProperty("installAs", out var name) ? name.GetString()! : Path.GetFileName(file);

        var rulesDirectory = Path.Combine(_project, ".aicontext", "rules");
        Directory.CreateDirectory(rulesDirectory);
        File.Copy(Path.Combine(SuiteDirectory, file.Replace('/', Path.DirectorySeparatorChar)), Path.Combine(rulesDirectory, installAs));

        var result = RuleLoader.Load(Path.Combine(_project, ".aicontext"));
        var location = $".aicontext/rules/{installAs}";

        if (!testCase.GetProperty("valid").GetBoolean())
        {
            Assert.Contains(result.Diagnostics, d => d.IsError && d.Location == location);
            Assert.Empty(result.Rules);
            return;
        }

        Assert.DoesNotContain(result.Diagnostics, d => d.IsError);
        var rule = Assert.Single(result.Rules);
        var expect = testCase.GetProperty("expect");

        Assert.Equal(expect.GetProperty("kind").GetString(), KindName(rule.Kind));
        Assert.Equal(expect.GetProperty("paths").EnumerateArray().Select(p => p.GetString()!), rule.Paths);
        Assert.Equal(expect.GetProperty("description").GetString(), rule.Description);

        if (testCase.TryGetProperty("warns", out var warns) && warns.GetBoolean())
        {
            Assert.Contains(result.Diagnostics, d => !d.IsError && d.Location == location);
        }
    }

    private static string KindName(RuleKind kind) => kind switch
    {
        RuleKind.Always => "always",
        RuleKind.OnDemand => "on-demand",
        RuleKind.Scoped => "scoped",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    [Fact]
    public void RuleManifest_ListsEveryFixtureAndEveryListedFileExists()
    {
        using var manifest = ReadManifest();
        var listed = manifest.RootElement.GetProperty("ruleCases").EnumerateArray()
            .Select(c => c.GetProperty("file").GetString()!)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        var onDisk = new[] { "valid", "invalid" }
            .SelectMany(directory => Directory.EnumerateFiles(Path.Combine(SuiteDirectory, "rules", directory), "*.md")
                .Select(f => $"rules/{directory}/{Path.GetFileName(f)}"))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(listed);
        Assert.Equal(onDisk, listed);
        Assert.Equal(listed.Count, listed.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void RuleManifest_EveryCaseNamesASpecSectionThatExistsAndDeclaresItsOutcome()
    {
        var headings = ReadSpecHeadings();
        using var manifest = ReadManifest();

        foreach (var testCase in manifest.RootElement.GetProperty("ruleCases").EnumerateArray())
        {
            var file = testCase.GetProperty("file").GetString()!;
            Assert.Contains(testCase.GetProperty("spec").GetString()!, headings);

            var valid = testCase.GetProperty("valid").GetBoolean();
            Assert.Equal(file.StartsWith("rules/valid/", StringComparison.Ordinal), valid);
            Assert.Equal(valid, testCase.TryGetProperty("expect", out _));
        }
    }

    public static IEnumerable<object[]> GlobCaseData() =>
        ConformanceSuite.GlobCases().Select(c => new object[] { c.Pattern });

    public static IEnumerable<object[]> GlobInvalidData() =>
        ConformanceSuite.GlobInvalid().Select(c => new object[] { c.Pattern });

    [Theory]
    [MemberData(nameof(GlobCaseData))]
    public void GlobCase_MatchesExactlyTheFilesTheManifestListsAndNoOther(string pattern)
    {
        var testCase = ConformanceSuite.GlobCases().Single(c => c.Pattern == pattern);

        var actual = ConformanceSuite.GlobFiles().Where(file => PortableGlob.IsMatch(pattern, file));

        Assert.Equal(testCase.Matches.Order(StringComparer.Ordinal), actual.Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(GlobInvalidData))]
    public void GlobInvalid_IsRefusedByTheParserAndByARuleThatUsesIt(string pattern)
    {
        Assert.False(PortableGlob.Parse(pattern).IsValid);
        Assert.False(PortableGlob.IsMatch(pattern, "src/app.ts"));

        var rulesDirectory = Path.Combine(_project, ".aicontext", "rules");
        Directory.CreateDirectory(rulesDirectory);
        var quoted = pattern.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(rulesDirectory, "ui.md"), $"---\npaths: [\"{quoted}\"]\n---\nBody.\n");

        var result = RuleLoader.Load(Path.Combine(_project, ".aicontext"));

        Assert.Empty(result.Rules);
        Assert.Contains(result.Diagnostics, d => d.IsError && d.Location == ".aicontext/rules/ui.md");
    }

    [Fact]
    public void GlobManifest_EveryCaseNamesASpecSectionThatExistsAndTheMatchesAreFilesOfTheSet()
    {
        var headings = ReadSpecHeadings();
        var files = ConformanceSuite.GlobFiles();

        Assert.Equal(files.Count, files.Distinct(StringComparer.Ordinal).Count());
        Assert.All(files, file => Assert.DoesNotContain('\\', file));

        var cases = ConformanceSuite.GlobCases();
        Assert.Equal(cases.Count, cases.Select(c => c.Pattern).Distinct(StringComparer.Ordinal).Count());
        foreach (var testCase in cases)
        {
            Assert.Contains(testCase.Spec, headings);
            Assert.All(testCase.Matches, match => Assert.Contains(match, files));
            Assert.Equal(testCase.Matches.Count, testCase.Matches.Distinct(StringComparer.Ordinal).Count());
        }

        var invalid = ConformanceSuite.GlobInvalid();
        Assert.Equal(invalid.Count, invalid.Select(c => c.Pattern).Distinct(StringComparer.Ordinal).Count());
        Assert.All(invalid, c => Assert.Contains(c.Spec, headings));
    }
}
