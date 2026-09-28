# HomeVault

A Personal Knowledge & Asset Manager built incrementally using domain-first DDD-lite.

Start with the [documentation entry point](doc/README.md). The approved .NET 10 solution contains Domain, Application, Infrastructure, Playground, and NUnit Tests projects. The first domain operation creates an Asset with a validated identity and name.

Install SDK 10.0.300, then run from the repository root:

```powershell
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build --configuration Release --no-restore -warnaserror
dotnet test --configuration Release --no-build --no-restore --logger trx --results-directory TestResults
dotnet run --project src/HomeVault.Playground --configuration Release --no-build
```

Tests cover Asset creation success/failure behavior, project dependency boundaries, and solution coverage. Run them from a source checkout; they inspect project files. Playground demonstrates valid Asset creation and rejection of an empty identity; persistence and Vault access are not implemented. See the [HV-05 implementation record](doc/hv-05-domain-building-blocks.md). See the [CI/CD and review policy](doc/ci-cd.md) for validation and delivery requirements.

Agent guidance: [AGENTS.md](AGENTS.md).
