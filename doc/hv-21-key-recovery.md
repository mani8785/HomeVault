# Windows key custody and recovery (#64)

HomeVault can explicitly initialize a DPAPI CurrentUser key ring, verify an
encrypted portable export, and restore that export under another Windows user.
The commands do not enable Sensitive attributes, change a database, or configure
the API. See [Sensitive integration](hv-21-sensitive-attributes.md) for #65 and
[coordinated database recovery](hv-21-rotation-recovery.md) for #66. A key-only
recovery is not a verified application restore.
See accepted [ADR-0028](ADRs/0028-windows-key-custody-recovery.md).

## Terminal validation

From the repository root, run each command and check its exit code before continuing:

```powershell
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build --configuration Release --no-restore -warnaserror
dotnet test --configuration Release --no-build --no-restore
```

Windows CI also creates a disposable local account, loads its real profile,
proves that it cannot decrypt the original user's DPAPI blob, and restores a
fictional export into its own private ring to decrypt a fictional record. No
account is created by local test runs. The test reports that the second-profile
portion is CI-only. Linux exercises the portable format and unsupported-platform
outcomes; Windows-only integration cases are skipped there.

## Initialize

Use an interactive Windows terminal without redirected input/output. Generate
exactly 32 random bytes represented by 64 hex characters in a cryptographically
secure password manager and store them there. Do not use a human password, put
the key in a command, environment variable, file, transcript or source control.
HomeVault validates syntax, not randomness. The hidden prompt requires manual
entry and asks twice for initialization. Escape cancels; Backspace corrects input.

Choose an existing export directory outside any checkout and separately from
your database and ring. Configure its ACL for only the current Windows user.
For example, these direct PowerShell commands create a new private directory
(replace the nonsecret location; its parent must exist):

```powershell
$exportDirectory = 'D:\HomeVaultRecovery'
$owner = [Security.Principal.WindowsIdentity]::GetCurrent().User
$acl = [Security.AccessControl.DirectorySecurity]::new()
$acl.SetOwner($owner)
$acl.SetAccessRuleProtection($true, $false)
$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($owner, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
[IO.DirectoryInfo]::new($exportDirectory).Create($acl)
```

Then choose a new ring path with an existing parent. Example after a successful
Release build (replace paths, never supply the recovery secret as an argument):

```powershell
dotnet run --project src/HomeVault.Playground --configuration Release --no-build -- encryption-keys initialize 'C:\Users\YOUR_USER\HomeVaultKeys' 'D:\HomeVaultRecovery'
```

Success prints `Key operation verified`. The ring contains DPAPI-protected
generation files, a local encrypted export, an exclusive writer lock file and
an atomic `current` pointer. The export directory contains `.hvkr` files. Keep
the export and separately stored recovery secret available; both are necessary
for portable recovery. Initialization does not issue a data-writing key.

## Verify or recover

Select the export that corresponds to the current generation, using the filename's
generation and `current` pointer. Older exports intentionally fail current-ring
equivalence after new write keys have been published.

```powershell
dotnet run --project src/HomeVault.Playground --configuration Release --no-build -- encryption-keys verify 'C:\Users\YOUR_USER\HomeVaultKeys' 'D:\HomeVaultRecovery\YOUR_EXPORT.hvkr'
dotnet run --project src/HomeVault.Playground --configuration Release --no-build -- encryption-keys recover 'D:\HomeVaultRecovery\YOUR_EXPORT.hvkr' 'C:\Users\YOUR_USER\HomeVaultRestoredKeys'
```

On another Windows computer/profile, transfer the encrypted export into an
owner-only directory for the destination user; its file must also have protected,
owner-only permissions. Enter the separately held secret at the hidden prompt.
Recovery verifies all keys, rewraps them for the current user, and checks a
purpose-derived synthetic authentication record. It never overwrites an existing
destination or enables writes with a retained key. Actual encrypted database
record recovery must be implemented and tested during #65/#66.

## Failure and custody behavior

Invalid input, wrong secrets, missing/corrupt files, foreign ownership, broad ACLs
and reparse points fail safely. No permissions or damaged ring are silently
repaired. Non-Windows platforms return an explicit unsupported result. All
operator failures omit input values and exception details.

An internal unlocked custody instance owns a copy of the recovery secret only in
memory. Under an OS file lock, each write session creates a fresh data key,
publishes and verifies the protected generation and encrypted export, then moves
the pointer. Concurrent adapters reload the last committed generation. Historical
keys remain readable; the 4,096-key bound refuses further sessions without deletion.

An interrupted attempt may leave private staging directories or orphan generation
and export files. Preserve these and the last known-good export for inspection;
there is no automatic pruning or rollback. Before pointer replacement, the previous
generation remains usable. After replacement, a failed session issuance leaves an
unused retained key. Filesystem checks cannot protect against an administrator or
the same compromised Windows account. The database-location separation check
belongs to #65's composition because these commands do not accept a database path.

The console parser is linked into the Infrastructure test assembly to exercise
the actual input code without adding a reverse project dependency on Playground.
Owned character/key buffers are cleared; this does not guarantee perfect erasure
of runtime, console or operating-system memory.
