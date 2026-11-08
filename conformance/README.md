# AIContext conformance suite

Language-neutral examples for independent implementations of the [AIContext specification](../SPEC.md). They define, by example, which `context.json` documents are valid and where an implementation has to report an error for the invalid ones. The reference implementation (`AIContext`) runs the suite in its own tests.

Status: the suite covers the draft format `0.1.0` and only `context.json` (SPEC 5 and 7). Cases for rule files, export output and the rules for newer versions will follow; see [GOVERNANCE.md section 6.2](../GOVERNANCE.md).

## Layout

```text
conformance/
  v0.1/
    manifest.json      the list of cases with the expected outcome
    valid/             documents that must be accepted
    invalid/           documents that must be rejected
```

A directory is named after the specification version it belongs to.

## Using it

For every entry in `manifest.json`:

1. Treat the file as the complete `.aicontext/context.json` of a project whose `README.md` is non-empty.
2. If `valid` is `true`, the implementation MUST report no **error** for it (warnings, such as missing recommended files, are allowed).
3. If `valid` is `false`, the implementation MUST report at least one **error** located at `pointer`, an [RFC 6901](https://www.rfc-editor.org/rfc/rfc6901) JSON Pointer into the document (the empty string `""` is the document root).

Only the location is part of the contract, not the wording or the code of the message, so each implementation can word its own diagnostics. Some invalid cases rely on rules that JSON Schema cannot express (for example `project.name` that is only whitespace, or `exports.defaultTarget` that is not listed in `exports.targets`); an implementation has to enforce them in addition to the schema.

## Changing the suite

- While the specification is a draft, cases may be added. Adding a case that an existing implementation fails is a signal that the specification or that implementation needs to change, and it is recorded in the [CHANGELOG](../CHANGELOG.md).
- Changing the expected outcome of an existing case changes the specification, so it follows the rules in [GOVERNANCE.md](../GOVERNANCE.md).
- Every file in `valid/` and `invalid/` must be listed in `manifest.json`; the reference implementation's tests check this.
