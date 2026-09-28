# HV-09: Sensitive attributes

Issue: [#13](https://github.com/mani8785/HomeVault/issues/13).
Contract: accepted [ADR-0007](ADRs/0007-sensitive-attributes.md).

Completed tasks: require explicit classification on add; hide Sensitive text
from property inspection; provide deliberate ReadValue access; preserve
classification through changes; test validation, snapshots, diagnostics, and
default JSON serialization; demonstrate redaction in Playground.

AddAttribute now requires AttributeSensitivity.Ordinary or Sensitive. There is
no default classification. Invalid enum values return InvalidSensitivity after
name/value validation and before duplicate lookup. Private identifiers, financial
details, health information, and credentials must be classified Sensitive by the
caller; no automatic content detection occurs. Names remain visible descriptive
metadata and must never contain secrets.

Value is nullable: it returns null for Sensitive entries and exact text for
Ordinary entries. ReadValue() deliberately returns either kind of text. This
method does not enforce authorization. ChangeAttribute preserves classification;
reclassification is deferred. Errors and ToString do not contain supplied text.
Default System.Text.Json serialization hides Sensitive text, but ordinary values
remain visible. Such JSON is not a lossless persistence format.

Earlier snapshots and previously read strings cannot be revoked. This is an
accidental-disclosure safeguard, not encryption or secure memory. Authorization
and encryption/key management remain HV-22/HV-21. Callers must not log deliberate
reads or use custom serializers that expose private fields.

## Terminal verification

From the repository root, run each command after the preceding succeeds:

```powershell
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build --configuration Release --no-restore -warnaserror
dotnet test --configuration Release --no-build --no-restore --logger trx --results-directory TestResults
dotnet run --project src/HomeVault.Playground --configuration Release --no-build
```

The no-build commands require a successful Release build. Playground adds
`Sensitive attribute: True; value: [redacted]` without printing protected text.
All example inputs are fictional and nothing is persisted.
