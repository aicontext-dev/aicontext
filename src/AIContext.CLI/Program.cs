using System.Globalization;
using System.Text;
using System.Text.Json;

const string CanonicalSchemaUrl = "https://aicontext.dev/schema/v1/context.schema.json";
const string ExpectedSpecVersion = "0.1.0";
var allowedTargets = new HashSet<string>(StringComparer.Ordinal)
{
    "agents",
    "copilot",
    "claude",
    "cursor",
    "windsurf",
    "aider"
};

if (args.Length == 0 || IsHelp(args[0]))
{
    PrintHelp();
    return 0;
}

var command = args[0];
var options = ParseOptions(args.Skip(1).ToArray());
if (options.Error is not null)
{
    Console.Error.WriteLine(options.Error);
    PrintHelp();
    return 1;
}

return command switch
{
    "init" => RunInit(options.Values),
    "validate" => RunValidate(options.Values),
    "export" => RunExport(options.Values),
    _ => UnknownCommand(command)
};

static int UnknownCommand(string command)
{
    Console.Error.WriteLine($"Unknown command '{command}'.");
    PrintHelp();
    return 1;
}

int RunInit(Dictionary<string, string?> options)
{
    var basePath = GetBasePath(options);
    var force = IsTrue(options, "force");
    var projectName = GetOption(options, "name");
    if (string.IsNullOrWhiteSpace(projectName))
    {
        projectName = new DirectoryInfo(basePath).Name;
    }

    var contextDirectory = Path.Combine(basePath, ".aicontext");
    var readmePath = Path.Combine(contextDirectory, "README.md");
    var contextPath = Path.Combine(contextDirectory, "context.json");

    Directory.CreateDirectory(contextDirectory);

    if (!force)
    {
        if (File.Exists(readmePath))
        {
            Console.Error.WriteLine($"File already exists: {readmePath}. Use --force to overwrite.");
            return 1;
        }

        if (File.Exists(contextPath))
        {
            Console.Error.WriteLine($"File already exists: {contextPath}. Use --force to overwrite.");
            return 1;
        }
    }

    var readmeContent = $$"""
                        # Project: {{projectName}}
                        
                        Brief project summary.
                        
                        **Tech stack:** TODO  
                        **Architecture:** TODO  
                        **Testing:** TODO
                        """;

    var contextDocument = new Dictionary<string, object?>
    {
        ["$schema"] = CanonicalSchemaUrl,
        ["specVersion"] = ExpectedSpecVersion,
        ["project"] = new Dictionary<string, object?>
        {
            ["name"] = projectName
        }
    };
    var contextJson = JsonSerializer.Serialize(contextDocument, new JsonSerializerOptions
    {
        WriteIndented = true
    });

    File.WriteAllText(readmePath, readmeContent + Environment.NewLine, Encoding.UTF8);
    File.WriteAllText(contextPath, contextJson + Environment.NewLine, new UTF8Encoding(false));

    Console.WriteLine($"Created: {readmePath}");
    Console.WriteLine($"Created: {contextPath}");
    return 0;
}

int RunValidate(Dictionary<string, string?> options)
{
    var basePath = GetBasePath(options);
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
        ValidateContextJson(root, errors, warnings, allowedTargets);
    }

    var localSchemaPath = FindLocalSchema(basePath);
    if (localSchemaPath is null)
    {
        warnings.Add("Local schema file not found at schema/v1/context.schema.json in current directory or parent directories.");
    }
    else
    {
        Console.WriteLine($"Schema reference available: {localSchemaPath}");
    }

    foreach (var warning in warnings)
    {
        Console.WriteLine($"WARNING: {warning}");
    }

    if (errors.Count > 0)
    {
        foreach (var error in errors)
        {
            Console.Error.WriteLine($"ERROR: {error}");
        }

        Console.Error.WriteLine($"Validation failed with {errors.Count} error(s).");
        return 1;
    }

    Console.WriteLine("AIContext validation passed.");
    return 0;
}

