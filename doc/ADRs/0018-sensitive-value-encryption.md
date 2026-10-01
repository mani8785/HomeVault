# ADR-0018: Sensitive-value encryption and key recovery

Status: Accepted
Created: 2026-10-01
Accepted: 2026-10-01
Issue: [HV-21 / #25](https://github.com/mani8785/HomeVault/issues/25)

## Current state and scope

[ADR-0007](0007-sensitive-attributes.md) classifies and redacts Sensitive
attribute values in Domain; deliberate ReadValue access remains possible.
[ADR-0017](0017-durable-local-persistence.md) persists only Vaults, memberships,
and Asset identity/VaultId/name. SqliteAssetRegistrationStore rejects Assets
with attributes or Evidence. Neither the database nor its backups are encrypted.

HV-21 is a decision issue with the implementation subtasks linked below. This decision
defines a future protection boundary and test plan; it does not add encryption,
attribute persistence, authentication, packages, or migrations. Attribute storage
needs a separately reviewed application operation and schema before integration.

## Threats and protection boundary

Propose protecting Sensitive attribute text against disclosure from a copied
database, its journal/WAL, or its backups when the attacker lacks the keys.
Detect changes to protected payloads and substitution between attributes.

This does not protect against malware executing as the unlocked OS user,
administrators/debuggers reading process memory, an authorized caller deliberately
logging decrypted text, deletion, or restoration of an older valid database.
Encryption does not authenticate users or enforce Vault membership; HV-22 remains
necessary. Current Playground actors are fictional and are not authentication.

| Data | Proposed treatment |
| --- | --- |
| Sensitive attribute text | Authenticated encryption before reaching EF/SQLite |
| Ordinary attribute text | Plaintext, explicitly classified under ADR-0007 |
| Vault/Asset names, attribute labels, IDs, memberships, classifications | Visible metadata; never put secrets in labels |
| File Evidence, document contents, other future payloads | Separate design before storage; no implicit protection claim |
| Keys and recovery material | Separate protected storage, never database columns or source control |

Names and relationships can themselves reveal private information. The owner must
accept this metadata exposure for field encryption. If all database contents must
be confidential, revisit whole-database encryption before implementation. OS disk
encryption is useful additional protection, but does not protect a copied backup
on another unencrypted medium.

## Proposed Infrastructure design

1. Keep cryptography, key access, and ciphertext models in Infrastructure. Domain
   retains its factories and classification rules. Application owns the eventual
   purpose-specific authorized read/write contract, with no provider or key types.
   Avoid automatic EF value converters that decrypt every materialized record.
2. Use the framework AesGcm implementation with a 256-bit random key, a 96-bit
   nonce, and a 128-bit authentication tag. Persist a versioned envelope containing
   format version, key ID, nonce, ciphertext, and tag. Use authenticated associated
   data with an unambiguous encoding of format/purpose, Vault ID, Asset ID, and
   stable attribute identity. Review that identity and encoding with the attribute
   schema; do not concatenate ambiguous strings or bind only a mutable label.
3. Nonce reuse with the same key must be prevented. The implementation slice must
   specify generation, per-key usage limits, concurrent writers, and restored-copy
   behavior before code is approved. Prefer cryptographically random nonces with
   a conservative documented usage bound; never use timestamps or a counter that
   resets on restore. Random sampling tests alone cannot prove uniqueness.
4. Authorize before decrypting. Reads use current membership and a consistent
   storage view; writes retain the accepted archive/role transaction rules.
   Persist only ciphertext for Sensitive values, including updates and error paths.
   No plaintext fallback on absent keys, corrupt envelopes, or unsupported versions.
5. Return safe failure categories at the application boundary. Do not include
   plaintext, key material, payloads, connection strings, or raw crypto/provider
   exceptions in user output or telemetry. Keep EF sensitive-data logging off.
   Clear temporary byte buffers where possible; managed strings cannot promise
   secure erasure. Redacted Domain properties alone are insufficient protection.

The algorithm is a recommendation, not permission to invent cryptographic
primitives. Review envelope/key lifecycle details in the first implementation PR.

## Key custody, recovery, and rotation proposal

For the initial Windows local adapter, propose random data keys stored in a
versioned key ring outside the database and checkout, with restrictive OS access
and Windows DPAPI CurrentUser wrapping. An unlocked process running as that user
can obtain them. This deliberately limits that adapter to Windows; unsupported
platforms must fail explicitly until a separate key-store implementation is agreed.
No plaintext key file or machine-wide fallback. Select any required supported
package and pin its version only after acceptance.

The owner confirmed portable recovery with a separately stored recovery key on
2026-10-01. Propose a separately held, cryptographically random
256-bit recovery key that encrypts an exported key-ring package. Store the recovery
key separately from both the database backup and wrapped key-ring export. Do not
accept a human password as if it were a random key; password-based recovery would
require a separately reviewed derivation design. Do not expose recovery secrets
as CLI arguments, environment variables, logs, or normal console output. Design
an explicit secure export/import interaction and test it before enabling writes.

Enrollment must verify recovery before sensitive persistence is enabled. Losing
both the usable OS-wrapped keys and recovery material means permanent data loss.
On a replacement Windows installation, recover the ring and wrap it for the new
user, then verify known records before switching the database path. A DPAPI-only
alternative avoids export complexity but cannot promise recovery after account
or machine loss. The owner selected portable recovery; the detailed wrapping
and provisioning design still requires review.

Use key IDs and separate active-for-write and retained-for-read states. Rotation
creates and durably saves a new key and updated recovery export before any record
references it. Re-encrypt existing data in explicit restartable transactions;
interruption leaves all committed records readable. Retain old keys while any
live records or retained backups need them. Key retirement requires a verified
inventory and recovery rehearsal; never delete keys merely because they are old.
Rotating keys cannot revoke plaintext or old key copies already stolen.

Back up database and key ring as a coordinated recovery set while writers and
rotation are stopped. The existing SQLite backup API remains responsible for
consistent database copies; it does not export keys. Recovery must verify schema,
integrity, authorization, and authenticated decryption before publishing a restored
configuration. Ciphertext-only database backups still expose the metadata above.
Never overwrite the only known-good database or key ring during recovery.

## Alternatives considered

| Alternative | Tradeoff |
| --- | --- |
| Field encryption with explicit Infrastructure mapping | Fits Sensitive classification; metadata remains visible and key lifecycle is application work |
| SQLCipher or another encrypted SQLite build | Broader database confidentiality; changes native provider packaging and migration/backup/viewer support; still requires key recovery |
| DPAPI on every value | Simple Windows-only approach; couples data portability directly to OS key recovery |
| ASP.NET Core Data Protection | Provides key management, but its stated primary purpose is not indefinite confidential payload storage; evaluate lifecycle before adopting for durable records |
| Disk encryption alone | Helps with a powered-off stolen device; does not meet copied-database/backup threat by itself |

Standard SQLite does not provide file encryption by default. Adding a Password
connection-string setting to the current provider is not an encryption solution.

## Tasks after acceptance, in dependency order

Implementation slices created after acceptance:

- [HV-21.1 / #63](https://github.com/mani8785/HomeVault/issues/63): envelope and key lifecycle contracts.
- [HV-21.2 / #64](https://github.com/mani8785/HomeVault/issues/64): Windows custody and portable recovery; depends on #63.
- [HV-21.3 / #65](https://github.com/mani8785/HomeVault/issues/65): authorized attribute persistence; depends on #63 and #64.
- [HV-21.4 / #66](https://github.com/mani8785/HomeVault/issues/66): rotation and recovery; depends on the preceding slices.

1. **Envelope and key lifecycle:** finalize encoding, nonce bounds, key-store and
   recovery interfaces, dependency versions, secure provisioning, and safe failures.
   Verify known cryptographic vectors, round trips, wrong keys, malformed/versioned
   inputs, and mutations of every authenticated component. Review nonce design.
2. **Windows custody and portable recovery:** test provisioning,
   ACLs, wrong-user/missing/corrupt key-ring failures, recovery into a fresh profile,
   and no replacement of an existing ring. Add meaningful Windows CI coverage;
   Linux-only CI cannot certify DPAPI. Use fictional keys and isolated test stores.
3. **Authorized attribute persistence:** separately refine the narrow application
   operation and migration, then integrate encryption. Test current access/role/
   archive rules, rollback, exact Unicode/whitespace preservation, and record-swap
   rejection. Raw SQLite rows must contain ciphertext; inspect database, WAL,
   journals, backups, and captured diagnostics for fictional plaintext sentinels.
   Sentinel absence is supporting evidence, not proof of complete confidentiality.
4. **Rotation and recovery rehearsal:** verify mixed key generations, interruption
   before/after key publication and database commits, old-backup restoration,
   unavailable retired keys, wrong recovery key, and fresh-profile recovery.
   Document key retention and safe terminal operations; no helper scripts without
   separate approval. Persist no real secrets until these checks pass.

Each slice needs restore, formatting, Release build, NUnit tests, relevant runtime
verification, and passing PR CI. Existing 259 tests exercise current behavior;
they do not verify future encryption. No claim of secret-free diagnostics for the
new design is established until its integration and failure-path tests execute.

## Confirmation

The owner explicitly confirmed the remaining ADR choices on 2026-10-01,
including the copied-file threat model, visible metadata, field encryption,
Windows key custody, and rotation/retention responsibilities. Portable recovery
with a separately stored recovery key was confirmed earlier the same day.
This records architectural acceptance, not completed encryption or permission
to merge. Detailed contracts and verification remain in the linked slices.

## References

- [AesGcm encryption and nonce requirements](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aesgcm.encrypt)
- [Windows ProtectedData](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.protecteddata)
- [SQLite encryption and native providers](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/encryption)
- [ASP.NET Core Data Protection scope](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/introduction)
- [Data Protection key encryption at rest](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-encryption-at-rest)
