# AIContext

Core library of the [AIContext](https://github.com/aicontext-dev/aicontext) open format standard for AI coding assistant project context.

It contains the logic behind the `aicontext` CLI so it can be reused in other tools:

- `ContextValidator` – validates `.aicontext/` (embedded JSON Schema, recommended files, secret lint)
- `ContextLoader` – reads `.aicontext/` into a model
- `ContextExporter`, `ExportTargets`, `ExportWriter` – deterministic exports for AGENTS.md, Copilot, Claude, Cursor, Windsurf, Devin and Aider, with `single` and `split` layouts and safe overwrite
- `ExportStatus` – context size per tool and export sync state

For the command-line tool install `AIContext.CLI` (`dotnet tool install --global AIContext.CLI`).
See https://github.com/aicontext-dev/aicontext for the specification and documentation.