int RunExport(Dictionary<string, string?> options)
{
    var basePath = GetBasePath(options);
    var target = GetOption(options, "target");
    if (string.IsNullOrWhiteSpace(target))
    {
        Console.Error.WriteLine("Missing required option: --target");
        return 1;
    }

    if (!string.Equals(target, "agents", StringComparison.Ordinal) &&
        !string.Equals(target, "copilot", StringComparison.Ordinal) &&
        !string.Equals(target, "all", StringComparison.Ordinal))
    {
        Console.Error.WriteLine($"Unsupported export target '{target}'. Supported targets: agents, copilot, all");
        return 1;
    }

    var requestedTargets = string.Equals(target, "all", StringComparison.Ordinal)
        ? new List<string> { "agents", "copilot", "claude", "cursor", "windsurf", "aider" }
        : new List<string> { target };

    var contextDirectory = Path.Combine(basePath, ".aicontext");
    var readmePath = Path.Combine(contextDirectory, "README.md");
    var contextPath = Path.Combine(contextDirectory, "context.json");

    if (!File.Exists(readmePath))
    {
        Console.Error.WriteLine($"Missing required file: {readmePath}");
        return 1;
    }

    if (!File.Exists(contextPath))
    {
        Console.Error.WriteLine($"Missing required file: {contextPath}");
        return 1;
    }

    JsonElement root;
    try
    {
        using var document = JsonDocument.Parse(File.ReadAllText(contextPath));
        root = document.RootElement.Clone();
    }
    catch (JsonException ex)
    {
        Console.Error.WriteLine($"Invalid JSON in {contextPath}. {ex.Message}");
        return 1;
    }

    if (!root.TryGetProperty("project", out var project) ||
        !TryGetString(project, "name", out var projectName) ||
        string.IsNullOrWhiteSpace(projectName))
    {
        projectName = new DirectoryInfo(basePath).Name;
    }

    var readme = NormalizeReadmeForAgents(File.ReadAllText(readmePath));
    var architecture = ReadOptionalContextSection(contextDirectory, "architecture.md");
    var decisions = ReadOptionalContextSection(contextDirectory, "decisions.md");
    var patterns = ReadOptionalContextSection(contextDirectory, "patterns.md");
    var constraints = ReadOptionalContextSection(contextDirectory, "constraints.md");
    var glossary = ReadOptionalContextSection(contextDirectory, "glossary.md");

    var exportContext = new ExportContext(
        projectName,
        readme,
        architecture,
        decisions,
        patterns,
        constraints,
        glossary);

    var generatedCount = 0;
    var notImplemented = new List<string>();
    var generatedAt = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    foreach (var requestedTarget in requestedTargets)
    {
        if (string.Equals(requestedTarget, "agents", StringComparison.Ordinal))
        {
            var outputPath = Path.Combine(basePath, "AGENTS.md");
            var content = BuildAgentsMarkdown(exportContext, generatedAt);
            File.WriteAllText(outputPath, content + Environment.NewLine, new UTF8Encoding(false));
            Console.WriteLine($"Generated ({requestedTarget}): {outputPath}");
            generatedCount++;
            continue;
        }

        if (string.Equals(requestedTarget, "copilot", StringComparison.Ordinal))
        {
            var copilotDirectory = Path.Combine(basePath, ".github");
            Directory.CreateDirectory(copilotDirectory);
            var outputPath = Path.Combine(copilotDirectory, "copilot-instructions.md");
            var content = BuildCopilotMarkdown(exportContext, generatedAt);
            File.WriteAllText(outputPath, content + Environment.NewLine, new UTF8Encoding(false));
            Console.WriteLine($"Generated ({requestedTarget}): {outputPath}");
            generatedCount++;
            continue;
        }

        notImplemented.Add(requestedTarget);
    }

    foreach (var pendingTarget in notImplemented)
    {
        Console.WriteLine($"WARNING: Export target '{pendingTarget}' is not implemented yet.");
    }

    if (generatedCount == 0)
    {
        Console.Error.WriteLine("No export files were generated.");
        return 1;
    }

    if (notImplemented.Count > 0)
    {
        Console.WriteLine($"Export completed partially ({generatedCount} generated, {notImplemented.Count} pending).");
    }

    return 0;
}

