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

## Validation

Local validation command:

```powershell
.\scripts\validate-aicontext.ps1
```

CI workflow:

- [validate-aicontext.yml](./.github/workflows/validate-aicontext.yml)
