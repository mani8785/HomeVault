# HV-21.1: Encryption envelope and key lifecycle contracts

Implements [#63](https://github.com/mani8785/HomeVault/issues/63) under accepted
[ADR-0027](ADRs/0027-encryption-envelope-key-lifecycle.md). This is an internal
Infrastructure building block, not enabled Sensitive storage or a new user endpoint.

## What changed

EnvelopeEncryption validates bounded binary envelopes before key lookup, snapshots
caller bytes against concurrent mutation, builds authenticated record context and
decrypts through an owned read-key lease. Returned plaintext is accessed through
ReadValue, never a public serializable payload property. Safe errors do not carry
provider exception messages, keys, ciphertext or user text.

WriteKeySession owns one key and serializes nonce reservation plus encryption.
It retains every reserved nonce, rejects collisions with at most eight retries,
and permits at most 65,536 reservations. Reservations remain consumed after failed
text validation/encryption or an abandoned database operation. Disposal clears the
key and permanently disables writes; there is no reset or read-lease-to-write API.
Entropy injection and raw-key construction are internal trusted/test boundaries,
not public caller capabilities.

The framing uses AES-256-GCM from .NET with 12-byte nonces and 16-byte tags.
Magic/version/key ID/nonce/length and the Vault, Asset and stable attribute IDs
are authenticated using the exact encoding in ADR-0027. Strict UTF-8 preserves
Unicode, whitespace and embedded zero characters; invalid surrogate/UTF-8 data
fails instead of being replaced. The primitive permits empty strings and bounds
encoded plaintext to 1 MiB. Later Domain/application validation remains responsible
for attribute business rules.

ReadKeyLease exposes no key bytes and clears its owned buffer on disposal. Temporary
plaintext byte arrays are cleared in finally blocks. Returned strings and runtime
copies cannot promise secure erasure. Neither keys nor payloads are logged.

## Deliberately unavailable production capabilities

IEncryptionKeyCustody defines verified fresh-session issuance and retained-key
lookup, but only tests implement it in this slice. It is internal, not registered
in the host, and does not accept a caller-supplied recovery-verified boolean.
#64 must generate a fresh random key per writable activation, serialize ring
publication across processes, persist protected keys and the recovery export,
verify recovery, then transfer exclusive key ownership to one session.
Imported/retained keys must remain decrypt-only. No test fake is a production store.

The session enforces uniqueness within its lifetime. Fresh independently random
keys isolate sessions and restored copies under the custody contract; #63 tests
do not certify future DPAPI, disk durability, interprocess locks or recovery.
Restoring live process snapshots is outside the accepted threat model. Key counts
grow with writable activations; old keys cannot be deleted while backups need them.

No schema, migration, package, script, CLI encryption command, EF converter or
authorization policy is added. Existing databases remain as before. #64 implements
Windows custody/recovery; #65 reviews stable attribute identity, Sensitive policy
and encrypted persistence; #66 proves rotation and recovery. #25/#72/#26 remain open.

## Verification and terminal commands

The focused tests use fictional or published vector keys only. They check GCM
specification cases 13/14, a fixed HomeVault wire fixture, exact round trips,
mutation of every envelope byte, each record identity, wrong/missing/misidentified
keys, malformed/truncated/oversized/versioned inputs, strict encoding, concurrent
writes, forced collisions, all 65,536 reservations, disposal and safe diagnostics.
The fixture was independently assembled from the specified bytes and framework
AES-GCM; it is a format regression test, not independent cryptographic certification.
Nonce sampling is supporting evidence; the set and serialized reservation boundary
provide session-local uniqueness enforcement.

From a clean working tree, run each command separately and stop on failure:

```powershell
cd D:\repos\HomeVault
git fetch origin
git switch codex/hv-21-1-encryption-contract
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build --configuration Release --no-restore -warnaserror
dotnet test --configuration Release --no-build --no-restore --logger trx --results-directory TestResults/encryption
dotnet test tests/HomeVault.Infrastructure.Tests --configuration Release --no-build --no-restore --filter FullyQualifiedName~EncryptionEnvelopeTests
```

Expect a zero-warning build and passing tests. The focused command is the executable
encryption demonstration for this slice; there is no production encryption entry
point. To run the existing application after a successful build, use the setup,
database and session-key steps in the [account guide](hv-22-accounts.md), then:

```powershell
$accountHome = "$env:LOCALAPPDATA/HomeVault-Accounts"
dotnet run --project src/HomeVault.Api -c Release --no-build -- serve "$accountHome/accounts.db" "$accountHome/session-keys"
```

This starts the existing API at https://localhost:7443; stop with Ctrl+C. It does
not enable encryption. No database migration or real secret input is needed to
validate this change.

## Sources and reusable tooling

- [Microsoft AesGcm](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aesgcm): framework authenticated encryption, not a custom primitive.
- [Published GCM cases in dotnet/runtime](https://github.com/dotnet/runtime/blob/main/src/libraries/Common/tests/System/Security/Cryptography/AesGcmTests.cs): source for cases 13/14 used by the tests.
- [NIST SP 800-38D](https://csrc.nist.gov/pubs/sp/800/38/d/final): underlying GCM specification.

Windows ProtectedData remains the planned custody building block for #64; it does
not replace portable recovery or the application authorization boundary.
