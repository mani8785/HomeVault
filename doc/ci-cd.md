# Phase 0: Git, CI, and review policy

The public repository is `mani8785/HomeVault`. The initial phase 0 changes were
merged into `main` by the owner in PR #1. HV-01 verifies the delivered CI and
activates the review policy for subsequent pull requests.

## Continuous integration

The `CI` workflow runs on pull requests targeting `main`, pushes to `main` and
phase branches, manual dispatch, and nightly at 03:17 in `Europe/Copenhagen`. Its required check is `Validate`.
It uses a read-only token, cancels superseded runs, and has a 15-minute timeout.

CI checks required files and relative Markdown links. The approved HV-03 scaffold now provides one root HomeVault.slnx and global.json, enabling:

1. SDK setup from `global.json` and dependency restore.
2. Formatting verification.
3. Release build with warnings treated as errors.
4. Tests with TRX reports; zero executed tests fails the check.
5. Test report upload, including on failure when reports exist.

The scaffold uses the NUnit framework, test SDK and adapter versions recorded in ADR-0001, with the existing VSTest/TRX commands. Review compatibility if adopting another test runner.
Code without a root solution fails repository validation.

CI checks live directly in the GitHub Actions workflow. There are no standalone
helper scripts. Creating scripts requires prior explicit user approval with a
clear reason; see [agent instructions](../AGENTS.md).

Run the standard commands directly from the solution directory:

```powershell
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build --configuration Release --no-restore -warnaserror
dotnet test --configuration Release --no-build --no-restore --logger trx --results-directory TestResults
```

Confirm tests actually execute. CI independently rejects missing or zero-test
reports and uses a fresh results directory. Also review relative Markdown links and run `git diff --check` for documentation changes.
CI repository validation examines tracked files.

Dependabot checks GitHub Actions and NuGet packages weekly.

## Delivery

Successful pushes to `main` and nightly runs upload a source ZIP identified by commit SHA,
retained for 14 days. This is the phase 0 delivery artifact, not a deployable app.
Application publishing and deployment require an application and hosting target.
Add them in a later approved phase with production approval and secrets stored
in GitHub environments.

## Enforced review policy

Actions runs checks; GitHub branch protection enforces reviews and merging.
The [branch protection payload](../.github/branch-protection.json) specifies:

- Pull requests required, with zero mandatory approving reviews in solo-owner mode.
- Latest-push approval and stale-review dismissal disabled in solo-owner mode.
- `Validate` passing with the branch up to date with `main`.
- Review conversations resolved, including for administrators.
- No force pushes or deletion of `main`.

GitHub does not allow PR authors to submit an approving review on their own PR.
The owner can review and merge once CI and the remaining protections pass.
Agents still need explicit owner approval in the task or PR before merging;
GitHub does not enforce that conversational approval. A policy-change request
alone is not merge approval. Revisit required reviewers when collaborators join.
CODEOWNERS awaits actual maintainer assignments.

The JSON does not activate protection by itself. After pushing both branches and
running CI once, an admin can apply it with authenticated GitHub CLI:

```powershell
gh api --method PUT repos/mani8785/HomeVault/branches/main/protection --input .github/branch-protection.json
```

For an existing repository, inspect and reconcile protection first: this PUT
replaces the configured policy. See [GitHub protected branches](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-protected-branches/about-protected-branches).

## HV-01 verification (2026-09-23)

- [PR #1](https://github.com/mani8785/HomeVault/pull/1) was merged by `mani8785`
  at 14:18:04 UTC, producing commit `17f30d74e97085647a5b32c51775b9de90f130a5`.
  GitHub records no approving reviews on that PR; branch protection was not active
  at the time. This records the existing merge, not a retrospective review approval.
- [The main-branch CI run](https://github.com/mani8785/HomeVault/actions/runs/35873169307)
  passed its `Validate` job. Repository validation, source packaging, and artifact
  upload succeeded. SDK setup, build, and NUnit tests were correctly skipped
  because application scaffolding does not exist yet.
- The run produced `HomeVault-source-17f30d74e97085647a5b32c51775b9de90f130a5`,
  a source artifact with 14-day retention. Deployment remains deferred.
- Initially applied the two-person protection payload and verified the response:
  required `Validate` check, strict up-to-date requirement, one approval, dismissal
  of stale approvals, approval of the latest push, resolved conversations, and
  administrator enforcement. Force pushes and branch deletion are disabled.
- The owner then explicitly requested a solo-owner policy because `mani8785` is
  the only collaborator. Required approving reviews are now zero; latest-push
  approval and stale-review dismissal are disabled. Pull requests, passing CI,
  up-to-date branches, resolved conversations, and administrator enforcement
  remain required. Force pushes and branch deletion remain disabled.

The HV-01 follow-up remains subject to user review and approval. No next-phase
implementation or merge is authorized by this verification record. GitHub settings
can change; recheck server-side protection when relying on it later.

## Nightly validation

The existing CI workflow runs daily at 03:17 Copenhagen time using GitHub's
schedule timezone setting. Runs use the latest default-branch commit; the schedule
activates only after this workflow change is merged into the default branch.
GitHub can delay scheduled runs, so this is a target time rather than a guarantee.

Concurrency is separated by event type so a push cannot cancel a scheduled run.
Nightly runs use the same repository validation and, when the solution exists,
restore, formatting, Release build, and NUnit test steps. Before HV-03 scaffolding,
application build and tests were explicitly skipped. Successful nightly runs also
upload the source ZIP with 14-day retention. No application deployment is added.

Inspect scheduled runs under Actions > CI and check the run date as well as its
conclusion. Manual dispatch exercises the validation jobs, but does not test the
schedule trigger or upload the source ZIP. Confirm the first scheduled run after
merge to verify scheduling and nightly artifact delivery end to end.

Email notifications are configured in the owner's GitHub notification settings;
this change does not enable email or add a mail-sending step. No scripts are added.

## HV-03 scaffold validation

The five-project solution targets net10.0 with SDK 10.0.300 pinned exactly. Initial NUnit tests inspect source project files to enforce the accepted dependency boundaries, Domain framework independence, and solution coverage. They require a source checkout. Domain use-case behavior is intentionally deferred to later approved issues. The Playground currently reports scaffold readiness. Production projects generate XML documentation with compiler warnings treated as errors; test APIs do not require XML comments.
