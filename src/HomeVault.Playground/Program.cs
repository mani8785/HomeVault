using HomeVault.Application.Vaults;
using HomeVault.Domain.Assets;
using HomeVault.Domain.Relationships;
using HomeVault.Domain.Reminders;
using HomeVault.Domain.Vaults;
using HomeVault.Playground;
using HomeVault.Infrastructure.Vaults;
using HomeVault.Infrastructure;
using HomeVault.Infrastructure.Assets;
using HomeVault.Application.Assets;

if (args.Length > 0)
{
    if (args[0] == "session-keys")
    {
        Environment.ExitCode = SessionKeyJourney.Run(args);
        return;
    }
    if (args[0] != "storage")
    {
        Console.Error.WriteLine("Unknown command.");
        Environment.ExitCode = 2;
        return;
    }
    Environment.ExitCode = await DurableJourney.RunAsync(args);
    return;
}

Console.WriteLine("Standalone domain examples (not stored).");
var result = Asset.Create(Guid.Parse("74128a99-4eb5-4b75-8ad1-6bf2d2c8453d"), "Example bicycle");
Console.WriteLine($"Valid Asset creation: {result.IsSuccess}");
if (result.Asset is { } asset)
{
    // Only fictional example data is displayed; real Asset names may be sensitive.
    Console.WriteLine($"Asset identity: {asset.Id}");
    Console.WriteLine($"Asset name: {asset.Name}");
    Console.WriteLine($"Add attribute: {asset.AddAttribute("Material", "Steel", AttributeSensitivity.Ordinary)}");
    Console.WriteLine($"Duplicate attribute: {asset.AddAttribute(" material ", "Wood", AttributeSensitivity.Ordinary)}");
    Console.WriteLine($"Change attribute: {asset.ChangeAttribute("MATERIAL", "Aluminium")}");
    Console.WriteLine($"Example attribute: {asset.Attributes.Single().Name} = {asset.Attributes.Single().Value}");
    Console.WriteLine($"Remove attribute: {asset.RemoveAttribute("Material")}");
    asset.AddAttribute("Private note", "Fictional demonstration text", AttributeSensitivity.Sensitive);
    var privateNote = asset.Attributes.Single();
    Console.WriteLine($"Sensitive attribute: {privateNote.IsSensitive}; value: {privateNote.Value ?? "[redacted]"}");
    var evidenceId = Guid.NewGuid();
    Console.WriteLine($"Add URL evidence: {asset.AddEvidence(evidenceId, "Example receipt", EvidenceKind.Url, "https://example.invalid/receipt")}");
    Console.WriteLine($"Evidence kind: {asset.Evidence.Single().Kind}");
    Console.WriteLine($"Remove evidence: {asset.RemoveEvidence(evidenceId)}");
    Console.WriteLine($"Add note evidence: {asset.AddEvidence(Guid.NewGuid(), "Example note", EvidenceKind.Note, "Fictional supporting text")}");
}

var invalidResult = Asset.Create(Guid.Empty, "Example bicycle");
Console.WriteLine($"Invalid Asset creation: {invalidResult.Error}");
var blankNameResult = Asset.Create(Guid.Parse("74128a99-4eb5-4b75-8ad1-6bf2d2c8453d"), " ");
Console.WriteLine($"Blank Asset name: {blankNameResult.Error}");
var vaultResult = Vault.Create(Guid.NewGuid(), "Example household", VaultType.Household, Guid.NewGuid());
Console.WriteLine($"Valid Vault creation: {vaultResult.IsSuccess}");
if (vaultResult.Vault is { } vault)
{
    Console.WriteLine($"Vault state: {vault.Status}; initial role: {vault.Memberships.Single().Role}");
    var memberId = Guid.NewGuid();
    Console.WriteLine($"Add member: {vault.AddMember(memberId, VaultRole.Editor)}");
    Console.WriteLine($"Change member role: {vault.ChangeMemberRole(memberId, VaultRole.Viewer)}");
    Console.WriteLine($"Remove member: {vault.RemoveMember(memberId)}");
    Console.WriteLine($"Remove last Owner: {vault.RemoveMember(vault.Memberships.Single().ActorId)}");
    Console.WriteLine($"Archive Vault: {vault.Archive(vault.Memberships.Single().ActorId)}");
    Console.WriteLine($"Vault state after archive: {vault.Status}");
    Console.WriteLine($"Add member after archive: {vault.AddMember(memberId, VaultRole.Editor)}");
}

var invalidVault = Vault.Create(Guid.NewGuid(), "Example household", VaultType.Household, Guid.Empty);
Console.WriteLine($"Invalid Vault creation: {invalidVault.Error}");
// Standalone fictional references; no endpoint existence or ownership lookup is implied.
var relationshipResult = Relationship.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), RelationshipKind.Covers);
Console.WriteLine($"Valid Relationship creation: {relationshipResult.IsSuccess}");
if (relationshipResult.Relationship is { } relationship)
{
    Console.WriteLine($"Relationship kind: {relationship.Kind}; state: {relationship.Status}");
    relationship.Remove();
    relationship.Remove();
    Console.WriteLine($"Relationship state after removal: {relationship.Status}");
}
Console.WriteLine("Demonstration only: storage is in memory; real authentication/Vault access is not implemented.");
var exampleDue = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
var reminderResult = Reminder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Example insurance review", exampleDue);
Console.WriteLine($"Valid Reminder creation: {reminderResult.IsSuccess}");
if (reminderResult.Reminder is { } reminder)
{
    Console.WriteLine($"Update Reminder: {reminder.Update("Updated example review", exampleDue)}");
    Console.WriteLine($"Reminder overdue at supplied later instant: {reminder.IsOverdue(exampleDue.AddMinutes(1))}");
    Console.WriteLine($"Complete Reminder: {reminder.Complete()}; state: {reminder.Status}");
    Console.WriteLine($"Update completed Reminder: {reminder.Update("Another example", exampleDue)}");
}

