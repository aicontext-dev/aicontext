# AIContext Governance

Status: DRAFT  
Last updated: 2026-11-01

## 1. Scope

This document defines how AIContext specification changes are proposed, reviewed, and accepted.

## 2. Roles

### 2.1 Maintainers

Maintainers can:

- review and merge pull requests
- approve or reject RFC proposals
- publish releases and update versioned schema/spec files

### 2.2 Contributors

Contributors can:

- open issues and RFC proposals
- submit pull requests to spec, schema, and tooling
- participate in design discussions

## 3. Decision model

AIContext uses maintainer consensus for normative changes.

- Minor/editorial changes can be merged with one maintainer approval.
- Normative changes (format behavior, required fields, compatibility rules) require:
  - at least two maintainer approvals, and
  - no unresolved maintainer objections.

If consensus cannot be reached, maintainers may timebox discussion and make a final recorded decision in the RFC thread.

## 4. RFC process

Normative changes SHOULD follow this process:

1. Open a GitHub issue with title prefix `RFC:`.
2. Describe motivation, proposed change, alternatives, and migration impact.
3. Allow a public discussion period (recommended minimum: 14 days).
4. Submit PR(s) updating:
   - [SPEC.md](./SPEC.md)
   - schema files under [schema/](./schema/)
   - related docs/changelog
5. Merge after approval rules are met.
6. Publish release notes and version updates.

## 5. Versioning policy

AIContext follows Semantic Versioning at the specification level:

- PATCH: clarifications, typo fixes, non-normative edits
- MINOR: backward-compatible additions
- MAJOR: breaking changes

Any MAJOR change MUST include explicit migration guidance.

## 6. Compatibility commitment

- Versioned schema URLs, once published, MUST remain available.
- Existing versions MUST NOT change behavior retroactively.
- Breaking changes require a new MAJOR version path.

## 7. Conduct and collaboration

Participants are expected to collaborate respectfully, focus on technical arguments, and keep proposals grounded in practical interoperability.

