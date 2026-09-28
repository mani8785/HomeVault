using HomeVault.Domain.Assets;

var result = Asset.Create(Guid.Parse("74128a99-4eb5-4b75-8ad1-6bf2d2c8453d"), "Example bicycle");
Console.WriteLine($"Valid Asset creation: {result.IsSuccess}");

var invalidResult = Asset.Create(Guid.Empty, "Example bicycle");
Console.WriteLine($"Invalid Asset creation: {invalidResult.Error}");
Console.WriteLine("Domain-only demonstration: nothing is persisted and Vault access is not implemented.");
