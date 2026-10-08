using Microsoft.EntityFrameworkCore;

namespace HomeVault.Infrastructure.Persistence;

internal static class AttributeMetadata
{
    internal sealed record Entry(Guid? Id, string Name, int Sensitivity);
    internal static async Task<List<Entry>> Load(HomeVaultDbContext context, Guid assetId, CancellationToken token)
    {
        var ordinary = await context.AssetAttributes.AsNoTracking().Where(row => row.AssetId == assetId)
            .Select(row => new Entry(null, row.Name, row.Sensitivity)).ToListAsync(token);
        var sensitive = await context.SensitiveAttributes.AsNoTracking().Where(row => row.AssetId == assetId)
            .Select(row => new Entry(row.Id, row.Name, row.Sensitivity)).ToListAsync(token);
        if (ordinary.Any(row => row.Sensitivity != 0) || sensitive.Any(row => row.Sensitivity != 1 || row.Id == Guid.Empty))
            throw new InvalidOperationException("Invalid stored attribute metadata.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var all = ordinary.Concat(sensitive).ToList();
        if (all.Any(row => string.IsNullOrWhiteSpace(row.Name) || row.Name != row.Name.Trim() || !names.Add(row.Name)))
            throw new InvalidOperationException("Invalid stored attribute metadata.");
        return all;
    }
}
