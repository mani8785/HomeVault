# HomeVault agent instructions

These instructions apply throughout this repository. Read the project documents
before changing behavior; explicit user instructions take precedence.

## Project goal and source documents

HomeVault is a personal knowledge and asset manager for people, households, and
organizations. Track anything valuable or important: physical assets, documents,
vehicles, property, digital accounts, licenses, insurance, bank accounts, and
person records. Build incrementally around domain behavior and use cases.

Read these sources for the current requirements and decision status:

- [Documentation entry point](doc/README.md)
- [Context, vocabulary, and roadmap](doc/context.md)
- [Architecture and dependency direction](doc/architecture.md)
- [Coding, XML documentation, and review workflow](doc/workflow.md)
- [ADR process and decision index](doc/ADRs/README.md)
- [CI/CD and review policy](doc/ci-cd.md)

Use the established terms Asset, Vault, Asset attribute, Relationship, Evidence,
and Reminder. A Vault defines ownership and access; do not assume it loads every
Asset as an aggregate child. Revisit historical Vault rules during implementation.
The old project's reported completed phases are context, not code in this restart.

## Decision and implementation boundaries

At phase 0 the repository contains documentation and CI configuration; solution
scaffolding is still a separate approval step. NUnit is confirmed in ADR-0001.
The remaining C#/.NET 10 stack proposals and ADR-0002 project boundaries await
confirmation. Selective dependency injection in ADR-0003 is accepted.
Check the ADRs for updates instead of treating this snapshot as permanent.

Discuss architectural choices with the user and record them as Proposed ADRs
before implementation. Mark them Accepted only after explicit confirmation.
Preserve accepted decision history; use a new superseding ADR when it changes.
Do not choose a database, ORM, UI, hosting target, authentication provider,
DI container, or event-dispatch library merely because an older plan mentioned it.

## Coding policy

- Keep changes small, coherent, and within the approved step. Avoid unrelated
  refactors, speculative abstractions, and dependencies without a concrete need.
- Preserve Guid identifiers, manual validation, lightweight domain events, and
  project-owned domain/result patterns. Favor domain behavior over generic CRUD.
- Keep Domain independent of UI, persistence, DI frameworks, and other frameworks;
  .NET base libraries are allowed. Encryption and storage belong in Infrastructure.
- Follow the proposed dependency direction once approved: Application references
  Domain; Infrastructure references Application/Domain; Playground composes them;
  initial tests reference Domain/Application. Never introduce reverse dependencies.
- Use constructor injection where explicit collaborators improve clarity or tests.
  Add interfaces only for a concrete boundary or substitution need. Application
  owns technical contracts and Infrastructure implements them. Prefer manual wiring
  in Playground; do not use service locators or IServiceProvider in Domain/Application.
  Construct entities and value objects through domain constructors or factories.
- Give public types and members meaningful XML documentation. Include param,
  returns, value, exception, and remarks tags where relevant. Explain invariants,
  failures, side effects, and sensitive-value handling. Do not invent exceptions
  or merely repeat names. Private helpers and test names need no redundant comments.
- Never expose secrets or sensitive values in examples, logs, or failure output.
- Follow .editorconfig and existing conventions. Use NUnit for behavioral tests
  when application code exists; review package and adapter choices at scaffolding.

## Scripts require explicit approval

Do not create or add a script without first asking the user and receiving explicit
approval. This includes helper scripts, generated scripts, temporary script files,
and wrappers in any language or location. Explain the specific need, what it will
contain, why existing tools or direct commands are insufficient, and the maintenance
cost. Wait for approval or rejection; silence is not approval.

Prefer existing tools and direct commands. Keep necessary CI commands in the
existing GitHub Actions workflow instead of adding standalone script files.
Do not use lengthy inline automation as a workaround for the approval requirement;
ask before introducing substantial new scripted automation. The phase 0 checks
moved into the existing workflow are the baseline, not blanket approval for more.

## Workflow and validation

1. State the current state, goal, and a small plan for the requested step.
2. Work on a separate branch per phase; preserve unrelated user changes.
3. Update affected documentation and ADRs with the implementation.
4. When a solution exists, run restore, formatting verification, Release build,
   and NUnit tests as appropriate, using the direct commands in doc/ci-cd.md.
   Review XML comments alongside behavior. Do not claim tests passed when there
   are no tests, no executed tests, or checks were not run.
5. For documentation/CI changes, validate links, workflow syntax, and the diff.
   Report the actual checks run, failures, and limitations.
6. Present the completed step for review. Do not merge or start the next step
   without explicit user approval. GitHub checks do not replace phase approval.

The GitHub review policy requires passing Validate checks, an up-to-date branch,
and resolved conversations. In solo-owner mode, no GitHub approving review is
required; explicit owner approval in the task or PR is still required before an
agent merges. Policy changes alone do not authorize a merge. Verify server-side
protection before claiming it is active; the local JSON alone does not enforce it.
