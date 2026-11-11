using System.Text.Json;
using Xunit;

namespace AIContext.Tests;

// One entry of "globCases" of the conformance manifest: the pattern (one item of `paths`) and exactly the files of
// the fixed set it matches.
internal sealed record GlobCase(string Pattern, string Spec, IReadOnlyList<string> Matches);

// Reads the glob part of the conformance suite (conformance/v0.1), which the project file copies next to the test
// assembly. Shared by the conformance runner and by the tests that check the translation for each tool against it.
internal static class ConformanceSuite
{
    public static string SuiteDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "conformance", "v0.1");

    public static JsonDocument ReadManifest()
    {
        var path = Path.Combine(SuiteDirectory, "manifest.json");
        Assert.True(File.Exists(path), $"The conformance manifest was not copied to the test output: {path}");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    public static IReadOnlyList<string> GlobFiles()
    {
        using var manifest = ReadManifest();
        return manifest.RootElement.GetProperty("globFiles").EnumerateArray().Select(f => f.GetString()!).ToList();
    }

    public static IReadOnlyList<GlobCase> GlobCases()
    {
        using var manifest = ReadManifest();
        return manifest.RootElement.GetProperty("globCases").EnumerateArray()
            .Select(c => new GlobCase(
                c.GetProperty("pattern").GetString()!,
                c.GetProperty("spec").GetString()!,
                c.GetProperty("matches").EnumerateArray().Select(m => m.GetString()!).ToList()))
            .ToList();
    }

    public static IReadOnlyList<(string Pattern, string Spec)> GlobInvalid()
    {
        using var manifest = ReadManifest();
        return manifest.RootElement.GetProperty("globInvalid").EnumerateArray()
            .Select(c => (c.GetProperty("pattern").GetString()!, c.GetProperty("spec").GetString()!))
            .ToList();
    }
}
