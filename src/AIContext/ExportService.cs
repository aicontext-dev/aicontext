using System.Text.Json;

namespace AIContext;

// What to export. Target: null uses exports.defaultTarget, "all" the targets from exports.targets (or every
// default target). Layout: null uses exports.layout. Force allows overwriting files aicontext did not generate.
public sealed record ExportRequest(string BasePath, string? Target = null, string? Layout = null, bool Force = false);

// The result of planning an export: nothing has been written yet. When HasErrors is true the export must not be
// applied; Diagnostics then explain why (invalid context, unsupported target, overwrite conflicts, links that
// leave the project, ...). Warnings of the context are included but never block an export.
public sealed record ExportPlan(
    string BasePath,
    IReadOnlyList<string> Targets,
    // Set when the target came from exports.defaultTarget instead of the request.
    string? DefaultTargetUsed,
    ExportLayout Layout,
    IReadOnlyList<ExportFilePlan> Files,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(d => d.IsError);

    // Files that would be created, updated or removed.
    public int ChangedCount => Files.Count(f => f.Status is ExportFileStatus.Created or ExportFileStatus.Updated or ExportFileStatus.Deleted);
}

// Changed counts the files this call really wrote or removed (Written lists them in order) and Unchanged the files
// that already had the planned content. When applying stopped early, Diagnostics says why, Written holds what was
// completed before the failure, and the rest of the plan was not touched: an export is a sequence of single-file
// writes, not a transaction, and planning again from the files on disk finishes the job.
public sealed record ExportResult(int Changed, int Unchanged)
{
    public IReadOnlyList<ExportFilePlan> Written { get; init; } = [];

    // Set when applying stopped because the plan was out of date, unsafe or a write failed.
    public IReadOnlyList<Diagnostic> Diagnostics { get; init; } = [];

    public bool HasErrors => Diagnostics.Any(d => d.IsError);
}

public sealed record StatusReport(
    LoadedContext? Context,
    ExportLayout Layout,
    bool NestedAgents,
    IReadOnlyList<TargetStatus> Targets,
    // Only set for the split layout with nestedAgents when the agents target is selected.
    NestedAgentsPlan? NestedAgentsPlan,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(d => d.IsError);

    // Tools that load several of the selected targets' files together (an estimate, see ToolOverlaps).
    public IReadOnlyList<ToolOverlap> Overlaps => ToolOverlaps.Compute(Targets);
}

// The safe way to export: load and validate the project, plan every file of every target, report conflicts and
// problems, and only then write. Front ends (the CLI, editor extensions) call this instead of assembling the steps.
public static class ExportService
{
    private static readonly HashSet<string> AllowedTargets = new(ExportTargets.Names, StringComparer.Ordinal);

    private static string SupportedTargets => string.Join(", ", ExportTargets.Names);

