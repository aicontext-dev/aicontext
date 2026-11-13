# AIContext Governance

Status: DRAFT  
Last updated: 2026-11-13

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
- With two or more maintainers, normative changes (format behavior, required fields, compatibility rules) require:
  - at least two maintainer approvals, and
  - no unresolved maintainer objections.

During the single-maintainer phase, the sole maintainer may accept a normative change by recording the decision, rationale, alternatives and compatibility impact in a public issue or pull request. A second approval is not required in that phase; this does not waive the compatibility policy or the need for specification, implementation and conformance evidence to agree.

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

During the single-maintainer draft phase, the proposal, discussion and decision may live in the implementing pull request instead of a separate RFC issue. If the recommended discussion period is shortened, record why in that pull request. The change and its migration impact must still be reviewable.

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

Until the specification leaves draft status (its first stable release), the commitment above applies with one exception, which [SPEC.md section 9.1](./SPEC.md#91-specification-version-vs-tool-version) already describes: backward-compatible additions MAY be made within the current `specVersion`, and the table in that section records which tool version introduced which change. The schema does not allow unknown properties, so a validator from before such an addition rejects a newer document that uses it, while every document that was valid before stays valid. The first change that makes existing documents invalid or changes their meaning bumps `specVersion`. This concerns `context.json`, the document that declares the version; rule files carry no version of their own, so a change of what a rule file means is recorded in the table of SPEC 9.1 and in the CHANGELOG, with migration examples.

### 6.2 Before the first stable release

Draft describes the maturity of the compatibility commitment, not whether the tooling is usable. A draft can be released and tried in real projects. Declaring the specification stable is a separate decision from releasing a new CLI version or presenting the project publicly.

Before the specification is declared stable the maintainers plan to decide and document:

- how a project version identifies the whole input contract, including rule frontmatter and path patterns, rather than only `context.json`;
- which published specification, schema and fixture snapshots become immutable, and under which URLs, keeping existing published URLs available;
- which versions each implementation supports, how it handles newer versions and unknown properties, and how projects migrate;
- how to distinguish an input-format change from a correction to a tool-specific exporter;
- the [conformance suite](./conformance/README.md) covering that contract, compatibility cases and export semantics, with reference-output fixtures where appropriate;
- evidence from independent review or a scoped independent implementation, and real-project trials covering both initial setup and later maintenance. A second full CLI is not a prerequisite.

These are readiness criteria, not new requirements on existing `0.1.0` documents. Until a versioned change is accepted, the draft exceptions in 6.1 and the current specification remain in effect. The suite currently covers `context.json`, rule frontmatter and glob matching for `0.1.0`; export and cross-version cases remain to be added.

One open design question is whether misspelled scope keys should be errors instead of ignored unknown keys. Today, `Paths` is not `paths`: it produces a warning and can leave a rule always on, as [SPEC 4.4](./SPEC.md#44-rules-optional) specifies. Any change needs a separate proposal, a compatibility decision, implementation, fixtures and migration guidance; this document does not change that behavior or introduce a new draft version.

Stability is reached when the recorded evidence supports the chosen contract and no known unresolved design decision requires changing it incompatibly, not when a particular date, adoption count or marketing milestone is reached.

## 7. Conduct and collaboration

Participants are expected to collaborate respectfully, focus on technical arguments, and keep proposals grounded in practical interoperability.

