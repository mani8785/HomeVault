# HV-21: Encryption delivery and verification record

All four implementation tasks for [HV-21 / #25](https://github.com/mani8785/HomeVault/issues/25)
are merged and closed as of 2026-10-09. The parent decision's agreed design,
implementation and verification evidence are recorded below. This is a completion
record, not a new architectural decision or a change to the protection boundary.

## Acceptance evidence

| Parent acceptance criterion | Delivered evidence |
| --- | --- |
| Agree threats, protected data, key custody and recovery/rotation expectations | Accepted [ADR-0018](ADRs/0018-sensitive-value-encryption.md), refined by [ADR-0027](ADRs/0027-encryption-envelope-key-lifecycle.md), [ADR-0028](ADRs/0028-windows-key-custody-recovery.md), [ADR-0029](ADRs/0029-authorized-sensitive-attributes.md) and [ADR-0030](ADRs/0030-offline-rotation-coordinated-recovery.md) |
| Record the Infrastructure design and verify safe error/log behavior | Explicit Infrastructure encryption and custody; safe outcomes and deliberate plaintext access; serialization/provider-failure tests, captured EF diagnostics, real WAL/journal inspection and authenticated-backup sentinel checks |
| Plan meaningful protection and recovery tests before implementation | ADR-0018's staged plan and the detailed accepted contracts preceded their implementation; NUnit coverage includes record binding, malformed envelopes, permission denial, interrupted publication/commits, mixed generations, old backups and real second-profile recovery |

Tests support these claims for the exercised paths. Sentinel absence and safe
serialization are not a proof that every future caller can never disclose a
secret. Keep the logging and sensitive-value rules in [AGENTS.md](../AGENTS.md).

## Merged implementation

| Task | Pull request | Capability |
| --- | --- | --- |
| [#63](https://github.com/mani8785/HomeVault/issues/63) | [#89](https://github.com/mani8785/HomeVault/pull/89) | AES-GCM envelopes, authenticated record identity, bounded nonce/write sessions and retained-key reads |
| [#64](https://github.com/mani8785/HomeVault/issues/64) | [#90](https://github.com/mani8785/HomeVault/pull/90) | Windows DPAPI CurrentUser custody and verified portable recovery exports |
| [#65](https://github.com/mani8785/HomeVault/issues/65) | [#91](https://github.com/mani8785/HomeVault/pull/91) | Durable Sensitive attributes with current Vault authorization and explicit host unlock |
| [#66](https://github.com/mani8785/HomeVault/issues/66) | [#92](https://github.com/mani8785/HomeVault/pull/92) | Offline rotation, authenticated database/key sets and staged recovery with session invalidation |

[PR #92's verification run](https://github.com/mani8785/HomeVault/actions/runs/37934900160)
passed on commit `899dfeda5a89b60d396d46f6d8dd98dfab4f1482`:

- Windows: **781 executed tests**, including complete database/key recovery under
  a disposable second account, fresh login, old-session/credential rejection and
  permitted/denied Sensitive reads.
- Linux: **732 executed tests**, including portable formats, rotation core and
  unsupported-platform behavior. Windows-only coverage is established by the
  separate Windows job, not by the Linux total.
- Restore, formatting verification, Release build with warnings as errors and
  repository/documentation-link checks passed.

The owner merged PR #92 on 2026-10-09. The specific gate requiring #66's tests and
review before using real Sensitive values is therefore satisfied for the accepted
local Windows scope. This is not approval for public hosting, a whole-database
confidentiality claim or permission to omit provisioning and recovery rehearsals.

## Operational entry points and retained limits

- [Sensitive attributes](hv-21-sensitive-attributes.md): explicit schema upgrade,
  host unlock, HTTP operations, role policy and current-access checks.
- [Key custody and recovery](hv-21-key-recovery.md): provisioning, hidden secret
  input, export verification and key-only recovery.
- [Rotation and coordinated recovery](hv-21-rotation-recovery.md): complete terminal
  build/test/run steps, backup creation, recovery and deliberate activation.

Only Sensitive attribute text receives this field encryption. Ordinary values,
names, IDs, memberships, Evidence and Reminder text remain visible as documented.
The protection does not cover malware or administrators in an unlocked process,
deletion, or replay of an older valid backup. Managed plaintext strings cannot
promise secure erasure. Losing both usable custody and recovery material can
permanently lose the protected data.

Data keys and the separate random recovery secret must remain outside the
database and checkout. Keep old keys while retained backups need them; no automatic
retirement is implemented. Maintenance is offline and Windows-only, recovery
requires the current exact schema, and historical memberships need review before
activation. No file/document encryption or public deployment is implied.

## Next implementation candidate

[HV-23 / #27](https://github.com/mani8785/HomeVault/issues/27) is the independently
replaceable Angular frontend accepted in [ADR-0019](ADRs/0019-browser-ui-first-journey.md).
Its authentication dependency, [HV-22 / #26](https://github.com/mani8785/HomeVault/issues/26),
is closed. UI implementation remains parked in Todo until explicitly resumed.
The accepted first journey is create Vault, add Asset and reopen its detail URL,
using the implemented account/session mechanism rather than the earlier fictional
prototype. Hosting remains a separate decision in #28.
