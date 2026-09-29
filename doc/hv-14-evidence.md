# HV-14: Asset evidence — domain slice

Issue: [#18](https://github.com/mani8785/HomeVault/issues/18).
Contract: accepted [ADR-0010](ADRs/0010-asset-evidence.md).

## Completed tasks

Asset.AddEvidence accepts an entry Guid, label, kind, and content. It validates
identity, label, kind, content, URL format, then duplicate identity. Url and Note
are supported. URLs must be absolute HTTP/HTTPS with a host, no user-info, and
no raw whitespace/control characters. Labels and content are preserved exactly.
Same content with different IDs is allowed. No network or file access occurs.

RemoveEvidence removes only the entry addressed by ID; missing entries fail.
Failures preserve existing entries. Evidence inspection provides read-only
snapshots of immutable entries. Content is available through ReadContent(),
not a public property, so default JSON omits it. Labels remain visible metadata
and must not hold secrets. String diagnostics and errors omit supplied data.
Snapshots and text already read cannot be revoked.

Tests cover formats, validation precedence, duplicates, removal, failure safety,
snapshots, per-Asset isolation, and default JSON disclosure. Playground prints
only operation outcomes and evidence kind, never content.

## Deferred resolution and access

The owner confirmed file/document resolution is later work. Domain will retain
stable reference metadata; Application will enforce Vault access and use a
technical contract; Infrastructure will resolve the chosen storage provider.
Storage choices, missing-resource behavior, permissions, and deletion semantics
must be agreed before implementation. Removing Evidence never deletes an external
document. This slice does not upload, open, download, or test URL reachability.

Evidence inherits the standalone Asset scope: actual Vault ownership, permissions,
and archive write enforcement require future application integration. Content
method access is not authorization or encryption. Keep HV-14 open until that
integration is implemented and tested. Default JSON is not lossless persistence.

## Terminal validation

Run each command from the repository root after the preceding succeeds:

```powershell
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build --configuration Release --no-restore -warnaserror
dotnet test --configuration Release --no-build --no-restore --logger trx --results-directory TestResults
dotnet run --project src/HomeVault.Playground --configuration Release --no-build
```

The no-build commands require a successful Release build. Expect None for adding
URL evidence, removing it, and adding note evidence; the inspected kind is Url.
No scripts or packages were added.
