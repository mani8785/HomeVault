using HomeVault.Domain.Assets;

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
Console.WriteLine("Domain-only demonstration: nothing is persisted and Vault access is not implemented.");
