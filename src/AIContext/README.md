# AIContext

Core library of the [AIContext](https://github.com/aicontext-dev/aicontext) open format standard for AI coding assistant project context.

It contains the logic behind the `aicontext` CLI so it can be reused in other tools:

- `ContextValidator` – validates `.aicontext/` (embedded JSON Schema, recommended files, secret lint)
- `ContextLoader` – reads `.aicontext/` into a model
- `ContextExporter`, `ExportTargets`, `ExportWriter` – deterministic exports for AGENTS.md, Copilot, Claude, Cursor, Windsurf, Devin and Aider, with `single` and `split` layouts and safe overwrite
- `ExportStatus` – context size per tool and export sync state

Safe export in a few lines (this is what the CLI does; nothing is written until `Apply`). The body of a console program, with the default implicit usings, where `projectPath` is the directory that contains `.aicontext/`:

```csharp
using AIContext;

var plan = ExportService.Plan(new ExportRequest(projectPath, Target: "all"));
if (plan.HasErrors)
{
    // Refused before anything was written: an invalid context, an overwrite conflict, a path that leaves the
    // project, a destination that is blocked on disk.
    foreach (var d in plan.Diagnostics.Where(d => d.IsError))
        Console.Error.WriteLine($"{d.Code} {d.Location}: {d.Message}");
    return 1;
}

var result = ExportService.Apply(plan);
foreach (var file in result.Written)
    Console.WriteLine($"{file.Status}: {file.Path}");

if (result.HasErrors)
{
    // Apply checks the plan against the disk again before it writes: a plan that went stale (AIC4103), a path that
    // now leaves the project (AIC4102, AIC4104) or a file that cannot be written (AIC4105, AIC4106) writes nothing.
    // A write that fails midway stops there: result.Written is what was done and stays on disk, nothing is rolled
    // back, and planning again continues from the files as they are.
    foreach (var d in result.Diagnostics.Where(d => d.IsError))
        Console.Error.WriteLine($"{d.Code} {d.Location}: {d.Message}");
    Console.Error.WriteLine($"{result.Written.Count} of {plan.ChangedCount} planned change(s) were written.");
    return 2;
}

Console.WriteLine($"{result.Changed} changed, {result.Unchanged} unchanged.");
return 0;
```

The two calls report problems in two places. `plan.Diagnostics` holds the problems found before any write (and also warnings, so filter on `IsError`); `Apply` throws `InvalidOperationException` for a plan with errors, so check `plan.HasErrors` first. `result.Diagnostics` holds the errors of `Apply`, and `result.Written` lists exactly the files that were created, updated or removed, so a result with errors can still describe changes that were made. An export is a sequence of single-file writes, not a transaction.

`ExportService.Plan` loads and validates the project, plans every file of every target and reports overwrite conflicts and paths that leave the project as diagnostics; `Apply` refuses a plan with errors and writes a plan without errors file by file; `Status` reports context size and sync state. Every problem is a `Diagnostic` with a stable code (`AIC1001`, ...), a message and a location; the codes are listed in the user manual.

For the command-line tool install `AIContext.CLI` (`dotnet tool install --global AIContext.CLI`).
See https://github.com/aicontext-dev/aicontext for the specification and documentation.
