# AIContext conformance suite

Language-neutral examples for independent implementations of the [AIContext specification](../SPEC.md). They define, by example, which `context.json` documents are valid and where an implementation has to report an error for the invalid ones. The reference implementation (`AIContext`) runs the suite in its own tests.

Status: the suite covers the draft format `0.1.0`: `context.json` (SPEC 5 and 7), rule files (SPEC 4.4 and 4.4.1) and the path patterns of rules (SPEC 4.4.2). Cases for export output and the rules for newer versions will follow; see [GOVERNANCE.md section 6.2](../GOVERNANCE.md).

The suite does not introduce rules of its own: every case follows from [SPEC 4.4, 4.4.1, 4.4.2, 5.1 and 5.2](../SPEC.md) and the [schema](../schema/v1/context.schema.json). If an independent implementation fails a case although it follows the specification and the schema, the specification is incomplete, and that is a bug to report here.

## Layout

```text
conformance/
  v0.1/
    manifest.json      the list of cases with the expected outcome ("cases", "ruleCases", "globCases", "globInvalid")
    valid/             context.json documents that must be accepted
    invalid/           context.json documents that must be rejected
    rules/
      valid/           rule files that must be accepted
      invalid/         rule files that must be rejected
```

A directory is named after the specification version it belongs to. The fixtures have LF line endings.

## Using it

### `context.json` (`cases`)

For every entry in `cases` of `manifest.json`:

1. Treat the file as the complete `.aicontext/context.json` of a project whose `README.md` is non-empty.
2. If `valid` is `true`, the implementation MUST report no **error** for it (warnings, such as missing recommended files, are allowed).
3. If `valid` is `false`, the implementation MUST report at least one **error** located at `pointer`, an [RFC 6901](https://www.rfc-editor.org/rfc/rfc6901) JSON Pointer into the document (the empty string `""` is the document root).

Every case names in `spec` the section of the specification it relies on (`5.1` for the properties, `5.2` for the rules a JSON Schema evaluator does not enforce by default). Cases marked `"beyondSchema": true` are accepted by a JSON Schema evaluator with default settings: the implementation has to enforce the rule itself, which is what SPEC 5.2 requires (for example `exports.defaultTarget` that is not listed in `exports.targets`, or `metadata.lastReviewed` when the evaluator does not assert `format`).

Only the location is part of the contract, not the wording or the code of the message, so each implementation can word its own diagnostics.

### Rule files (`ruleCases`)

For every entry in `ruleCases` of `manifest.json`:

1. Install the file as `.aicontext/rules/<installAs>` of a valid project (`installAs` defaults to the file name of `file`; it is given when the name of the rule is what the case tests, such as `_team.md` or `.md`), and read the rules of that project.
2. If `valid` is `true`, the implementation MUST report no **error**, and the project MUST have exactly one rule that matches `expect`: `kind` (`always`, `on-demand` or `scoped`), `paths` (the patterns, in order; an empty list for a rule that is not scoped) and `description` (the text, or `null` when the rule has none). When `warns` is `true`, the implementation SHOULD report a warning (it is a case about an unknown key).
3. If `valid` is `false`, the implementation MUST report at least one **error** located at the rule file (`.aicontext/rules/<installAs>`), and the rule MUST NOT be part of the context. In particular it is never exported as an always-on rule, which would make it broader than its author wrote it.

Every case names in `spec` the section it relies on (`4.4` for the names and the meaning of the keys, `4.4.1` for the syntax of the frontmatter, `4.4.2` for the path patterns). As for `context.json`, only the location is part of the contract, not the wording or the code of the message.

### Path patterns (`globFiles`, `globCases`, `globInvalid`)

`globFiles` is a fixed set of file paths, relative to the project root and with `/` separators (it contains a root-level file called `src`, names that start with a dot, a directory with a comma in its name, and directories that differ only by letter case). For every entry of `globCases`:

1. `pattern` is one item of `paths`, after the frontmatter was read (so a comma in it is part of the pattern, and a brace group is still in it).
2. The implementation MUST match exactly the files listed in `matches` among the files of `globFiles`, and no other. An empty `matches` is a valid pattern that matches no file of the set.

Every entry of `globInvalid` is an item that is not a pattern of SPEC 4.4.2. The implementation MUST NOT accept it: a rule that has it as an item of `paths` is invalid (an error at the rule file) and is never exported. The cases follow from SPEC 4.4.2 alone; translating a pattern for a tool is not part of the suite.

## Changing the suite

- While the specification is a draft, cases may be added. Adding a case that an existing implementation fails is a signal that the specification or that implementation needs to change, and it is recorded in the [CHANGELOG](../CHANGELOG.md).
- Changing the expected outcome of an existing case changes the specification, so it follows the rules in [GOVERNANCE.md](../GOVERNANCE.md).
- Every file in `valid/`, `invalid/`, `rules/valid/` and `rules/invalid/` must be listed in `manifest.json`; the reference implementation's tests check this.
