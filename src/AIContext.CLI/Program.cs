using System.Globalization;
using System.Text;
using System.Text.Json;
using AIContext;

const string CanonicalSchemaUrl = "https://aicontext.dev/schema/v1/context.schema.json";
const string ExpectedSpecVersion = "0.1.0";
string[] supportedTargets =
{
    "agents",
    "copilot",
    "claude",
    "cursor",
    "windsurf",
    "aider"
};
var allowedTargets = new HashSet<string>(supportedTargets, StringComparer.Ordinal);

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
    var validationResult = ContextValidator.ValidateDirectory(
        basePath,
        CanonicalSchemaUrl,
        ExpectedSpecVersion,
        allowedTargets);

    if (validationResult.LocalSchemaPath is not null)
    {
        Console.WriteLine($"Schema reference available: {validationResult.LocalSchemaPath}");
    }

    foreach (var warning in validationResult.Warnings)
    {
        Console.WriteLine($"WARNING: {warning}");
    }

    if (validationResult.Errors.Count > 0)
    {
        foreach (var error in validationResult.Errors)
        {
            Console.Error.WriteLine($"ERROR: {error}");
        }

        Console.Error.WriteLine($"Validation failed with {validationResult.Errors.Count} error(s).");
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

    if (!string.Equals(target, "all", StringComparison.Ordinal) &&
        !allowedTargets.Contains(target))
    {
        Console.Error.WriteLine($"Unsupported export target '{target}'. Supported targets: {string.Join(", ", supportedTargets)}, all");
        return 1;
    }

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

    List<string> requestedTargets;
    if (string.Equals(target, "all", StringComparison.Ordinal))
    {
        var targetResolution = ExportTargetResolver.ResolveTargetsForAll(root, supportedTargets, allowedTargets);
        if (targetResolution.Error is not null)
        {
            Console.Error.WriteLine($"Invalid exports.targets configuration in {contextPath}: {targetResolution.Error}");
            Console.Error.WriteLine("Run 'aicontext validate' to see full details.");
            return 1;
        }

        requestedTargets = targetResolution.Targets;
    }
    else
    {
        requestedTargets = new List<string> { target };
    }

    if (!root.TryGetProperty("project", out var project) ||
        !TryGetString(project, "name", out var projectName) ||
        string.IsNullOrWhiteSpace(projectName))
    {
        projectName = new DirectoryInfo(basePath).Name;
    }

    var readme = File.ReadAllText(readmePath);
    var architecture = ReadOptionalContextSection(contextDirectory, "architecture.md");
    var decisions = ReadOptionalContextSection(contextDirectory, "decisions.md");
    var patterns = ReadOptionalContextSection(contextDirectory, "patterns.md");
    var constraints = ReadOptionalContextSection(contextDirectory, "constraints.md");
    var glossary = ReadOptionalContextSection(contextDirectory, "glossary.md");

    var exportContext = new AIContext.ExportContext(
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
            var content = ContextExporter.BuildAgents(exportContext, generatedAt);
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
            var content = ContextExporter.BuildCopilot(exportContext, generatedAt);
            File.WriteAllText(outputPath, content + Environment.NewLine, new UTF8Encoding(false));
            Console.WriteLine($"Generated ({requestedTarget}): {outputPath}");
            generatedCount++;
            continue;
        }

        if (string.Equals(requestedTarget, "claude", StringComparison.Ordinal))
        {
            var outputPath = Path.Combine(basePath, "CLAUDE.md");
            var content = ContextExporter.BuildClaude(exportContext, generatedAt);
            File.WriteAllText(outputPath, content + Environment.NewLine, new UTF8Encoding(false));
            Console.WriteLine($"Generated ({requestedTarget}): {outputPath}");
            generatedCount++;
            continue;
        }

        if (string.Equals(requestedTarget, "cursor", StringComparison.Ordinal))
        {
            var cursorRulesDirectory = Path.Combine(basePath, ".cursor", "rules");
            Directory.CreateDirectory(cursorRulesDirectory);
            var outputPath = Path.Combine(cursorRulesDirectory, "project.mdc");
            var content = ContextExporter.BuildCursor(exportContext, generatedAt);
            File.WriteAllText(outputPath, content + Environment.NewLine, new UTF8Encoding(false));
            Console.WriteLine($"Generated ({requestedTarget}): {outputPath}");
            generatedCount++;
            continue;
        }

        if (string.Equals(requestedTarget, "windsurf", StringComparison.Ordinal))
        {
            var windsurfRulesDirectory = Path.Combine(basePath, ".windsurf", "rules");
            Directory.CreateDirectory(windsurfRulesDirectory);
            var outputPath = Path.Combine(windsurfRulesDirectory, "project.md");
            var content = ContextExporter.BuildWindsurf(exportContext, generatedAt);
            File.WriteAllText(outputPath, content + Environment.NewLine, new UTF8Encoding(false));
            Console.WriteLine($"Generated ({requestedTarget}): {outputPath}");
            generatedCount++;
            continue;
        }

        if (string.Equals(requestedTarget, "aider", StringComparison.Ordinal))
        {
            var outputPath = Path.Combine(basePath, "CONVENTIONS.md");
            var content = ContextExporter.BuildAider(exportContext, generatedAt);
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
    Console.WriteLine("  aicontext export --target <agents|copilot|claude|cursor|windsurf|aider|all> [--path <directory>]");
}
