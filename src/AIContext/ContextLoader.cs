using System.Text.Json;

namespace AIContext;

public sealed record LoadedContext(string BasePath, JsonElement Root, ExportContext Context);

public sealed record ContextLoadResult(LoadedContext? Loaded, IReadOnlyList<string> Errors);

// Reads .aicontext/ into the model used for exporting and reporting.
public static class ContextLoader
{
    public static ContextLoadResult Load(string basePath)
    {
        var contextDirectory = Path.Combine(basePath, ".aicontext");
        var readmePath = Path.Combine(contextDirectory, "README.md");
        var contextPath = Path.Combine(contextDirectory, "context.json");

        var errors = new List<string>();
        if (!File.Exists(readmePath))
        {
            errors.Add($"Missing required file: {readmePath}");
        }

        if (!File.Exists(contextPath))
        {
            errors.Add($"Missing required file: {contextPath}");
        }

        if (errors.Count > 0)
        {
            return new ContextLoadResult(null, errors);
        }

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(contextPath));
            root = document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            return new ContextLoadResult(null, [$"Invalid JSON in {contextPath}. {ex.Message}"]);
        }

        var ruleLoad = RuleLoader.Load(contextDirectory);
        if (ruleLoad.Errors.Count > 0)
        {
            return new ContextLoadResult(null, ruleLoad.Errors);
        }

        var projectName = ReadProjectName(root);
        if (string.IsNullOrWhiteSpace(projectName))
        {
            projectName = new DirectoryInfo(basePath).Name;
        }

        var exportContext = new ExportContext(
            projectName,
            File.ReadAllText(readmePath),
            ReadSection(contextDirectory, "architecture.md"),
            ReadSection(contextDirectory, "decisions.md"),
            ReadSection(contextDirectory, "patterns.md"),
            ReadSection(contextDirectory, "constraints.md"),
            ReadSection(contextDirectory, "glossary.md"),
            ruleLoad.Rules);

        return new ContextLoadResult(new LoadedContext(basePath, root, exportContext), []);
    }

    private static string? ReadProjectName(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("project", out var project) &&
            project.ValueKind == JsonValueKind.Object &&
            project.TryGetProperty("name", out var name) &&
            name.ValueKind == JsonValueKind.String)
        {
            return name.GetString();
        }

        return null;
    }

    private static string? ReadSection(string contextDirectory, string fileName)
    {
        var filePath = Path.Combine(contextDirectory, fileName);
        if (!File.Exists(filePath))
        {
            return null;
        }

        var content = File.ReadAllText(filePath).Trim();
        return string.IsNullOrWhiteSpace(content) ? null : content;
    }
}
