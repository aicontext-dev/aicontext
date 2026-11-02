using System.Globalization;
using System.Text;
using System.Text.Json;
using AIContext;

const string CanonicalSchemaUrl = "https://aicontext.dev/schema/v1/context.schema.json";
const string ExpectedSpecVersion = "0.1.0";
string[] supportedTargets = ExportTargets.Names.ToArray();
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
    var strict = IsTrue(options, "strict");
    var validationResult = ContextValidator.ValidateDirectory(basePath, CanonicalSchemaUrl);

    foreach (var warning in validationResult.Warnings)
    {
        Console.WriteLine($"WARNING: {warning}");
    }

    var failureCount = validationResult.Errors.Count + (strict ? validationResult.Warnings.Count : 0);
    if (validationResult.Errors.Count > 0)
    {
        foreach (var error in validationResult.Errors)
        {
            Console.Error.WriteLine($"ERROR: {error}");
        }
    }

    if (failureCount > 0)
    {
        var detail = strict && validationResult.Warnings.Count > 0
            ? $"{validationResult.Errors.Count} error(s) and {validationResult.Warnings.Count} warning(s) treated as errors (--strict)"
            : $"{validationResult.Errors.Count} error(s)";
        Console.Error.WriteLine($"Validation failed with {detail}.");
        return 1;
    }

    var profile = validationResult.ExtendedProfile ? "Extended" : "Core";
    Console.WriteLine($"AIContext validation passed ({profile} profile).");
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
        var targetResolution = ExportTargetResolver.ResolveTargetsForAll(root, ExportTargets.DefaultNames, allowedTargets);
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

    var ruleLoad = RuleLoader.Load(contextDirectory);
    if (ruleLoad.Errors.Count > 0)
    {
        foreach (var ruleError in ruleLoad.Errors)
        {
            Console.Error.WriteLine($"ERROR: {ruleError}");
        }

        Console.Error.WriteLine("Fix the rule files above (run 'aicontext validate' for details). No files were written.");
        return 1;
    }

    var layoutResolution = ExportLayoutResolver.Resolve(root, GetOption(options, "layout"));
    if (layoutResolution.Error is not null)
    {
        Console.Error.WriteLine(layoutResolution.Error);
        return 1;
    }

    var exportContext = new AIContext.ExportContext(
        projectName,
        readme,
        architecture,
        decisions,
        patterns,
        constraints,
        glossary,
        ruleLoad.Rules);

    var force = IsTrue(options, "force");
    var check = IsTrue(options, "check");
    var preview = check || IsTrue(options, "dry-run");

    var plans = requestedTargets
        .SelectMany(name => ExportWriter.PlanTarget(ExportTargets.Find(name)!, basePath, exportContext, layoutResolution.Layout, force))
        .ToList();

    // Nothing is written when any target would overwrite a file that was not generated by aicontext.
    var conflicts = plans.Where(p => p.Status == ExportFileStatus.Conflict).ToList();

    foreach (var plan in plans)
    {
        Console.WriteLine($"{DescribeStatus(plan.Status, preview || conflicts.Count > 0)} ({plan.Target}): {plan.Path}");
    }

    if (conflicts.Count > 0)
    {
        foreach (var conflict in conflicts)
        {
            Console.Error.WriteLine($"ERROR: {conflict.Path} exists and was not generated by aicontext. Use --force to overwrite.");
        }

        Console.Error.WriteLine("No files were written.");
        return 1;
    }

    var changed = plans.Count(p => p.Status is ExportFileStatus.Created or ExportFileStatus.Updated or ExportFileStatus.Deleted);

    if (check)
    {
        if (changed > 0)
        {
            Console.Error.WriteLine($"Export is out of date ({changed} file(s)). Run 'aicontext export' to update.");
            return 1;
        }

        Console.WriteLine("Export is up to date.");
        return 0;
    }

    if (preview)
    {
        Console.WriteLine($"Dry run: {changed} file(s) would change, nothing was written.");
        return 0;
    }

    foreach (var plan in plans)
    {
        ExportWriter.Apply(plan);
    }

    Console.WriteLine($"Export completed ({changed} changed, {plans.Count - changed} unchanged).");
    return 0;
}

static string DescribeStatus(ExportFileStatus status, bool preview) => status switch
{
    ExportFileStatus.Created => preview ? "Would create" : "Created",
    ExportFileStatus.Updated => preview ? "Would update" : "Updated",
    ExportFileStatus.Deleted => preview ? "Would remove" : "Removed",
    ExportFileStatus.Unchanged => "Unchanged",
    _ => "Conflict"
};

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
    if (!options.TryGetValue(key, out var value))
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
    Console.WriteLine("  aicontext validate [--path <directory>] [--strict]");
    Console.WriteLine("  aicontext export --target <agents|copilot|claude|cursor|windsurf|devin|aider|all> [--path <directory>] [--layout <single|split>] [--force] [--dry-run] [--check]");
    Console.WriteLine();
    Console.WriteLine("Validate options:");
    Console.WriteLine("  --strict    Treat warnings (missing recommended files, possible secrets) as errors.");
    Console.WriteLine();
    Console.WriteLine("Export options:");
    Console.WriteLine("  --layout    single (default): one file per tool. split: small always-on file plus separate scoped/on-demand rule files.");
    Console.WriteLine("  --force     Overwrite existing files that were not generated by aicontext.");
    Console.WriteLine("  --dry-run   Show what would be written without changing any file.");
    Console.WriteLine("  --check     Like --dry-run, but exit with code 1 if any file is missing or out of date (for CI).");
}