static void ValidateContextJson(
    JsonElement root,
    List<string> errors,
    List<string> warnings,
    HashSet<string> allowedTargets)
{
    if (!TryGetString(root, "$schema", out var schemaValue))
    {
        warnings.Add("context.json is missing '$schema'.");
    }
    else if (!string.Equals(schemaValue, CanonicalSchemaUrl, StringComparison.Ordinal))
    {
        warnings.Add($"$schema should be '{CanonicalSchemaUrl}'.");
    }

    if (!TryGetString(root, "specVersion", out var specVersion))
    {
        errors.Add("context.json is missing required property: specVersion");
    }
    else if (!string.Equals(specVersion, ExpectedSpecVersion, StringComparison.Ordinal))
    {
        errors.Add($"specVersion must be '{ExpectedSpecVersion}'.");
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

static void ValidateExports(JsonElement exports, List<string> errors, HashSet<string> allowedTargets)
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

static bool TryGetString(JsonElement element, string propertyName, out string value)
{
    if (element.TryGetProperty(propertyName, out var propertyValue) && propertyValue.ValueKind == JsonValueKind.String)
    {
        value = propertyValue.GetString() ?? string.Empty;
        return true;
    }

    value = string.Empty;
    return false;
}

static string? FindLocalSchema(string startPath)
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

static string GetBasePath(Dictionary<string, string?> options)
{
    var path = GetOption(options, "path");
    if (string.IsNullOrWhiteSpace(path))
    {
        return Environment.CurrentDirectory;
    }

    return Path.GetFullPath(path);
}

static bool IsTrue(Dictionary<string, string?> options, string key)
{
    var value = GetOption(options, key);
    if (value is null)
    {
        return false;
    }

    if (string.IsNullOrWhiteSpace(value))
    {
        return true;
    }

    return value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("1", StringComparison.OrdinalIgnoreCase);
}

static string? GetOption(Dictionary<string, string?> options, string key)
{
    return options.TryGetValue(key, out var value) ? value : null;
}

static string? ReadOptionalContextSection(string contextDirectory, string fileName)
{
    var filePath = Path.Combine(contextDirectory, fileName);
    if (!File.Exists(filePath))
    {
        return null;
    }

    var content = File.ReadAllText(filePath).Trim();
    return string.IsNullOrWhiteSpace(content) ? null : content;
}

static string BuildAgentsMarkdown(ExportContext exportContext, string generatedAt)
{
    var builder = new StringBuilder();
    builder.AppendLine("<!-- Auto-generated by aicontext export --target agents -->");
    builder.AppendLine($"<!-- Source: .aicontext/ | Generated: {generatedAt} | Do not edit directly -->");
    builder.AppendLine();
    builder.AppendLine($"# {exportContext.ProjectName}");
    builder.AppendLine();
    builder.AppendLine(exportContext.ReadmeContent);

    AppendSection(builder, "Architecture", exportContext.Architecture);
    AppendSection(builder, "Key Decisions", exportContext.Decisions);
    AppendSection(builder, "Coding Patterns", exportContext.Patterns);
    AppendSection(builder, "Constraints", exportContext.Constraints);
    AppendSection(builder, "Glossary", exportContext.Glossary);

    return builder.ToString().TrimEnd();
}

static string BuildCopilotMarkdown(ExportContext exportContext, string generatedAt)
{
    var builder = new StringBuilder();
    builder.AppendLine("<!-- Auto-generated by aicontext export --target copilot -->");
    builder.AppendLine($"<!-- Source: .aicontext/ | Generated: {generatedAt} | Do not edit directly -->");
    builder.AppendLine();
    builder.AppendLine($"# Copilot Instructions for {exportContext.ProjectName}");
    builder.AppendLine();
    builder.AppendLine(exportContext.ReadmeContent);

    AppendSection(builder, "Architecture", exportContext.Architecture);
    AppendSection(builder, "Key Decisions", exportContext.Decisions);
    AppendSection(builder, "Coding Patterns", exportContext.Patterns);
    AppendSection(builder, "Constraints", exportContext.Constraints);
    AppendSection(builder, "Glossary", exportContext.Glossary);

    return builder.ToString().TrimEnd();
}

static string NormalizeReadmeForAgents(string readmeContent)
{
    var lines = readmeContent
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Split('\n')
        .ToList();

    if (lines.Count > 0 && lines[0].StartsWith("# ", StringComparison.Ordinal))
    {
        lines.RemoveAt(0);
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0]))
        {
            lines.RemoveAt(0);
        }
    }

    return string.Join(Environment.NewLine, lines).Trim();
}

static void AppendSection(StringBuilder builder, string title, string? content)
{
    if (string.IsNullOrWhiteSpace(content))
    {
        return;
    }

    builder.AppendLine();
    builder.AppendLine($"## {title}");
    builder.AppendLine(content);
}

static (Dictionary<string, string?> Values, string? Error) ParseOptions(string[] args)
{
    var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Length; i++)
    {
        var arg = args[i];
        if (!arg.StartsWith("--", StringComparison.Ordinal))
        {
            return (values, $"Unknown argument '{arg}'. Only --key [value] is supported.");
        }

        var key = arg[2..];
        if (string.IsNullOrWhiteSpace(key))
        {
            return (values, "Invalid option name.");
        }

        string? value = null;
        if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
        {
            value = args[++i];
        }

        values[key] = value;
    }

    return (values, null);
}

static bool IsHelp(string arg)
{
    return arg is "-h" or "--help" or "help";
}

static void PrintHelp()
{
    Console.WriteLine("AIContext CLI");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  aicontext init [--path <directory>] [--name <project-name>] [--force]");
    Console.WriteLine("  aicontext validate [--path <directory>]");
    Console.WriteLine("  aicontext export --target <agents|copilot|all> [--path <directory>]");
}

sealed record ExportContext(
    string ProjectName,
    string ReadmeContent,
    string? Architecture,
    string? Decisions,
    string? Patterns,
    string? Constraints,
    string? Glossary);
