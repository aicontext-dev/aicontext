# AIContext

Core library of the [AIContext](https://github.com/aicontext-dev/aicontext) open format standard for AI coding assistant project context.

It contains the logic behind the `aicontext` CLI so it can be reused in other tools:

- `ContextValidator` – validates `.aicontext/` (embedded JSON Schema, recommended files, secret lint)
- `ContextLoader` – reads `.aicontext/` into a model
- `ContextExporter`, `ExportTargets`, `ExportWriter` – deterministic exports for AGENTS.md, Copilot, Claude, Cursor, Windsurf, Devin and Aider, with `single` and `split` layouts and safe overwrite
- `ExportStatus` – context size per tool and export sync state

Safe export in a few lines (this is what the CLI does; nothing is written until `Apply`):

```csharp
var plan = ExportService.Plan(new ExportRequest(projectPath, Target: "all"));
if (plan.HasErrors)
{
    foreach (var d in plan.Diagnostics.Where(d => d.IsError))
        Console.Error.WriteLine($"{d.Code} {d.Location}: {d.Message}");
    return;
}

ExportService.Apply(plan);
```

`ExportService.Plan` loads and validates the project, plans every file of every target and reports overwrite conflicts and paths that leave the project as diagnostics; `Apply` refuses a plan with errors; `Status` reports context size and sync state. Every problem is a `Diagnostic` with a stable code (`AIC1001`, ...), a message and a location; the codes are listed in the user manual.

For the command-line tool install `AIContext.CLI` (`dotnet tool install --global AIContext.CLI`).
See https://github.com/aicontext-dev/aicontext for the specification and documentation.
