# aicontext

> Open format standard for AI coding assistant project context.
> One source of truth. Every AI tool.

**Specification and tooling — work in progress.**
⭐ Watch/Star this repo for updates.

## What is this?

`.aicontext/` is an open directory format for storing structured
project knowledge that AI coding assistants can use.

Works with: GitHub Copilot · Cursor · Claude Code · Windsurf · Aider

## Specification

First public draft is available in [SPEC.md](./SPEC.md).

Schema: [schema/v1/context.schema.json](./schema/v1/context.schema.json)  
Governance: [GOVERNANCE.md](./GOVERNANCE.md)  
Changelog: [CHANGELOG.md](./CHANGELOG.md)

## How to use schema

Use the canonical schema URL in your `.aicontext/context.json`:

```json
{
  "$schema": "https://aicontext.dev/schema/v1/context.schema.json",
  "specVersion": "0.1.0",
  "project": {
    "name": "my-project"
  }
}
```

Then validate locally:

```powershell
.\scripts\validate-aicontext.ps1
```

## Validation

Local validation command:

```powershell
.\scripts\validate-aicontext.ps1
```

CI workflow:

- [validate-aicontext.yml](./.github/workflows/validate-aicontext.yml)

## CLI (MVP)

Current commands:

- `aicontext init [--path <directory>] [--name <project-name>] [--force]`
- `aicontext validate [--path <directory>]`
- `aicontext export --target agents [--path <directory>]`
- `aicontext export --target copilot [--path <directory>]`
- `aicontext export --target claude [--path <directory>]`
- `aicontext export --target all [--path <directory>]` (scaffold: generates `agents` + `copilot` + `claude`, warns for pending targets)

## Website

GitHub Pages deployment workflow:

- [pages.yml](./.github/workflows/pages.yml)

Expected site URL:

- https://aicontext.dev/
