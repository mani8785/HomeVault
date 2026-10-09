# ADR-0030: Offline rotation and coordinated recovery

Status: Accepted
Created: 2026-10-09
Accepted: 2026-10-09
Issue: [HV-21.4 / #66](https://github.com/mani8785/HomeVault/issues/66)

## Context and review boundary

[ADR-0018](0018-sensitive-value-encryption.md) requires restartable rotation and
verified database/key recovery. The envelope, Windows custody and Sensitive
storage slices are implemented under [ADR-0027](0027-encryption-envelope-key-lifecycle.md),
[ADR-0028](0028-windows-key-custody-recovery.md) and
[ADR-0029](0029-authorized-sensitive-attributes.md). PR #91 completed #65.

Existing key recovery imports keys, not an application database. SQLite backup
checks do not establish that every Sensitive record decrypts or that restored
accounts and permissions are valid. The remaining choices are the maintenance
authority, restart algorithm, coordinated recovery format and activation process.
This proposal supplements the accepted decisions without changing their cipher,
role policy, metadata exposure or fresh-write-key requirements. No new package,
database migration, HTTP endpoint or helper script is proposed.

## 1. Offline operator boundary

Provide explicit Windows terminal operations for rotation, coordinated backup
and recovery. Require an interactive terminal and the existing hidden recovery
secret input. Never accept that secret through arguments, environment variables,
redirected input, logs or ordinary output.

Stop the API, Playground and other database tools before maintenance. Reuse the
exclusive database host lock, then acquire the data-ring lock in that fixed order.
Refactor internal custody locking if needed so maintenance does not deadlock by
reacquiring its own lock. Keep both locks until the operation finishes. These
locks coordinate supported HomeVault commands; they cannot prevent arbitrary
SQLite clients or filesystem administrators from bypassing them.

The OS operator with the recovery secret may verify and re-encrypt every Sensitive
record, including records in archived Vaults. This technical maintenance changes
only envelopes, not values, attribute identities, memberships or archive state.
It is not an authenticated application action and must not grant an operator
Vault membership or expose plaintext. Normal HTTP operations keep ADR-0029's
authorization-before-decryption policy. Output contains safe outcomes and counts,
not record names, values, payloads or raw exceptions.

## 2. Restartable rotation

For each invocation, publish and read-verify a fresh write key and its recovery
export before committing any envelope that uses it. Never resume writing with a
key loaded from disk. Process records in a deterministic order in bounded
transactions, up to 128 records per transaction. Authenticate each old envelope
using its actual Vault, Asset and attribute identities before replacing it.

Keep previously committed batches on interruption; roll back the current batch.
All generations remain readable because no old keys are deleted. A rerun scans
the full current record set and re-encrypts it with fresh write sessions. This is
safe restart, not a promise to resume at the exact last record. It avoids a
persisted writable-key checkpoint and can repeat completed work. Abort on a
missing key, corrupt envelope or invalid metadata; report incomplete rotation
without claiming that every record has been rotated.

Publish another fresh key/export before reaching the existing 65,536-encryption
session bound. Use the same path for normal rollover and interrupted invocations.
Changing ciphertext must preserve all Domain values and current permissions.
Retain all keys and recovery exports, even when an interrupted operation has not
committed any records. The existing ring capacity fails closed; automatic pruning
and retirement are outside #66.

## 3. Coordinated backup format

Create a new private staging directory outside the checkout. While maintenance
locks are held, use SQLite's backup API to create a consistent database copy and
include the exact verified recovery export for the current ring. Do not copy
DPAPI or authentication session-key files. Validate current schema, integrity,
foreign keys, authorization structure and authenticated decryption of every
Sensitive record before publishing the set.

Use fixed filenames: `database.sqlite`, `keys.hvkr` and `manifest.hvbm`.
The manifest is a versioned, fixed-length binary record containing a format/purpose
identifier, random 32-byte salt, ring ID, ring generation, both file lengths and
both SHA-256 file hashes. Authenticate the manifest with HMAC-SHA-256 using a
separate 32-byte key derived from the recovery secret with HKDF-SHA-256 and the
purpose `HomeVault.BackupManifest.v1`. Use framework cryptographic implementations,
fixed byte order, exact-length parsing and constant-time tag comparison. Specify
the byte layout and verification vectors alongside the implementation. The
manifest never supplies paths. Hash database files as streams, not whole-file
memory allocations; keep recovery-package limits from ADR-0028.

The authenticated manifest binds the database, including account/membership
metadata, to this recovery export. An unauthenticated checksum alone would only
detect accidental damage. Verify the tag before trusting manifest fields, and
verify file lengths/hashes before opening the copied database for recovery.
Reject malformed, unsupported, truncated, substituted or incomplete sets.

Read-verify staged outputs, flush files, then publish by renaming the directory
on the same volume to a previously nonexistent destination. A process interruption
must never expose a partial directory as a completed set. Do not claim stronger
power-loss durability than the underlying filesystem supports; an incomplete or
corrupt set must fail subsequent verification. Retain the existing database,
ring and earlier backups. Backup names/ordinary values remain visible: the
manifest adds authenticity, not whole-database confidentiality.

## 4. Recovery and deliberate activation

Restore into a new private destination on the current Windows profile. Reject
existing destinations, overlapping stores and reparse-point paths using the
existing private-storage rules. Verify the source set, copy into private staging,
and verify the staged bytes again before parsing. Rewrap recovered data keys for
the destination user using the existing portable recovery path. Provision new
authentication session keys in a separate directory; never recover old cookies.

The staged result contains separate database, data-key and session-key directories
compatible with the current host's non-overlap rules. Require the current exact
application schema for this first recovery command. An older backup taken with
this schema remains recoverable after later key rotations. Backups with an older
schema require a separately verified migration workflow; do not auto-migrate
unknown or historical schemas during this operation.

Before publication, validate SQLite integrity and foreign keys, existing Domain
restoration invariants, Asset-to-Vault ownership, attribute identities and the
combined ordinary/Sensitive name rules. Validate account references and stored
role/Owner rules without inventing a new requirement that every Owner is enabled.
Reject orphaned fictional Playground memberships rather than silently assigning
them to real accounts. Authenticate and decrypt every Sensitive envelope without
printing or persisting plaintext.

Run the existing restored-state invalidation in the staged database: rotate
account security stamps and consume outstanding invitations/recovery credentials.
Preserve password hashes, disabled-account state and Vault memberships. Revalidate
the resulting database before publishing the destination directory. Any failure
leaves the source and existing live stores untouched; only operation-owned staging
may be cleaned up. Publication must not overwrite an existing destination.

Do not change the running host's configuration or automatically start it. The
operator deliberately starts `serve-encrypted` against the new paths after success,
with an external export directory and separately held recovery secret. Normal
startup must publish a new verified write key before accepting Sensitive writes.

Recovery restores the historical account/membership state in the chosen backup;
it cannot infer later revocations. Review that state before use. Authentication
tests must prove new logins and current role checks work, and old cookies and
credentials fail. This does not add rollback detection, override account lockout,
reenable disabled accounts or guarantee access if all usable owners were disabled.

## Implementation and verification sequence

Keep #66 In Progress until implementation and verification are complete:

1. Implement offline locking and bounded rotation; test mixed generations, archive
   preservation, nonce/session bounds and interruptions before/after key/export
   publication and database commits. Verify reruns and safe ring-capacity failure.
2. Implement coordinated sets and authenticated manifests; test wrong secrets,
   corruption, substituted files, invalid schemas, private-path violations,
   incomplete publication and collisions without replacing existing files.
3. Implement staged recovery and restored-state invalidation. Test old backups,
   unavailable retained keys, corrupt authorization metadata and failures before
   publication. In Windows CI, recover a complete fictional database into the
   existing disposable second profile and exercise authorized and denied reads,
   fresh login and rejection of old sessions/credentials.
4. Document exact direct terminal commands, shutdown/activation steps, retention,
   failure recovery and limits. Verify fictional plaintext sentinels are absent
   from SQLite artifacts, backups and captured diagnostics. Run restore, format,
   Release build, NUnit and current-revision Windows/Linux PR CI.

Do not delete keys or backups as a test of retirement outside isolated fixtures.
No real Sensitive data is permitted until #66's required checks pass. Parent
issue completion still requires its remaining acceptance criteria and review.

## Alternatives and consequences

| Alternative | Reason for the proposed choice |
| --- | --- |
| Online background rotation | Adds concurrent-writer coordination and scheduling; offline maintenance matches the initial single-user target |
| One transaction for the whole database | Simpler rollback but unbounded transaction duration; bounded batches allow safe interruption |
| Persist a writable-key/checkpoint pair | Conflicts with the accepted fresh-session rule; rescanning costs work but preserves that rule |
| Independent database and key exports | Easy to mismatch; an authenticated manifest identifies one verified recovery set |
| Restore in place and immediately start serving | Risks the known-good copy and premature access; a new destination plus deliberate activation is reversible |
| Automatically remove old keys | Cannot establish which retained backups still need them; retain until a separately reviewed inventory/retirement operation |

Reuse SQLite backup, Windows custody, framework crypto, existing private-storage
checks and account invalidation. Do not introduce a general backup framework,
repository abstraction or third-party crypto package for these three operations.
The tradeoffs are maintenance downtime, extra retained storage and repeated work
after interrupted rotation. Managed plaintext strings still cannot promise secure
erasure; clear owned temporary byte buffers and avoid unnecessary materialization.

## Confirmation

The owner explicitly accepted sections 1-4 on 2026-10-09. Implementation and
passing verification are still required before closing #66 or removing its real-data gate.
