# AIContext Governance

Status: DRAFT  
Last updated: 2026-11-08

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

### 6.1 Draft period (0.x)

Until the specification leaves draft status (its first stable release), the commitment above applies with one exception, which [SPEC.md section 9.1](./SPEC.md#91-specification-version-vs-tool-version) already describes: backward-compatible additions MAY be made within the current `specVersion`, and the table in that section records which tool version introduced which change. The schema does not allow unknown properties, so a validator from before such an addition rejects a newer document that uses it, while every document that was valid before stays valid. The first change that makes existing documents invalid or changes their meaning bumps `specVersion`.

### 6.2 Before the first stable release

Before the specification is declared stable the maintainers plan to decide and document:

- which published schema versions become immutable, and under which URLs,
- how an implementation reacts to a document that declares a newer `specVersion` or uses properties it does not know (reject, or accept with a warning),
- the [conformance suite](./conformance/README.md) that independent implementations can run, extended with the cases that define these compatibility rules.

Until then the suite only covers the draft `0.1.0` format, and cases may be added to it.

## 7. Conduct and collaboration

Participants are expected to collaborate respectfully, focus on technical arguments, and keep proposals grounded in practical interoperability.

