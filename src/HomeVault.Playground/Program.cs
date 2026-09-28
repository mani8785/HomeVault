using HomeVault.Domain.Assets;
using HomeVault.Domain.Vaults;

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
Console.WriteLine("Domain-only demonstration: nothing is persisted and Vault access is not implemented.");
