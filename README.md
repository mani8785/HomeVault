# HomeVault

A Personal Knowledge & Asset Manager built incrementally using domain-first DDD-lite.

Start with the [documentation entry point](doc/README.md). The approved .NET 10 solution contains Domain, Application, Infrastructure, Playground, and NUnit Tests projects. The first domain operation creates an Asset with a validated identity and name.

Install SDK 10.0.300, then run from the repository root:

```powershell
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build --configuration Release --no-restore -warnaserror
dotnet test --configuration Release --no-build --no-restore --logger trx --results-directory TestResults
dotnet run --project src/HomeVault.Playground --configuration Release --no-build -- --database 'C:\HomeVaultDemo\homevault.db'
```

First provision a dedicated fictional demo database using the [Playground setup guide](doc/playground-modes.md). Normal Playground startup reads SQLite; use `in-memory` explicitly for temporary examples. The authenticated Angular application is a separate entry point; see the [UI guide](doc/hv-23-angular-ui.md). Tests cover domain behavior, authorization, persistence and application boundaries. Run them from a source checkout. See the [CI/CD and review policy](doc/ci-cd.md) for validation and delivery requirements.

Agent guidance: [AGENTS.md](AGENTS.md).
