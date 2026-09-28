using HomeVault.Domain.Assets;

var result = Asset.Create(Guid.Parse("74128a99-4eb5-4b75-8ad1-6bf2d2c8453d"), "Example bicycle");
Console.WriteLine($"Valid Asset creation: {result.IsSuccess}");
if (result.Asset is { } asset)
{
    // Only fictional example data is displayed; real Asset names may be sensitive.
    Console.WriteLine($"Asset identity: {asset.Id}");
    Console.WriteLine($"Asset name: {asset.Name}");
}

var invalidResult = Asset.Create(Guid.Empty, "Example bicycle");
Console.WriteLine($"Invalid Asset creation: {invalidResult.Error}");
var blankNameResult = Asset.Create(Guid.Parse("74128a99-4eb5-4b75-8ad1-6bf2d2c8453d"), " ");
Console.WriteLine($"Blank Asset name: {blankNameResult.Error}");
Console.WriteLine("Domain-only demonstration: nothing is persisted and Vault access is not implemented.");