    public static ExportPlan Plan(ExportRequest request)
    {
        var basePath = request.BasePath;
        var target = request.Target;

        if (!string.IsNullOrWhiteSpace(target) &&
            !string.Equals(target, "all", StringComparison.Ordinal) &&
            !AllowedTargets.Contains(target))
        {
            return Failed(basePath, null, Diagnostic.Error(
                DiagnosticCodes.UnsupportedTarget,
                $"Unsupported export target '{target}'. Supported targets: {SupportedTargets}, all"));
        }

        var load = ContextLoader.Load(basePath);
        if (load.Loaded is null)
        {
            return Failed(basePath, null, load.Diagnostics.ToArray());
        }

        var diagnostics = new List<Diagnostic>(load.Diagnostics);
        var root = load.Loaded.Root;

        string? defaultTargetUsed = null;
        if (string.IsNullOrWhiteSpace(target))
        {
            target = ExportTargetResolver.ResolveDefaultTarget(root);
            if (target is null)
            {
                diagnostics.Add(Diagnostic.Error(
                    DiagnosticCodes.MissingTarget,
                    $"No export target given: pass a target or set exports.defaultTarget in .aicontext/context.json. Supported targets: {SupportedTargets}, all"));
                return Failed(basePath, null, diagnostics.ToArray());
            }

            if (!string.Equals(target, "all", StringComparison.Ordinal) && !AllowedTargets.Contains(target))
            {
                diagnostics.Add(Diagnostic.Error(
                    DiagnosticCodes.UnsupportedDefaultTarget,
                    $"Unsupported exports.defaultTarget '{target}' in .aicontext/context.json. Supported targets: {SupportedTargets}, all"));
                return Failed(basePath, null, diagnostics.ToArray());
            }

            defaultTargetUsed = target;
        }

        var targets = ResolveTargets(root, target, out var targetError);
        if (targets is null)
        {
            diagnostics.Add(Diagnostic.Error(DiagnosticCodes.InvalidTargetsConfiguration, targetError!));
            return Failed(basePath, defaultTargetUsed, diagnostics.ToArray());
        }

        var layoutResolution = ExportLayoutResolver.Resolve(root, request.Layout);
        if (layoutResolution.Error is not null)
        {
            diagnostics.Add(Diagnostic.Error(DiagnosticCodes.UnsupportedLayout, layoutResolution.Error));
            return Failed(basePath, defaultTargetUsed, diagnostics.ToArray());
        }

        var renderOptions = new RenderOptions(basePath, ExportLayoutResolver.ResolveNestedAgents(root));
        var files = targets
            .SelectMany(name => ExportWriter.PlanTarget(
                ExportTargets.Find(name)!, basePath, load.Loaded.Context, layoutResolution.Layout, request.Force, renderOptions))
            .ToList();

        // Nothing is written when any file would overwrite one that was not generated by aicontext
        // or would be written outside the project, or into a separate project, through a link.
        foreach (var file in files)
        {
            var location = Path.GetRelativePath(basePath, file.Path).Replace('\\', '/');
            if (file.Status == ExportFileStatus.Conflict)
            {
                diagnostics.Add(Diagnostic.Error(
                    DiagnosticCodes.OverwriteConflict,
                    $"{file.Path} exists and was not generated by aicontext. Use --force to overwrite.",
                    location));
            }
            else if (file.Status == ExportFileStatus.Blocked)
            {
                diagnostics.Add(ExportWriter.BlockedDiagnostic(file.Path, file.Reason, file.ReasonCode, location));
            }
        }

        // Targets that are not exported any more may still have generated files on disk; that is only a warning.
        // Only a run for the whole configuration knows which targets are meant to be gone.
        if (string.Equals(target, "all", StringComparison.Ordinal))
        {
            diagnostics.AddRange(OrphanedTargetExports.Find(basePath, targets));
        }

        return new ExportPlan(basePath, targets, defaultTargetUsed, layoutResolution.Layout, files, diagnostics);
    }

