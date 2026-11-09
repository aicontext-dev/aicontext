using System.Text.Json;

namespace AIContext;

public sealed record ContextValidationResult(IReadOnlyList<Diagnostic> Diagnostics, bool ExtendedProfile)
{
    public IReadOnlyList<string> Errors => Diagnostics.ErrorMessages();

    public IReadOnlyList<string> Warnings => Diagnostics.WarningMessages();
}

public static class ContextValidator
{
    private const string ContextDirectoryName = ".aicontext";
    private const string ProjectNamePointer = "/project/name";

    public static readonly IReadOnlyList<string> RecommendedFiles =
        ["architecture.md", "decisions.md", "constraints.md"];

    public static ContextValidationResult ValidateDirectory(string basePath, string canonicalSchemaUrl)
    {
        var contextDirectory = Path.Combine(basePath, ContextDirectoryName);
        var readmePath = Path.Combine(contextDirectory, "README.md");
        var contextPath = Path.Combine(contextDirectory, "context.json");
        const string readmeLocation = $"{ContextDirectoryName}/README.md";
        const string contextLocation = $"{ContextDirectoryName}/context.json";

        var diagnostics = new List<Diagnostic>();

        if (!File.Exists(readmePath))
        {
            diagnostics.Add(Diagnostic.Error(DiagnosticCodes.MissingRequiredFile, $"Missing required file: {readmePath}", readmeLocation));
        }
        else if (string.IsNullOrWhiteSpace(File.ReadAllText(readmePath)))
        {
            diagnostics.Add(Diagnostic.Error(DiagnosticCodes.EmptyReadme, $"{readmePath} is empty.", readmeLocation));
        }

        JsonElement root = default;
        if (!File.Exists(contextPath))
        {
            diagnostics.Add(Diagnostic.Error(DiagnosticCodes.MissingRequiredFile, $"Missing required file: {contextPath}", contextLocation));
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
                diagnostics.Add(Diagnostic.Error(DiagnosticCodes.InvalidJson, $"Invalid JSON in {contextPath}. {ex.Message}", contextLocation));
            }
        }

        var contextValid = false;
        if (root.ValueKind != JsonValueKind.Undefined)
        {
            var issues = ContextSchema.Validate(root);
            foreach (var issue in issues)
            {
                // A non-empty name without a non-whitespace character has its own stable code (AIC1005);
                // an empty name is already reported by minLength.
                if (issue is { Keyword: "pattern", Location: ProjectNamePointer })
                {
                    if (!issues.Any(i => i.Location == ProjectNamePointer && i.Keyword == "minLength"))
                    {
                        diagnostics.Add(Diagnostic.Error(
                            DiagnosticCodes.ProjectNameWhitespace,
                            "project.name must not be whitespace only.",
                            $"{contextLocation}#{ProjectNamePointer}"));
                    }

                    continue;
                }

                var location = string.IsNullOrEmpty(issue.Location) ? "(root)" : issue.Location;
                diagnostics.Add(Diagnostic.Error(
                    DiagnosticCodes.SchemaViolation,
                    $"context.json {location}: {issue.Message}",
                    $"{contextLocation}#{issue.Location}"));
            }

            contextValid = issues.Count == 0;
            if (root.ValueKind == JsonValueKind.Object)
            {
                ValidateCrossFieldRules(root, diagnostics, canonicalSchemaUrl);
            }
        }

        diagnostics.AddRange(RuleLoader.Load(contextDirectory).Diagnostics);

        var extendedProfile = ValidateRecommendedFiles(contextDirectory, diagnostics) &&
                              !diagnostics.Any(d => d.IsError) &&
                              contextValid;

        foreach (var finding in PlaceholderScanner.ScanDirectory(contextDirectory))
        {
            diagnostics.Add(Diagnostic.Warning(
                DiagnosticCodes.PlaceholderContent,
                $"Placeholder text in .aicontext/{finding.File}:{finding.Line}. " +
                $"Replace it with real content, or add '{PlaceholderScanner.AllowMarker}' to the line if it is intentional.",
                $"{ContextDirectoryName}/{finding.File}:{finding.Line}"));
        }

