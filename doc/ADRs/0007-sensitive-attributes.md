# ADR-0007: Explicit sensitivity and deliberate attribute reads

Status: Accepted
Created: 2026-09-28
Accepted: 2026-09-28
Issue: [HV-09 / #13](https://github.com/mani8785/HomeVault/issues/13)
Supersedes in part: [ADR-0006](0006-asset-attributes.md), only its unrestricted
attribute Value property and add-operation signature.

## Context

HV-08 exposes every attribute value through a public string property. Safe
ToString output alone does not prevent accidental disclosure when callers
inspect or serialize public properties. HV-09 requires agreement on sensitivity
and access before implementation; its issue has no linked subtasks or comments.

## Proposed contract

1. Require an explicit Ordinary or Sensitive classification in AddAttribute;
   provide no default or legacy overload that silently chooses Ordinary.
   Reject unknown enum values with InvalidSensitivity. Callers classify values:
   private identifiers, financial details, health information, and credentials
   are Sensitive; ordinary descriptive material such as Steel can be Ordinary.
   Do not infer classification from names or claim automatic content detection.
2. Store the original nonblank text privately. Expose Sensitivity and IsSensitive.
   Value returns the original text for Ordinary entries and null for Sensitive
   entries. Keep names visible as metadata: names must be descriptive labels,
   never containers for secrets. Both classifications retain safe ToString output.
3. Provide an explicitly named ReadValue() method for deliberate text access.
   It returns the original text for either classification. This is an accidental
   disclosure safeguard, not authorization: any code holding the domain object
   can call it. Application authorization remains HV-22. Do not introduce a
   caller-supplied permission boolean that pretends to enforce access.
4. ChangeAttribute preserves classification. In-place reclassification is out
   of scope; no automatic downgrade occurs on value replacement. Existing remove
   rules remain. Add validates name, value, then classification before lookup;
   existing failure precedence otherwise remains unchanged.
5. Keep immutable snapshots. Earlier snapshots and strings already read cannot
   be revoked by changing or removing the attribute. No claims of secure memory
   erasure, encryption, or protection against reflection/debuggers are made.
6. Default System.Text.Json serialization of an entry or Asset must not contain
   Sensitive text. Test this via the public API and use fictional sentinel text
   only in tests. Errors and ToString never include either names or values.
   Playground demonstrates classification and redaction without printing
   sensitive text. Deliberately reading then logging text remains caller misuse.

## Alternatives and tradeoffs

A default Ordinary classification preserves source compatibility but makes
omission silently unsafe. A required classification makes every existing call
site deliberate. Keeping Value unrestricted would make reflection-based logging
easy to misuse. Removing all text access prevents useful future operations;
ReadValue makes the access visible in code review without claiming access control.

This changes the domain API and requires updating tests and Playground callers.
No storage format currently exists to migrate. Ordinary values are still public
and may be serialized: correct classification remains the caller's responsibility.
Future persistence must deliberately read protected text and implement HV-21;
ordinary JSON output is not a lossless persistence representation.

## Implementation tasks after acceptance

1. Introduce classification, validation, redacted properties, and deliberate reads.
2. Preserve classification through changes and update existing call sites.
3. Test both classifications, invalid classification, failure atomicity,
   snapshots, exact deliberate reads, safe errors/formatting, and JSON disclosure.
4. Update documentation and Playground; run restore, formatting, Release build,
   NUnit tests, smoke test, and PR CI.

## Confirmation

The owner explicitly confirmed all six contract items on 2026-09-28. Encryption,
key management, Vault authorization, and a reclassification workflow are deferred.
This proposal does not authorize storing real credentials in the Playground.