Console.WriteLine("Connected journey: create a Vault, then register an Asset in it.");
var store = new InMemoryHomeVaultStore();
var repository = new InMemoryVaultRepository(store);
var currentActor = new ExampleCurrentActor(Guid.NewGuid());
var createVault = new CreateVaultUseCase(currentActor, repository);
var request = new CreateVaultRequest(Guid.NewGuid(), "Example application Vault", VaultType.Personal);
var applicationResult = await createVault.ExecuteAsync(request);
Console.WriteLine($"Application Vault creation: {applicationResult.IsSuccess}; state: {applicationResult.Vault?.Status}");
var duplicateVault = await createVault.ExecuteAsync(request);
Console.WriteLine($"Application duplicate identity: {duplicateVault.Error}");
var invalidVaultName = await createVault.ExecuteAsync(new CreateVaultRequest(Guid.NewGuid(), " ", VaultType.Personal));
Console.WriteLine($"Application invalid name: {invalidVaultName.Error}");
var anonymousCreateVault = new CreateVaultUseCase(new ExampleCurrentActor(null), repository);
var anonymousVault = await anonymousCreateVault.ExecuteAsync(new CreateVaultRequest(Guid.NewGuid(), "Example", VaultType.Personal));
Console.WriteLine($"Application missing actor: {anonymousVault.Error}");
if (!applicationResult.IsSuccess || applicationResult.Vault!.Status != VaultStatus.Active ||
    duplicateVault.Error != CreateVaultError.IdentityConflict || duplicateVault.Vault is not null ||
    invalidVaultName.Error != CreateVaultError.BlankName || invalidVaultName.Vault is not null ||
    anonymousVault.Error != CreateVaultError.Unauthenticated || anonymousVault.Vault is not null)
    throw new InvalidOperationException("Unexpected Vault journey outcome.");
var registerAsset = new RegisterAssetUseCase(currentActor, new InMemoryAssetRegistrationStore(store));
var assetRequest = new RegisterAssetRequest(Guid.NewGuid(), applicationResult.Vault!.Id, "Example bicycle");
var registration = await registerAsset.ExecuteAsync(assetRequest);
Console.WriteLine($"Vault-bound Asset registration: {registration.IsSuccess}");
if (!registration.IsSuccess || registration.Asset!.VaultId != applicationResult.Vault.Id)
    throw new InvalidOperationException("Expected registration in the created Vault.");
await ExpectRegistration(assetRequest, RegisterAssetError.IdentityConflict);
await ExpectRegistration(new RegisterAssetRequest(Guid.NewGuid(), request.Id, " "), RegisterAssetError.BlankName);
await ExpectRegistration(new RegisterAssetRequest(Guid.NewGuid(), Guid.NewGuid(), "Example"), RegisterAssetError.VaultUnavailable);
var anonymousRegistration = await new RegisterAssetUseCase(new ExampleCurrentActor(null), new InMemoryAssetRegistrationStore(store))
    .ExecuteAsync(assetRequest);
Console.WriteLine($"Asset missing actor: {anonymousRegistration.Error}");
if (anonymousRegistration.Error != RegisterAssetError.Unauthenticated)
    throw new InvalidOperationException("Expected missing actor rejection.");
Console.WriteLine("Connected journey checks passed; storage lasts only for this process.");

async Task ExpectRegistration(RegisterAssetRequest proposed, RegisterAssetError expected)
{
    var actual = await registerAsset.ExecuteAsync(proposed);
    Console.WriteLine($"Asset registration outcome: {actual.Error}");
    if (actual.Error != expected || actual.Asset is not null)
        throw new InvalidOperationException("Unexpected registration outcome.");
}