    // Writes a plan that has no errors; a plan with errors is refused, so nothing is ever written partially
    // because of a problem the plan already knew about. A plan can go stale (a file or a link changed since
    // planning, which matters when a person reviews it for a while): every file is verified again first, and if
    // anything differs, cannot be written or has a directory in its way, nothing is written and the result carries
    // the errors.
    //
    // After that the files are written one by one, and each write is safe on its own (a new file is created
    // exclusively, an existing one is replaced through a temporary file). The whole is not a transaction: if a write
    // fails (locked, no permission, no space), writing stops, the failure is a diagnostic, and the result lists what
    // was already written (Written); those files stay. Nothing is rolled back and the later files are untouched.
    // Planning again from the files on disk and applying finishes the job. An unexpected exception, which is a bug,
    // is not caught.
    public static ExportResult Apply(ExportPlan plan)
    {
        if (plan.HasErrors)
        {
            throw new InvalidOperationException("The export plan has errors; nothing is written.");
        }

        var unchanged = plan.Files.Count(file => file.Status == ExportFileStatus.Unchanged);
        var guard = new ExportPathGuard(plan.BasePath);
        var problems = plan.Files
            .Select(file => ExportWriter.Verify(file, guard, plan.BasePath))
            .OfType<Diagnostic>()
            .ToList();
        if (problems.Count > 0)
        {
            return new ExportResult(0, unchanged) { Diagnostics = problems };
        }

        var written = new List<ExportFilePlan>();
        // An empty result keeps the default (shared) empty list, so results without writes compare equal.
        IReadOnlyList<ExportFilePlan> Done() => written.Count == 0 ? [] : written.ToArray();
        ExportResult Stopped(Diagnostic problem) =>
            new(written.Count, unchanged) { Written = Done(), Diagnostics = [problem] };

        foreach (var file in plan.Files)
        {
            try
            {
                ExportWriter.Apply(file);
            }
            catch (StaleExportPlanException ex)
            {
                var location = Path.GetRelativePath(plan.BasePath, ex.FilePath).Replace('\\', '/');
                return Stopped(ExportWriter.StalePlan(ex.FilePath, location, ex.Reason));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Stopped(ExportWriter.WriteFailed(file, ex, plan.BasePath));
            }

            if (file.Status is ExportFileStatus.Created or ExportFileStatus.Updated or ExportFileStatus.Deleted)
            {
                written.Add(file);
            }
        }

        return new ExportResult(written.Count, unchanged) { Written = Done() };
    }

    // How much context each tool would load and whether the exports are in sync, for every configured target.
    public static StatusReport Status(string basePath, string? layoutOverride = null)
    {
        var load = ContextLoader.Load(basePath);
        if (load.Loaded is null)
        {
            return new StatusReport(null, ExportLayout.Single, false, [], null, load.Diagnostics);
        }

        var diagnostics = new List<Diagnostic>(load.Diagnostics);
        var root = load.Loaded.Root;

        var targets = ResolveTargets(root, "all", out var targetError);
        if (targets is null)
        {
            diagnostics.Add(Diagnostic.Error(DiagnosticCodes.InvalidTargetsConfiguration, targetError!));
            return new StatusReport(null, ExportLayout.Single, false, [], null, diagnostics);
        }

        var layoutResolution = ExportLayoutResolver.Resolve(root, layoutOverride);
        if (layoutResolution.Error is not null)
        {
            diagnostics.Add(Diagnostic.Error(DiagnosticCodes.UnsupportedLayout, layoutResolution.Error));
            return new StatusReport(null, ExportLayout.Single, false, [], null, diagnostics);
        }

        var layout = layoutResolution.Layout;
        var nestedAgents = ExportLayoutResolver.ResolveNestedAgents(root);
        diagnostics.AddRange(OrphanedTargetExports.Find(basePath, targets));
        var statuses = ExportStatus.Compute(load.Loaded, targets, layout, new RenderOptions(basePath, nestedAgents));
        var nestedPlan = nestedAgents && layout == ExportLayout.Split && targets.Contains("agents")
            ? NestedAgentsPlanner.Plan(load.Loaded.Context.Rules ?? [], basePath)
            : null;

        return new StatusReport(load.Loaded, layout, nestedAgents, statuses, nestedPlan, diagnostics);
    }

    private static List<string>? ResolveTargets(JsonElement root, string target, out string? error)
    {
        error = null;
        if (!string.Equals(target, "all", StringComparison.Ordinal))
        {
            return [target];
        }

        var resolution = ExportTargetResolver.ResolveTargetsForAll(root, ExportTargets.DefaultNames, AllowedTargets);
        if (resolution.Error is not null)
        {
            error = $"Invalid exports.targets configuration in .aicontext/context.json: {resolution.Error}";
            return null;
        }

        return resolution.Targets;
    }

    private static ExportPlan Failed(string basePath, string? defaultTargetUsed, params Diagnostic[] diagnostics) =>
        new(basePath, [], defaultTargetUsed, ExportLayout.Single, [], diagnostics);
}
