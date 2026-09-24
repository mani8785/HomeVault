# Development and review workflow

1. Start each step with current state, goal, and a small concrete plan.
2. Discuss architectural choices and record them as proposed ADRs before implementation.
3. Make a coherent change using existing conventions; avoid unrelated refactors.
4. Run dotnet build and dotnet test when code exists. Report failures or checks not run honestly.
5. Review XML comments alongside behavior and tests.
6. Stop for explicit user confirmation after every step. Do not merge or start the next step before approval.

Use a separate Git branch per phase. Merge accepted, verified phases into main only after approval. Keep reviewed work and architecture decisions versioned in Git, following the [ADR process](ADRs/README.md). Git was initialized in phase 0 with the existing documents on main. Initial CI changes were merged through PR #1; main-branch protection was activated and verified in HV-01; see the [CI/CD and review policy](ci-cd.md). Solution scaffolding remains a separate confirmed step.

## XML documentation standard

Document public types and members with meaningful summaries describing their purpose and domain behavior. Include param, returns, value, exception, and remarks tags where relevant. Explain invariants, validation failures, side effects, and sensitive-value handling. Keep comments consistent with the actual API; do not invent exceptions or repeat a member name as its entire description. Test names and private helpers need no redundant XML comments.

During scaffolding, propose XML documentation generation and missing-public-documentation diagnostics for production projects. Compiler checks catch missing or malformed documentation; manual review must still check meaning and accuracy. No secret values should appear in examples, logs, or failure output.

## Review checklist

- Behavior and domain language match the agreed requirements.
- Dependency direction remains correct.
- Public API documentation is accurate and useful.
- Relevant tests and build checks pass.
- Documentation and affected ADRs are updated.
- Any genuine domain ambiguity is raised with the user.

## Script policy

Ask the user before creating any script and wait for explicit approval. Explain
its purpose, why direct commands or existing tools are insufficient, and its
maintenance cost. This applies to helper, generated, temporary, and wrapper scripts
in any language. Avoid substantial inline automation as an approval workaround.
Prefer direct tooling and necessary commands in the existing CI workflow.
See [AGENTS.md](../AGENTS.md) for the complete agent policy.
