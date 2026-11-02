using System.Text.Json;

namespace AIContext;

public sealed record ContextValidationResult(
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    bool ExtendedProfile);

public static class ContextValidator
{
    public static readonly IReadOnlyList<string> RecommendedFiles =
        ["architecture.md", "decisions.md", "constraints.md"];

    public static ContextValidationResult ValidateDirectory(string basePath, string canonicalSchemaUrl)
    {
        var contextDirectory = Path.Combine(basePath, ".aicontext");
        var readmePath = Path.Combine(contextDirectory, "README.md");
        var contextPath = Path.Combine(contextDirectory, "context.json");

        var errors = new List<string>();
        var warnings = new List<string>();

        if (!File.Exists(readmePath))
        {
            errors.Add($"Missing required file: {readmePath}");
        }
        else if (string.IsNullOrWhiteSpace(File.ReadAllText(readmePath)))
        {
            errors.Add($"{readmePath} is empty.");
        }

        JsonElement root = default;
        if (!File.Exists(contextPath))
        {
            errors.Add($"Missing required file: {contextPath}");
        }
        else
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(contextPath));
                root = document.RootElement.Clone();
            }
            catch (JsonException ex)
            {
                errors.Add($"Invalid JSON in {contextPath}. {ex.Message}");
            }
        }

        var contextValid = false;
        if (root.ValueKind != JsonValueKind.Undefined)
        {
            var issues = ContextSchema.Validate(root);
            foreach (var issue in issues)
            {
                var location = string.IsNullOrEmpty(issue.Location) ? "(root)" : issue.Location;
                errors.Add($"context.json {location}: {issue.Message}");
            }

            contextValid = issues.Count == 0;
            if (root.ValueKind == JsonValueKind.Object)
            {
                ValidateCrossFieldRules(root, errors, warnings, canonicalSchemaUrl);
            }
        }

        var ruleLoad = RuleLoader.Load(contextDirectory);
        errors.AddRange(ruleLoad.Errors);
        warnings.AddRange(ruleLoad.Warnings);

        var extendedProfile = ValidateRecommendedFiles(contextDirectory, warnings) &&
                              errors.Count == 0 &&
                              contextValid;

        foreach (var finding in SecretScanner.ScanDirectory(contextDirectory))
        {
            warnings.Add(
                $"Possible secret ({finding.Kind}) in .aicontext/{finding.File}:{finding.Line}. " +
                $"Remove it, or add '{SecretScanner.AllowMarker}' to the line if it is an intentional example.");
        }

        return new ContextValidationResult(errors, warnings, extendedProfile);
    }

    // Rules JSON Schema cannot express (or reports poorly).
    private static void ValidateCrossFieldRules(
        JsonElement root,
        List<string> errors,
        List<string> warnings,
        string canonicalSchemaUrl)
    {
        if (!TryGetString(root, "$schema", out var schemaValue))
        {
            warnings.Add("context.json is missing '$schema'.");
        }
        else if (!string.Equals(schemaValue, canonicalSchemaUrl, StringComparison.Ordinal))
        {
            warnings.Add($"$schema should be '{canonicalSchemaUrl}'.");
        }

        if (root.TryGetProperty("project", out var project) &&
            TryGetString(project, "name", out var projectName) &&
            projectName.Length > 0 &&
            string.IsNullOrWhiteSpace(projectName))
        {
            errors.Add("project.name must not be whitespace only.");
        }

        if (!root.TryGetProperty("exports", out var exports) || exports.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        WarnWhenWindsurfAndDevinOverlap(exports, warnings);

        if (!TryGetString(exports, "defaultTarget", out var defaultTarget) ||
            string.Equals(defaultTarget, "all", StringComparison.Ordinal))
        {
            return;
        }

        if (exports.TryGetProperty("targets", out var targetsElement) &&
            targetsElement.ValueKind == JsonValueKind.Array)
        {
            var targets = targetsElement.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString())
                .ToList();

            if (targets.Count > 0 && !targets.Contains(defaultTarget, StringComparer.Ordinal))
            {
                errors.Add("exports.defaultTarget must be 'all' or one of exports.targets.");
            }
        }
    }

    // Devin loads both .devin/rules and .windsurf/rules, so enabling both duplicates the context.
    private static void WarnWhenWindsurfAndDevinOverlap(JsonElement exports, List<string> warnings)
    {
        if (!exports.TryGetProperty("targets", out var targetsElement) ||
            targetsElement.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var targets = targetsElement.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString())
            .ToList();

        if (targets.Contains("windsurf") && targets.Contains("devin"))
        {
            warnings.Add(
                "exports.targets contains both 'windsurf' and 'devin'. Devin also loads .windsurf/rules, " +
                "so the same rules would be loaded twice. Keep only one of them.");
        }
    }

    // Returns true when all recommended files exist and are non-empty (Extended profile, SPEC 8.2).
    private static bool ValidateRecommendedFiles(string contextDirectory, List<string> warnings)
    {
        var allPresent = true;
        foreach (var fileName in RecommendedFiles)
        {
            var path = Path.Combine(contextDirectory, fileName);
            if (!File.Exists(path))
            {
                warnings.Add($"Recommended file missing: .aicontext/{fileName}.");
                allPresent = false;
            }
            else if (string.IsNullOrWhiteSpace(File.ReadAllText(path)))
            {
                warnings.Add($"Recommended file is empty: .aicontext/{fileName}.");
                allPresent = false;
            }
        }

        return allPresent;
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var propertyValue) &&
            propertyValue.ValueKind == JsonValueKind.String)
        {
            value = propertyValue.GetString() ?? string.Empty;
            return true;
        }

        value = string.Empty;
        return false;
    }
}
