# Phase 0: Git, CI, and review policy

Git starts on `main` with the existing design documents. Phase 0 changes live on
`phase-0/ci-cd` until reviewed and explicitly approved. The requested remote is
public repository `mani8785/HomeVault`.

## Continuous integration

The `CI` workflow runs on pull requests targeting `main`, pushes to `main` and
phase branches, and manual dispatch. Its required check is `Validate`.
It uses a read-only token, cancels superseded runs, and has a 15-minute timeout.

Today it checks required files and relative Markdown file links. No application
or tests exist yet; the run explicitly reports this instead of claiming tests passed.
Once scaffolding adds one root `.sln` or `.slnx` and an agreed `global.json`, it runs:

1. SDK setup from `global.json` and dependency restore.
2. Formatting verification.
3. Release build with warnings treated as errors.
4. Tests with TRX reports; zero executed tests fails the check.
5. Test report upload, including on failure when reports exist.

Scaffolding must add NUnit tests, the test SDK and compatible adapter for the
VSTest/TRX commands used here. Review compatibility if adopting another test runner.
Code without a root solution fails repository validation.

CI checks live directly in the GitHub Actions workflow. There are no standalone
helper scripts. Creating scripts requires prior explicit user approval with a
clear reason; see [agent instructions](../AGENTS.md).

Once the root solution exists, run the standard commands directly from its directory:

```powershell
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build --configuration Release --no-restore -warnaserror
dotnet test --configuration Release --no-build --no-restore --logger trx --results-directory TestResults
```

Confirm tests actually execute. CI independently rejects missing or zero-test
reports and uses a fresh results directory. During the documentation-only phase,
review relative Markdown links and run `git diff --check`; no .NET tests exist yet.
CI repository validation examines tracked files.

Dependabot checks GitHub Actions weekly. Add NuGet updates when projects exist.

## Delivery

Successful pushes to `main` upload a source ZIP identified by commit SHA,
retained for 14 days. This is the phase 0 delivery artifact, not a deployable app.
Application publishing and deployment require an application and hosting target.
Add them in a later approved phase with production approval and secrets stored
in GitHub environments.

## Enforced review policy

Actions runs checks; GitHub branch protection enforces reviews and merging.
The [branch protection payload](../.github/branch-protection.json) specifies:

- At least one approving review from another contributor.
- Stale approvals dismissed and approval of the latest push required.
- `Validate` passing with the branch up to date with `main`.
- Review conversations resolved, including for administrators.
- No force pushes or deletion of `main`.

A solo author cannot approve their own pull request; a second reviewer with the
appropriate access is needed. CODEOWNERS awaits actual maintainer assignments.
Explicit phase approval remains required by the project workflow.

The JSON does not activate protection by itself. After pushing both branches and
running CI once, an admin can apply it with authenticated GitHub CLI:

```powershell
gh api --method PUT repos/mani8785/HomeVault/branches/main/protection --input .github/branch-protection.json
```

For an existing repository, inspect and reconcile protection first: this PUT
replaces the configured policy. See [GitHub protected branches](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-protected-branches/about-protected-branches).

Remote publication, GitHub CI execution, and protection activation must be
verified separately from the local configuration.
