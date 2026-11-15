# Project: aicontext

Open-source format and CLI for maintaining shared AI coding instructions, exporting to supported tool formats and checking for stale exports in CI. It manages instruction files, not agent behavior.

**Tech stack:** .NET 9/10 (multi-targeted), Markdown, JSON Schema, JsonSchema.Net, xUnit  
**Architecture:** Spec-first standard with a core library and a thin CLI  
**Testing:** xUnit on net9.0 and net10.0; CI runs `validate --strict`, `export --check`, build and tests
