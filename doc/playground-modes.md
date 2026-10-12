# Playground storage modes (#61)

Normal startup uses SQLite under accepted ADR-0017. Supply `--database` with a
fully qualified file path, or set `HOMEVAULT_PLAYGROUND_DATABASE` for no-argument
startup. The command-line path takes precedence. Missing configuration exits 2;
missing/incompatible storage or unavailable demo records exit 1, without falling
back to memory. Diagnostics do not print paths or record values.

This is a fictional demonstration, not a browser for arbitrary user databases.
Keep its database separate from the account/API database. The demonstration uses
fixed actor, Vault and Asset identifiers. In a SQLite viewer, its records are in
`Vaults`, `Memberships`, and `Assets`. It does not create a real sign-in account.

## First setup and repeatable startup (PowerShell)

After a successful Release build, choose a new local directory outside the checkout
and outside cloud-synced folders. Substitute your chosen path below:

```powershell
New-Item -ItemType Directory -Path C:\HomeVaultDemo
$demoDatabase = 'C:\HomeVaultDemo\homevault.db'
dotnet run --project src/HomeVault.Playground -c Release --no-build -- storage migrate $demoDatabase
dotnet run --project src/HomeVault.Playground -c Release --no-build -- storage create $demoDatabase
dotnet run --project src/HomeVault.Playground -c Release --no-build -- --database $demoDatabase
$env:HOMEVAULT_PLAYGROUND_DATABASE = $demoDatabase
dotnet run --project src/HomeVault.Playground -c Release --no-build
```

Normal startup only inspects persisted records and verifies nonmember isolation.
It neither migrates nor seeds. Repeating it creates no duplicate records and does
not overwrite existing values. `storage create` remains an explicit one-time seed
command: rerunning it reports an identity conflict safely rather than overwriting.
Existing storage migrate/create/read/backup/restore and key commands remain valid.
For upgrades, stop writers and follow the [backup procedure](hv-20-persistence.md).

## Temporary demonstrations

```powershell
dotnet run --project src/HomeVault.Playground -c Release --no-build -- in-memory
```

This explicitly selected mode ignores the SQLite configuration and loses all data
when the process exits. In-memory adapters remain useful for fast Application
contract tests and fictional examples. They do not replace real SQLite migration,
constraint, transaction, reload and backup integration coverage.

## Owner validation

```powershell
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore -warnaserror
dotnet test -c Release --no-build --no-restore
```

Run each command only after the previous one succeeds. Use disposable demo data:
run normal startup twice and check row counts stay unchanged; verify `in-memory`
works even with an invalid configured database; remove the environment variable
and verify no-argument startup requests configuration; try a relative path and a
nonexistent file and verify failure without creating a database. Verify explicit
`--database` overrides the environment variable. Tests and these runtime acceptance
checks are left to the owner; the agent does not execute them by default.
