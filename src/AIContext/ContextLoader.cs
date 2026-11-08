using System.Text.Json;

namespace AIContext;

public sealed record LoadedContext(string BasePath, JsonElement Root, ExportContext Context);

public sealed record ContextLoadResult(LoadedContext? Loaded, IReadOnlyList<Diagnostic> Diagnostics)
{
    public IReadOnlyList<string> Errors => Diagnostics.ErrorMessages();
}

// Reads .aicontext/ into the model used for exporting and reporting.
public static class ContextLoader
{
    public static ContextLoadResult Load(string basePath)
    {
        var contextDirectory = Path.Combine(basePath, ".aicontext");
        var readmePath = Path.Combine(contextDirectory, "README.md");
        var contextPath = Path.Combine(contextDirectory, "context.json");

        // Export and status accept exactly what validate accepts: any validation error stops them,
        // warnings never do (D13).
        var validation = ContextValidator.ValidateDirectory(basePath, ContextSchema.CanonicalUrl);
        if (validation.Errors.Count > 0)
        {
            return new ContextLoadResult(null, validation.Diagnostics);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(contextPath));
        var root = document.RootElement.Clone();

        var ruleLoad = RuleLoader.Load(contextDirectory);

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

        return new ContextLoadResult(new LoadedContext(basePath, root, exportContext), validation.Diagnostics);
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
