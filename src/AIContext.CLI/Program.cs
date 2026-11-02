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
    "status" => RunStatus(options.Values),
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

    var load = ContextLoader.Load(basePath);
    if (load.Loaded is null)
    {
        foreach (var loadError in load.Errors)
        {
            Console.Error.WriteLine(loadError.StartsWith("rules/", StringComparison.Ordinal) ? $"ERROR: {loadError}" : loadError);
        }

        if (load.Errors.Any(e => e.StartsWith("rules/", StringComparison.Ordinal)))
        {
            Console.Error.WriteLine("Fix the rule files above (run 'aicontext validate' for details). No files were written.");
        }

        return 1;
    }

    var root = load.Loaded.Root;
    var exportContext = load.Loaded.Context;

    var targetResolution = ResolveTargets(root, target, out var targetError);
    if (targetResolution is null)
    {
        Console.Error.WriteLine(targetError);
        Console.Error.WriteLine("Run 'aicontext validate' to see full details.");
        return 1;
    }

    var requestedTargets = targetResolution;

    var layoutResolution = ExportLayoutResolver.Resolve(root, GetOption(options, "layout"));
    if (layoutResolution.Error is not null)
    {
        Console.Error.WriteLine(layoutResolution.Error);
        return 1;
    }

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

List<string>? ResolveTargets(JsonElement root, string target, out string? error)
{
    error = null;
    if (!string.Equals(target, "all", StringComparison.Ordinal))
    {
        return new List<string> { target };
    }

    var resolution = ExportTargetResolver.ResolveTargetsForAll(root, ExportTargets.DefaultNames, allowedTargets);
    if (resolution.Error is not null)
    {
        error = $"Invalid exports.targets configuration in .aicontext/context.json: {resolution.Error}";
        return null;
    }

    return resolution.Targets;
}

int RunStatus(Dictionary<string, string?> options)
{
    var basePath = GetBasePath(options);
    var load = ContextLoader.Load(basePath);
    if (load.Loaded is null)
    {
        foreach (var loadError in load.Errors)
        {
            Console.Error.WriteLine($"ERROR: {loadError}");
        }

        return 1;
    }

    var targets = ResolveTargets(load.Loaded.Root, "all", out var targetError);
    if (targets is null)
    {
        Console.Error.WriteLine(targetError);
        return 1;
    }

    var layoutResolution = ExportLayoutResolver.Resolve(load.Loaded.Root, GetOption(options, "layout"));
    if (layoutResolution.Error is not null)
    {
        Console.Error.WriteLine(layoutResolution.Error);
        return 1;
    }

    var layout = layoutResolution.Layout;
    var statuses = ExportStatus.Compute(load.Loaded, targets, layout);

    Console.WriteLine($"Project: {load.Loaded.Context.ProjectName}");
    Console.WriteLine($"Layout:  {layout.ToString().ToLowerInvariant()}");

    var rules = load.Loaded.Context.Rules ?? [];
    if (rules.Count > 0)
    {
        Console.WriteLine($"Rules:   {rules.Count}");
        foreach (var rule in rules)
        {
            var scope = rule.Kind switch
            {
                RuleKind.Scoped => $"scoped    {string.Join(", ", rule.Paths)}",
                RuleKind.OnDemand => $"on-demand {rule.Description}",
                _ => "always on"
            };
            Console.WriteLine($"  {rule.Name,-16} {scope}");
        }
    }

    Console.WriteLine();
    Console.WriteLine($"{"Target",-10}{"Always on",-22}{"Scoped / on-demand",-22}{"Files",-7}State");
    foreach (var status in statuses)
    {
        var onDemand = status.OnDemandChars == 0 ? "-" : FormatSize(status.OnDemandChars);
        Console.WriteLine(
            $"{status.Target,-10}{FormatSize(status.AlwaysOnChars),-22}{onDemand,-22}{status.FileCount,-7}{DescribeState(status.State)}");
    }

    Console.WriteLine();
    Console.WriteLine("Always on = loaded in every request. Scoped / on-demand = loaded only for matching files or when relevant.");
    Console.WriteLine($"Token counts are estimates (~{ExportStatus.CharsPerToken} characters per token).");

    if (layout == ExportLayout.Single)
    {
        var savings = statuses
            .Where(s => s.AlwaysOnCharsInOtherLayout < s.AlwaysOnChars)
            .Select(s => $"{s.Target} {FormatSize(s.AlwaysOnChars)} -> {FormatSize(s.AlwaysOnCharsInOtherLayout)}")
            .ToList();

        if (savings.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Tip: the 'split' layout (exports.layout) would reduce always-on context:");
            foreach (var saving in savings)
            {
                Console.WriteLine($"  {saving}");
            }
        }
    }

    return 0;
}

static string FormatSize(int chars) =>
    $"{(chars / 1024.0).ToString("0.0", CultureInfo.InvariantCulture),4} KB (~{ExportStatus.EstimateTokens(chars).ToString("N0", CultureInfo.InvariantCulture)} tok)";

static string DescribeState(SyncState state) => state switch
{
    SyncState.InSync => "in sync",
    SyncState.OutOfDate => "out of date (run export)",
    SyncState.NotExported => "not exported",
    _ => "conflict (existing file not generated by aicontext)"
};

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
    Console.WriteLine("  aicontext status [--path <directory>] [--layout <single|split>]");
    Console.WriteLine();
    Console.WriteLine("Status:");
    Console.WriteLine("  Shows how much context each tool loads (always on vs. scoped/on-demand) and whether exports are in sync.");
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