        var secretScan = SecretScanner.ScanDirectoryReport(contextDirectory);
        foreach (var finding in secretScan.Findings)
        {
            diagnostics.Add(Diagnostic.Warning(
                DiagnosticCodes.PossibleSecret,
                $"Possible secret ({finding.Kind}) in .aicontext/{finding.File}:{finding.Line}. " +
                $"Remove it, or add '{SecretScanner.AllowMarker}' to the line if it is an intentional example.",
                $"{ContextDirectoryName}/{finding.File}:{finding.Line}"));
        }

        foreach (var file in secretScan.Unscanned)
        {
            diagnostics.Add(Diagnostic.Warning(
                DiagnosticCodes.SecretScanIncomplete,
                $"Secret scan incomplete for .aicontext/{file.File}: {file.Reason}. Check it manually.",
                $"{ContextDirectoryName}/{file.File}"));
        }

        return new ContextValidationResult(diagnostics, extendedProfile);
    }

    // Rules JSON Schema cannot express (or reports poorly).
    private static void ValidateCrossFieldRules(
        JsonElement root,
        List<Diagnostic> diagnostics,
        string canonicalSchemaUrl)
    {
        const string location = $"{ContextDirectoryName}/context.json";

        if (!TryGetString(root, "$schema", out var schemaValue))
        {
            diagnostics.Add(Diagnostic.Warning(DiagnosticCodes.MissingSchemaProperty, "context.json is missing '$schema'.", location));
        }
        else if (!string.Equals(schemaValue, canonicalSchemaUrl, StringComparison.Ordinal))
        {
            diagnostics.Add(Diagnostic.Warning(
                DiagnosticCodes.SchemaUrlMismatch,
                $"$schema should be '{canonicalSchemaUrl}'.",
                $"{location}#/$schema"));
        }

        if (!root.TryGetProperty("exports", out var exports) || exports.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        WarnWhenWindsurfAndDevinOverlap(exports, diagnostics);

        if (exports.TryGetProperty("nestedAgents", out var nestedAgents) &&
            nestedAgents.ValueKind == JsonValueKind.True &&
            !(exports.TryGetProperty("layout", out var layout) && layout.ValueKind == JsonValueKind.String && layout.GetString() == "split"))
        {
            diagnostics.Add(Diagnostic.Warning(
                DiagnosticCodes.NestedAgentsWithoutSplit,
                "exports.nestedAgents has no effect unless exports.layout is 'split'.",
                $"{location}#/exports/nestedAgents"));
        }

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
                diagnostics.Add(Diagnostic.Error(
                    DiagnosticCodes.DefaultTargetNotInTargets,
                    "exports.defaultTarget must be 'all' or one of exports.targets.",
                    $"{location}#/exports/defaultTarget"));
            }
        }
    }

    // Devin loads both .devin/rules and .windsurf/rules, so enabling both duplicates the context.
    private static void WarnWhenWindsurfAndDevinOverlap(JsonElement exports, List<Diagnostic> diagnostics)
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
            diagnostics.Add(Diagnostic.Warning(
                DiagnosticCodes.WindsurfDevinOverlap,
                "exports.targets contains both 'windsurf' and 'devin'. Devin also loads .windsurf/rules, " +
                "so the same rules would be loaded twice. Keep only one of them.",
                $"{ContextDirectoryName}/context.json#/exports/targets"));
        }
    }

    // Returns true when all recommended files exist and are non-empty (Extended profile, SPEC 8.2).
    private static bool ValidateRecommendedFiles(string contextDirectory, List<Diagnostic> diagnostics)
    {
        var allPresent = true;
        foreach (var fileName in RecommendedFiles)
        {
            var path = Path.Combine(contextDirectory, fileName);
            if (!File.Exists(path))
            {
                diagnostics.Add(Diagnostic.Warning(
                    DiagnosticCodes.RecommendedFileMissing,
                    $"Recommended file missing: .aicontext/{fileName}.",
                    $"{ContextDirectoryName}/{fileName}"));
                allPresent = false;
            }
            else if (string.IsNullOrWhiteSpace(File.ReadAllText(path)))
            {
                diagnostics.Add(Diagnostic.Warning(
                    DiagnosticCodes.RecommendedFileEmpty,
                    $"Recommended file is empty: .aicontext/{fileName}.",
                    $"{ContextDirectoryName}/{fileName}"));
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
