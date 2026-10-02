# HV-22.4: Remaining authorized operations

Status: refinement for [#72](https://github.com/mani8785/HomeVault/issues/72),
which remains In Progress under [#26](https://github.com/mani8785/HomeVault/issues/26).
The prerequisite [#71](https://github.com/mani8785/HomeVault/issues/71) is complete
through merged [PR #75](https://github.com/mani8785/HomeVault/pull/75).

[ADR-0022](ADRs/0022-remaining-authorized-operations.md) was accepted on 2026-10-02. This document
is an implementation sequence, not a claim that the operations below exist.

## Task sequence

Tracked sub-issues: [archive #76](https://github.com/mani8785/HomeVault/issues/76),
[membership #77](https://github.com/mani8785/HomeVault/issues/77),
[ordinary attributes #78](https://github.com/mani8785/HomeVault/issues/78),
[Evidence #79](https://github.com/mani8785/HomeVault/issues/79),
[Relationships #80](https://github.com/mani8785/HomeVault/issues/80), and
[Reminders #81](https://github.com/mani8785/HomeVault/issues/81).
Archive #76 is implemented in the [archive slice](hv-22-vault-archive.md), pending
PR review. Tasks #77–#81 remain planned work under the stated review gates.

| Order | Bounded outcome | Dependencies | Required evidence |
| --- | --- | --- | --- |
| 1 | Restore stored Vault state and expose Owner-only ArchiveVault | Acceptance of ADR-0022 archive/restoration decisions | Current Owner, repeated archive, nonmember/missing equivalence, denied roles, archive versus registration with independent SQLite connections, no schema change |
| 2 | Add, change role and remove real-account Vault memberships | Task 1; acceptance of account-target policy | Full old/new-role matrix, disabled/missing targets, duplicate membership, last-Owner races, archived rejection, no account search |
| 3 | Persist and expose ordinary Asset attribute operations | Task 2; reviewed attribute schema/contract | Add/update/remove/read, snapshots, cross-Vault isolation, no Sensitive downgrade/disclosure, migration preservation |
| 4 | Persist and expose URL/Note Evidence | Task 3; reviewed Evidence schema/contract | Add/remove/read, deliberate content access, malformed URLs, isolation/archive checks, migration/backup, no file resolution |
| 5 | Persist and expose directed Relationship operations | Task 4; reviewed Relationship schema/contract | Actual same-Vault endpoints, concurrent active duplicates, removal/recreation, no endpoint deletion, migration/backup |
| 6 | Persist and expose Reminder lifecycle operations | Task 5; reviewed Reminder schema/contract | UTC round trip, update/complete/cancel, terminal-state concurrency, current permissions, no scheduling |

The sequence is for separate review PRs. It is not a claim that Evidence logically
requires attributes or Relationships require Evidence. Once shared patterns are
reviewed, independent later work can be reprioritized by the owner.

## Shared completion checklist for each slice

- [ ] Record and approve any new API/storage choices before implementing them.
- [ ] Add only the necessary Application contracts, validated domain restoration,
  Infrastructure adapter/mappings and authenticated API routes.
- [ ] Keep access/lifecycle checks and write commit atomic; prove denied writes do
  not change stored data, including race cases using independent connections.
- [ ] Test real Identity accounts/cookies, all applicable roles, antiforgery,
  current membership, inaccessible/missing equivalence and safe error payloads.
- [ ] Update the versioned OpenAPI contract and terminal journey instructions.
- [ ] Review XML documentation; restore, verify format, Release build, execute
  NUnit tests and verify current PR CI. Test schema upgrade/backup when changed.
- [ ] Leave the PR for review; do not close #72 merely because one child completes.

## Remaining parent-story boundaries

Sensitive access/encryption stays unexposed and blocked on its explicit policy and
HV-21 implementation. Account administration is not Vault ownership. File/document
resolution, notifications, UI, deployment, generic CRUD and account discovery do
not enter these slices implicitly. Before closing #26, reconcile its full acceptance
criteria against delivered operations and explicitly retain any outstanding scope.

## Validation for this planning change

Only Markdown changes are intended. Validate relative links and the diff, and
inspect the PR's existing CI results. There is no new application entry point or
migration to run. Existing build/test/run commands remain in
[CI/CD](ci-cd.md) and the [authenticated API guide](hv-22-authorized-api.md).
