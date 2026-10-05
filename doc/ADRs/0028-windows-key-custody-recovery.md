# ADR-0028: Windows key custody and portable recovery

Status: Proposed
Created: 2026-10-05
Issue: [#64](https://github.com/mani8785/HomeVault/issues/64), under #25

## Context

[ADR-0018](0018-sensitive-value-encryption.md) accepts DPAPI CurrentUser and
separately held random recovery material but defers the provisioning interaction
and package details. [ADR-0027](0027-encryption-envelope-key-lifecycle.md), implemented
in merged PR #89, requires verified durable publication before issuing a fresh
write-key session. This proposal defines that custody boundary. Sensitive attribute
schema, HTTP policy and production write integration remain #65.

## Operator interaction and recovery secret

Use a 256-bit random recovery secret, supplied as exactly 64 hexadecimal characters
through an interactive hidden-input console prompt. The operator must generate it
with a cryptographically secure password-manager generator and keep it there,
separately from the database and recovery export. This is a random key, not a human
password; no password derivation or password-based recovery is supported. Syntax
validation cannot establish entropy, so document this operator responsibility.

Prompt twice when provisioning, compare decoded bytes, and never echo characters,
copy to the clipboard, print the secret, or accept it from arguments, environment
variables or redirected stdin. Recovery/open-for-write prompts once. Clear owned
character/byte buffers on exit; no claim of perfect memory erasure. An unattended
unlock mechanism is not selected. A caller cannot pass a verified-recovery boolean.

Add direct Playground operator commands, with nonsecret absolute paths only:

- `encryption-keys initialize <new-ring-directory> <export-directory>`
- `encryption-keys verify <ring-directory> <recovery-export-file>`
- `encryption-keys recover <recovery-export-file> <new-ring-directory>`

Initialization creates an empty protected ring and authenticated recovery export,
reads both back and verifies equivalence before reporting success. It does not
create a data-write session. Recovery imports retained keys as decrypt-only and
wraps them for the destination Windows user; it never overwrites an existing ring.
There is no normal console key generation/display command, plaintext key file or
implicit unlock at API startup. Public operator methods return only safe outcomes.

## Local custody and permissions

Pin `System.Security.Cryptography.ProtectedData` 10.0.12 in Infrastructure. Use
ProtectedData.Protect/Unprotect with CurrentUser and fixed versioned HomeVault
purpose entropy. Do not reuse authentication-session keys or Data Protection
payload formats. Unsupported systems fail explicitly before touching files.

Ring and export directories must be absolute, outside Git checkouts and separate
from the database file/directory selected for later integration. Reject reparse
points in the path and protected artifacts. Create owner-only Windows ACLs with
inheritance disabled; validate owner and allowed principals on each open, including
files. Never silently repair permissions or replace corrupt/missing rings. Use
exclusive file creation and staging names; reject collisions. The operator chooses
an existing private export directory, which can be on separate storage. The secret
must remain outside both locations in the password manager.

Use a bounded version-one ring payload containing format version, nonempty ring
Guid, generation number and unique nonempty key Guids with exactly 32 bytes per
key. All persisted keys are retained-for-read; writable ownership exists only in
the live session. Reject duplicate IDs, unsupported versions, invalid sizes,
unexpected fields and more than 4,096 keys. Refuse new sessions at this bound;
never delete historical keys automatically. Raw payload exists only in owned
memory buffers and is DPAPI-protected before filesystem writes.

## Recovery export encoding

The export uses a dedicated binary format, distinct from attribute envelopes:
ASCII HVKR magic (4 bytes), version 1 (1), ring Guid in network order (16),
generation uint64 big endian (8), random salt (32), random nonce (12), ciphertext
length uint32 big endian (4), ciphertext, then a 16-byte AES-GCM tag.
The fixed header is 77 bytes. Reject trailing/truncated data and exports over
1 MiB before decryption. Authenticate the entire header and fixed ASCII purpose
`HomeVault.KeyRecovery.v1` followed by a zero byte.

Derive a fresh 32-byte wrapping key per export using framework HKDF-SHA256 with
the random 32-byte salt, supplied recovery secret and the same purpose as info.
This is domain-separated random-key derivation, not password stretching. Encrypt
the ring payload with AES-256-GCM and the independent 12-byte nonce. New exports
always use fresh salt and nonce, including after restoration; do not reuse the
attribute-envelope purpose or its session counter. Independent random salts make
wrapping keys independent with cryptographic collision probability, not certainty.
Clear derived keys and plaintext buffers. Authenticate before parsing the payload,
then require header/payload ring ID and generation to agree.

## Atomic publication and concurrent writers

The internal Windows custody adapter implements IEncryptionKeyCustody. Read access
opens only a validated existing ring and returns independently owned read leases.
Write-session issuance requires an explicitly unlocked, in-memory recovery secret.
Under an exclusive OS file lock in the private ring directory, reload the latest
generation, generate a fresh random data key/ID, and construct the next generation.
Never reactivate a key from any persisted/imported generation.

Write immutable generation artifacts under unique names, flush them to disk, and
read/decrypt them back. Publish a new non-overwriting recovery export in the chosen
export directory and verify its plaintext matches the new ring, including the
fresh key. Only then atomically replace the local current-generation pointer and
return the exclusive write session. Hold the interprocess lock through publication.
No session is returned for partial publication or verification failure.

Failure before pointer replacement leaves the previous generation usable; failure
after replacement but before session return leaves an unused retained key. Orphan
artifacts may be retained for inspection; do not remove prior generations or the
only known-good recovery export. A later attempt reloads the committed generation
and generates another fresh key. A process restart must reacquire the secret and
publish another generation before writing. Stop writers/rotation for coordinated
database backups; merely copying a ring is not a verified database recovery.

Restore to a new private directory, validate/export-decrypt all keys, DPAPI-wrap
them for the current user, verify a synthetic authenticated record using a retained
key when available, then publish. It does not publish a database configuration or
claim real record recovery; #65/#66 must verify actual encrypted records and access.
Missing keys, wrong secret/user, malformed artifacts and unsafe permissions yield
safe failures without raw exception messages or plaintext fallback. Old valid
generation rollback is outside the accepted threat model.

## Verification and delivery

Tests use isolated fictional material only. Add cross-platform format/HKDF/GCM
tests and explicit unsupported-platform tests. Windows tests exercise actual DPAPI,
ACL rejection, missing/corrupt rings, wrong secrets, non-overwrite, generation
collisions, independent writers, injected publication failures and restart reads.
Verify wrong-user DPAPI rejection and recovery under a real second Windows profile
in the Windows CI job; a same-user round trip or mocked protector is insufficient
evidence for that requirement. Use direct steps in the existing workflow if runner
account setup is needed, never a new helper script. Document any environment limit
rather than claiming fresh-profile verification without executing it.

Test secret-input behavior (no echo, no redirected input, no secret arguments),
buffer cleanup and safe diagnostics. No real user keys/accounts are provisioned
for validation. Run restore, format verification, Release build, NUnit and current
Windows/Linux PR checks; provide PowerShell instructions and leave the PR open.

## Alternatives and tradeoffs

Generating and displaying a recovery key in ordinary terminal output risks shell
transcripts; raw secret exports conflict with the no-plaintext-key-file boundary.
A dedicated secure GUI or password-manager connector could automate provisioning
later but adds an integration decision now. Hidden entry of an externally generated
random key keeps the initial operator interaction small, with entropy and safekeeping
explicitly assigned to the operator. Keeping the secret DPAPI-persisted alongside
the ring would simplify unattended activation but is not selected here.

Immutable generations and a commit pointer avoid requiring atomic replacement
across two directories/volumes. The tradeoff is retained artifacts and explicit
capacity management. Key retirement, recovery-secret replacement, database
reencryption and unattended hosting remain separate reviewed work.

## Confirmation

Pending owner acceptance of the hidden-input operator interaction, externally
generated random secret, package pin, formats and publication protocol. No custody
implementation or provisioning is authorized by this proposal alone.

## References

- [ProtectedData 10.0.12](https://www.nuget.org/packages/System.Security.Cryptography.ProtectedData/10.0.12)
- [Windows CryptProtectData](https://learn.microsoft.com/en-us/windows/win32/api/dpapi/nf-dpapi-cryptprotectdata)
- [Framework HKDF](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.hkdf)
