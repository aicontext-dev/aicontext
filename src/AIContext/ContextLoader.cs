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

        // The files are read again after validation; one that became unreadable (or that became a link out of the
        // project) in between is an error like any other.
        var reader = new ContextFileReader(basePath);
        var diagnostics = new List<Diagnostic>(validation.Diagnostics);
        var contextText = reader.TryReadAllText(contextPath, DiagnosticCodes.UnreadableContextFile, ".aicontext/context.json", diagnostics);
        var readme = reader.TryReadAllText(readmePath, DiagnosticCodes.UnreadableContextFile, ".aicontext/README.md", diagnostics);
        var architecture = ReadSection(reader, contextDirectory, "architecture.md", diagnostics);
        var decisions = ReadSection(reader, contextDirectory, "decisions.md", diagnostics);
        var patterns = ReadSection(reader, contextDirectory, "patterns.md", diagnostics);
        var constraints = ReadSection(reader, contextDirectory, "constraints.md", diagnostics);
        var glossary = ReadSection(reader, contextDirectory, "glossary.md", diagnostics);
        if (contextText is null || readme is null || diagnostics.Any(d => d.IsError))
        {
            return new ContextLoadResult(null, diagnostics);
        }

        using var document = JsonDocument.Parse(contextText);
        var root = document.RootElement.Clone();

        var ruleLoad = RuleLoader.Load(contextDirectory, reader);
        if (ruleLoad.Diagnostics.Any(d => d.IsError))
        {
            diagnostics.AddRange(ruleLoad.Diagnostics.Where(d => d.IsError));
            return new ContextLoadResult(null, diagnostics);
        }

        var projectName = ReadProjectName(root);
        if (string.IsNullOrWhiteSpace(projectName))
        {
            projectName = new DirectoryInfo(basePath).Name;
        }

        var exportContext = new ExportContext(
            projectName,
            readme,
            architecture,
            decisions,
            patterns,
            constraints,
            glossary,
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

    // The trimmed text of an optional section; null when the file is absent or empty, or after an error diagnostic
    // was added because it could not be read.
    private static string? ReadSection(ContextFileReader reader, string contextDirectory, string fileName, List<Diagnostic> diagnostics)
    {
        var filePath = Path.Combine(contextDirectory, fileName);
        if (!File.Exists(filePath))
        {
            return null;
        }

        var content = reader.TryReadAllText(filePath, DiagnosticCodes.UnreadableContextFile, $".aicontext/{fileName}", diagnostics)?.Trim();
        return string.IsNullOrWhiteSpace(content) ? null : content;
    }
}
