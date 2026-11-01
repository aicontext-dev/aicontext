# Changelog

All notable changes to the AIContext specification repository will be documented in this file.

## [0.1.0-draft] - 2026-11-01

### Added

- Initial public specification draft in [SPEC.md](./SPEC.md).
- Initial JSON Schema for `context.json` in [schema/v1/context.schema.json](./schema/v1/context.schema.json).
- Governance process draft in [GOVERNANCE.md](./GOVERNANCE.md).
- Initial repository `.aicontext` example files in [README.md](./.aicontext/README.md) and [context.json](./.aicontext/context.json).
- Validation script for local/CI checks in [validate-aicontext.ps1](./scripts/validate-aicontext.ps1).
- GitHub Actions validation workflow in [validate-aicontext.yml](./.github/workflows/validate-aicontext.yml).
- GitHub Pages deployment workflow in [pages.yml](./.github/workflows/pages.yml).
- Initial website pages in [docs/index.html](./docs/index.html) and [docs/spec/index.html](./docs/spec/index.html).
- Schema usage section in [README.md](./README.md) with canonical `$schema` URL example.
- Issue templates for bug reports, spec changes, and RFC proposals under [ISSUE_TEMPLATE](./.github/ISSUE_TEMPLATE/).
- Pull request CI workflow in [pr-checks.yml](./.github/workflows/pr-checks.yml) for context validation and .NET build checks.
- CLI MVP `init` and `validate` commands in [Program.cs](./src/AIContext.CLI/Program.cs).
- CLI project converted to executable app in [AIContext.CLI.csproj](./src/AIContext.CLI/AIContext.CLI.csproj).
- CLI `export --target agents` command to generate [AGENTS.md](./AGENTS.md) from `.aicontext` content.
- CLI `export --target all` now respects `exports.targets` in `.aicontext/context.json` (falls back to all supported targets when unset).
- First real additional export target: `copilot` output to [.github/copilot-instructions.md](./.github/copilot-instructions.md).
- Additional export target: `claude` output to [CLAUDE.md](./CLAUDE.md).
- Additional export target: `cursor` output to [.cursor/rules/project.mdc](./.cursor/rules/project.mdc).
- Additional export target: `windsurf` output to [.windsurf/rules/project.md](./.windsurf/rules/project.md).
- Additional export target: `aider` output to [CONVENTIONS.md](./CONVENTIONS.md).
- Export rendering logic extracted to core library in [ContextExporter.cs](./src/AIContext/ContextExporter.cs).
- Validation logic extracted to core library in [ContextValidator.cs](./src/AIContext/ContextValidator.cs).
- New test project [AIContext.Tests](./tests/AIContext.Tests/AIContext.Tests.csproj) with exporter coverage.
- Added validation rule tests in [ContextValidatorTests.cs](./tests/AIContext.Tests/ContextValidatorTests.cs).
- PR workflow now runs tests in [pr-checks.yml](./.github/workflows/pr-checks.yml).
