# ADR-0027: Encryption envelope and write-key lifecycle

Status: Proposed
Created: 2026-10-04
Issue: [#63](https://github.com/mani8785/HomeVault/issues/63), under #25

## Context

[ADR-0018](0018-sensitive-value-encryption.md) accepts field encryption but requires
the encoding, nonce limits, concurrency and restored-copy behavior to be specified
before implementation. The owner resumed #63 after merging Reminder PR #88.
No Sensitive storage or HTTP endpoint is enabled by this slice.

## Envelope and record binding

Use the existing .NET 10 AesGcm and RandomNumberGenerator implementations, with
256-bit data keys, 12-byte nonces and 16-byte tags. Add no cryptography package.
Infrastructure owns the encoding and crypto types; Domain/Application have no
key, algorithm or provider dependencies.

Version 1 is binary, with the following strict layout:

| Offset | Bytes | Meaning |
| --- | --- | --- |
| 0 | 4 | ASCII HVAE magic |
| 4 | 1 | Version 1 |
| 5 | 16 | Nonempty key Guid in RFC/network byte order |
| 21 | 12 | Nonce |
| 33 | 4 | Unsigned ciphertext length, big endian |
| 37 | N | Ciphertext |
| 37+N | 16 | Authentication tag |

Reject unknown versions, empty key IDs, wrong lengths, trailing bytes, truncation
and lengths above 1 MiB before allocating plaintext buffers or requesting a key.
The 1 MiB byte limit is an envelope resource bound, not a Domain text-length rule.
Use strict UTF-8 without BOM: preserve valid Unicode and whitespace exactly; reject
unpaired UTF-16 surrogates rather than silently replacing them. Empty plaintext
is supported by the primitive; later authorized attribute operations enforce
their own nonblank business rules. Ciphertext storage uses bytes; Base64, if later
needed by a transport, is a separate representation.

Associated authenticated data is the fixed ASCII purpose
`HomeVault.SensitiveAttribute` followed by one zero byte, the exact 37-byte header,
and Vault, Asset and stable attribute Guids (16 bytes each, network order).
All context Guids are nonempty. Header/key ID, nonce, length, purpose/version and
record identity are thereby authenticated. Do not bind only a mutable label.
The stable attribute Guid is a required caller context in #63, not a new Domain
identity or migration; #65 must approve its creation/preservation rules and schema
before integration. Copying a payload to a different context must fail decryption.

## Nonce limits and restored copies

Use a fresh random 256-bit key for each write session, never reactivate a previously
persisted or imported key for writing. A write session is an exclusively owned,
process-local disposable object. Retained keys are decrypt-only; restarting a
process or restoring any database/key-ring copy requires a newly generated key
and verified recovery publication before creating a new write session.

Within one write session, serialize nonce reservation and AES-GCM use, generate
12 random nonce bytes, and retain every reserved nonce in a private set. Reject
duplicates and retry at most eight times, then fail safely. Limit each key to
65,536 reservations. A failed encryption or abandoned transaction consumes its
reservation; never refund or clear the set while the key remains writable.
At exhaustion require a new key/session. Tests may inject a deterministic entropy
source internally to force collisions; production callers cannot supply nonces.

The set makes nonce uniqueness enforceable within a session rather than relying
on random sampling. Its bound is under 1 MiB of raw nonce data, plus collection
overhead. Independent fresh 256-bit keys separate concurrent process sessions and
restored copies; random key uniqueness remains a cryptographic assumption, not
mathematical certainty. Live process snapshots that duplicate key and RNG state
are outside the copied-file threat model and are not supported.

The custody adapter must serialize ring updates between local writers, reserve a
new key ID, persist the protected key and updated recovery export, verify recovery,
then hand exclusive key ownership to exactly one write session. Failed publication
must not return a writable key. Read-only retained key access must never offer an
API for constructing a new write session from arbitrary/imported key bytes.
#63 implements session lifetime/limits with test custody only; #64 must implement
and prove the durable publication and cross-process locking contract before any
production write session can be issued. No public raw-key encryption shortcut.

## Key access, recovery boundary and failures

Define Infrastructure-internal custody boundaries for creating a fresh verified
write session and looking up retained read keys by key ID. Key leases own their
buffers, cannot be formatted/serialized into secret output, and clear owned bytes
on disposal. Consumers cannot mutate the ring through a read lease. The envelope
service exposes ciphertext results or a deliberate plaintext read result, never
key bytes. Crypto/provider exceptions must not propagate as user-visible details.

Recovery publication is a precondition of issuing a write session, represented
by the custody boundary rather than a caller-supplied verified boolean. #64 will
define the versioned wrapped-ring/recovery package and secure operator interaction;
#63 neither exports keys nor adds a placeholder production provider. Missing keys,
unknown versions, invalid payload/context, authentication failure, unsupported
platform, exhausted sessions and unavailable custody return safe categories with
no plaintext fallback. Leave unknown-key versus authentication-failure details
inside Infrastructure; a later Application API must map failures safely after
authorization rather than becoming a key/record oracle.

Clear temporary plaintext/UTF-8/key buffers in finally blocks where owned. Returned
managed strings cannot promise secure erasure. Do not log payloads, private text,
key bytes or raw provider exceptions. No automatic EF decryption/converter, no
normal host registration and no production secret CLI command in this slice.

## Alternatives and consequences

A persisted nonce counter can roll back with a copied database/key ring. Reusing a
long-lived random-nonce key requires a trustworthy global lifetime usage ledger
across restored copies. Fresh session keys plus bounded nonce tracking avoid that
rollback dependency, at the cost of more retained key generations and recovery
publication on every writable activation. Do not delete old keys automatically.
This design must be revisited before high-frequency restarts or shared hosting.

Framework AES-GCM supplies the primitive; HomeVault implements only framing,
context binding and lifecycle controls. Windows ProtectedData and portable ring
recovery belong to #64; no new dependency is required for #63. Whole-database
encryption, password-based recovery and scheduling remain outside this contract.

## Tasks and evidence after acceptance

1. Implement strict versioned framing, safe outcomes and context-bound encryption/
   decryption in Infrastructure using framework cryptography.
2. Implement owned key leases and bounded exclusive write sessions; keep production
   custody unavailable until #64. Never expose a resettable public nonce counter.
3. Verify published AES-256-GCM known-answer vectors plus a fixed HomeVault framing
   fixture, exact Unicode/whitespace round trips, malformed/version/length rejection,
   wrong keys, tampering with every header/payload/tag component and record swaps.
4. Test forced nonce collisions, bounded retries/exhaustion, concurrent writes,
   disposed sessions, consumed failed reservations and read-only restored keys.
   These tests establish local enforcement, not future DPAPI/recovery correctness.
5. Review XML documentation and safe diagnostics; restore, format-check, Release
   build, NUnit and current Windows/Linux PR CI. Update the terminal guide without
   generating real secrets or adding scripts.

Sensitive APIs remain unexposed. #64 custody/recovery, #65 authorization/schema and
#66 rotation/recovery rehearsal remain required. #72/#26 stay open for that boundary.

## Confirmation

Pending owner acceptance of framing, stable identity context, the fresh-key session
model, bounds and custody contracts. Resuming #63 does not accept these new details.

## References

- [Microsoft AesGcm.Encrypt: nonce and associated-data requirements](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aesgcm.encrypt?view=net-10.0)
- [NIST SP 800-38D: GCM specification](https://csrc.nist.gov/pubs/sp/800/38/d/final)
