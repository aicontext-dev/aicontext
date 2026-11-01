using System.Globalization;
using System.Text.Json;

namespace AIContext;

public sealed record ContextValidationResult(
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    string? LocalSchemaPath);

public static class ContextValidator
{
    public static ContextValidationResult ValidateDirectory(
        string basePath,
        string canonicalSchemaUrl,
        string expectedSpecVersion,
        ISet<string> allowedTargets)
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

        if (root.ValueKind == JsonValueKind.Object)
        {
            ValidateContextJson(root, errors, warnings, allowedTargets, canonicalSchemaUrl, expectedSpecVersion);
        }

        var localSchemaPath = FindLocalSchema(basePath);
        if (localSchemaPath is null)
        {
            warnings.Add("Local schema file not found at schema/v1/context.schema.json in current directory or parent directories.");
        }

        return new ContextValidationResult(errors, warnings, localSchemaPath);
    }

    private static void ValidateContextJson(
        JsonElement root,
        List<string> errors,
        List<string> warnings,
        ISet<string> allowedTargets,
        string canonicalSchemaUrl,
        string expectedSpecVersion)
    {
        if (!TryGetString(root, "$schema", out var schemaValue))
        {
            warnings.Add("context.json is missing '$schema'.");
        }
        else if (!string.Equals(schemaValue, canonicalSchemaUrl, StringComparison.Ordinal))
        {
            warnings.Add($"$schema should be '{canonicalSchemaUrl}'.");
        }

        if (!TryGetString(root, "specVersion", out var specVersion))
        {
            errors.Add("context.json is missing required property: specVersion");
        }
        else if (!string.Equals(specVersion, expectedSpecVersion, StringComparison.Ordinal))
        {
            errors.Add($"specVersion must be '{expectedSpecVersion}'.");
        }

        if (!root.TryGetProperty("project", out var project) || project.ValueKind != JsonValueKind.Object)
        {
            errors.Add("context.json is missing required object: project");
        }
        else
        {
            if (!TryGetString(project, "name", out var projectName) || string.IsNullOrWhiteSpace(projectName))
            {
                errors.Add("project.name is required and must be non-empty.");
            }
        }

        if (root.TryGetProperty("exports", out var exports) && exports.ValueKind == JsonValueKind.Object)
        {
            ValidateExports(exports, errors, allowedTargets);
        }

        if (root.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object)
        {
            if (TryGetString(metadata, "lastReviewed", out var lastReviewed) &&
                !DateTime.TryParseExact(lastReviewed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                errors.Add("metadata.lastReviewed must use ISO date format yyyy-MM-dd.");
            }
        }
    }

    private static void ValidateExports(JsonElement exports, List<string> errors, ISet<string> allowedTargets)
    {
        List<string>? targets = null;

        if (exports.TryGetProperty("targets", out var targetsElement))
        {
            if (targetsElement.ValueKind != JsonValueKind.Array)
            {
                errors.Add("exports.targets must be an array.");
            }
            else
            {
                targets = new List<string>();
                foreach (var targetElement in targetsElement.EnumerateArray())
                {
                    if (targetElement.ValueKind != JsonValueKind.String)
                    {
                        errors.Add("exports.targets must contain only strings.");
                        continue;
                    }

                    var target = targetElement.GetString() ?? string.Empty;
                    targets.Add(target);
                    if (!allowedTargets.Contains(target))
                    {
                        errors.Add($"exports.targets contains unsupported target '{target}'.");
                    }
                }

                var duplicate = targets
                    .GroupBy(x => x, StringComparer.Ordinal)
                    .FirstOrDefault(g => g.Count() > 1);
                if (duplicate is not null)
                {
                    errors.Add($"exports.targets contains duplicate target '{duplicate.Key}'.");
                }
            }
        }

        if (TryGetString(exports, "defaultTarget", out var defaultTarget))
        {
            if (!string.Equals(defaultTarget, "all", StringComparison.Ordinal) &&
                !allowedTargets.Contains(defaultTarget))
            {
                errors.Add("exports.defaultTarget must be 'all' or a supported target.");
            }
            else if (targets is not null &&
                     targets.Count > 0 &&
                     !string.Equals(defaultTarget, "all", StringComparison.Ordinal) &&
                     !targets.Contains(defaultTarget, StringComparer.Ordinal))
            {
                errors.Add("exports.defaultTarget must be 'all' or one of exports.targets.");
            }
        }
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        if (element.TryGetProperty(propertyName, out var propertyValue) && propertyValue.ValueKind == JsonValueKind.String)
        {
            value = propertyValue.GetString() ?? string.Empty;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static string? FindLocalSchema(string startPath)
    {
        var directory = new DirectoryInfo(startPath);
        while (directory is not null)
        {
            var schemaPath = Path.Combine(directory.FullName, "schema", "v1", "context.schema.json");
            if (File.Exists(schemaPath))
            {
                return schemaPath;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
