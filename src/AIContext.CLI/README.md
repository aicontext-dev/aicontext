# AIContext.CLI

Open format standard for AI coding assistant project context.

Install:

```powershell
dotnet tool install --global AIContext.CLI
```

Run:

```powershell
aicontext --help
```

Available commands:

- `aicontext init [--path <directory>] [--name <project-name>] [--force]`
- `aicontext validate [--path <directory>]`
- `aicontext export --target agents [--path <directory>]`
- `aicontext export --target copilot [--path <directory>]`
- `aicontext export --target claude [--path <directory>]`
- `aicontext export --target cursor [--path <directory>]`
- `aicontext export --target windsurf [--path <directory>]`
- `aicontext export --target aider [--path <directory>]`
- `aicontext export --target all [--path <directory>]`

See https://github.com/aicontext-dev/aicontext for updates.
