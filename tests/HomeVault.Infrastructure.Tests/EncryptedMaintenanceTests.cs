using System.Security.Cryptography;
using System.Text;
using HomeVault.Application.Assets;
using HomeVault.Domain.Assets;
using HomeVault.Domain.Vaults;
using HomeVault.Infrastructure.Encryption;
using HomeVault.Infrastructure.Identity;
using HomeVault.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace HomeVault.Infrastructure.Tests;

public sealed class EncryptedMaintenanceTests
{
    private string _root = null!;
    private string Database => Path.Combine(_root, "data", "live.sqlite");
    private string Ring => Path.Combine(_root, "ring");
    private string Exports => Path.Combine(_root, "exports");
    private string Backup => Path.Combine(_root, "backup");
    private string Restored => Path.Combine(_root, "restored");
    private readonly byte[] _secret = RandomNumberGenerator.GetBytes(32);
    private MaintenanceData _data = null!;

    [SetUp]
    public async Task Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "HomeVault-maintenance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        if (!OperatingSystem.IsWindows()) return;
        PrivateKeyFiles.CreateDirectory(Path.GetDirectoryName(Database)!); PrivateKeyFiles.CreateDirectory(Exports);
        WindowsKeyCustody.Initialize(Ring, Exports, _secret);
        using var session = SensitiveStorageSession.OpenWindows(Ring, Exports, _secret);
        _data = await MaintenanceData.Create(Database, session);
        PrivateKeyFiles.SecureNewFile(Database);
    }
    [TearDown]
    public void Cleanup() => Directory.Delete(_root, true);

    [TestCase("protected"), TestCase("export"), TestCase("committed"), TestCase("batch-written"), TestCase("batch-committed")]
    [Platform("Win")]
    public async Task InterruptedRotationRetainsReadableBatchesAndCanRestart(string failure)
    {
        if (!OperatingSystem.IsWindows()) return;
        await AddRotationRows();
        var before = await Envelopes();
        await Assert.ThrowsAsync<InvalidOperationException>(() => EncryptedMaintenance.RotateCore(Database, Ring, Exports, _secret, default,
            stage => { if (stage == failure) throw new InvalidOperationException(); }));
        using (var custody = new WindowsKeyCustody(Ring)) await EncryptedDatabaseMaintenance.Validate(new SqliteDatabase(Database), custody, default);
        var after = await Envelopes();
        Assert.That(after.Count(pair => !pair.Value.SequenceEqual(before[pair.Key])), Is.EqualTo(failure == "batch-committed" ? 128 : 0));
        Assert.That(await EncryptedMaintenance.RotateAsync(Database, Ring, Exports, _secret), Is.EqualTo(KeyOperationOutcome.Succeeded));
        Assert.That((await Envelopes()).All(pair => !pair.Value.SequenceEqual(before[pair.Key])), Is.True);
        using var session = SensitiveStorageSession.OpenWindows(Ring, Exports, _secret);
        await _data.CheckReads(new SqliteDatabase(Database), session);
    }

    [Test, Platform("Win")]
    public async Task RotationRollsSessionsAndPreservesArchivedVaultAndOldBackup()
    {
        if (!OperatingSystem.IsWindows()) return;
        await AddRotationRows();
        Assert.That(await EncryptedMaintenance.BackupAsync(Database, Ring, Backup, _secret), Is.EqualTo(KeyOperationOutcome.Succeeded));
        var backupBytes = File.ReadAllBytes(Path.Combine(Backup, "database.sqlite"));
        var oldKeys = 0;
        using (var ring = WindowsKeyCustody.Load(Ring, out _)) oldKeys = ring.Keys.Count;
        await new SqliteVaultArchiveStore(new SqliteDatabase(Database)).ArchiveAsync(_data.Vault, _data.Owner, default);
        await EncryptedMaintenance.RotateCore(Database, Ring, Exports, _secret, default, sessionLimit: 64);
        using (var ring = WindowsKeyCustody.Load(Ring, out _)) Assert.That(ring.Keys.Count, Is.EqualTo(oldKeys + 3));
        await using (var db = new SqliteDatabase(Database).CreateContext())
            Assert.That((await db.Vaults.SingleAsync()).Status, Is.EqualTo((int)VaultStatus.Archived));
        Assert.That(File.ReadAllBytes(Path.Combine(Backup, "database.sqlite")), Is.EqualTo(backupBytes));
        Assert.That(await EncryptedMaintenance.RecoverAsync(Backup, Restored, _secret), Is.EqualTo(KeyOperationOutcome.Succeeded));
        var restoredDatabase = Path.Combine(Restored, "database", "homevault.sqlite");
        using var session = SensitiveStorageSession.OpenWindows(Path.Combine(Restored, "data-keys"), Exports, _secret);
        await _data.CheckReads(new SqliteDatabase(restoredDatabase), session);
        await _data.CheckAccounts(restoredDatabase, Path.Combine(Restored, "session-keys"));
        foreach (var file in Directory.GetFiles(Backup))
            Assert.That(File.ReadAllBytes(file).AsSpan().IndexOf(Encoding.UTF8.GetBytes(MaintenanceData.Value)), Is.EqualTo(-1));
    }

    [TestCase("backup-verified"), TestCase("recovery-copied"), TestCase("recovery-invalidated"), TestCase("recovery-verified")]
    [Platform("Win")]
    public async Task InterruptedPublicationDoesNotReplaceLiveStores(string failure)
    {
        if (!OperatingSystem.IsWindows()) return;
        var original = File.ReadAllBytes(Database); var pointer = File.ReadAllBytes(Path.Combine(Ring, "current"));
        Action<string> crash = stage => { if (stage == failure) throw new InvalidOperationException(); };
        if (failure == "backup-verified")
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => EncryptedMaintenance.BackupCore(Database, Ring, Backup, _secret, default, crash));
            Assert.That(Directory.Exists(Backup), Is.False);
        }
        else
        {
            Assert.That(await EncryptedMaintenance.BackupAsync(Database, Ring, Backup, _secret), Is.EqualTo(KeyOperationOutcome.Succeeded));
            await Assert.ThrowsAsync<InvalidOperationException>(() => EncryptedMaintenance.RecoverCore(Backup, Restored, _secret, default, crash));
            Assert.That(Directory.Exists(Restored), Is.False);
            Assert.That(await EncryptedMaintenance.RecoverAsync(Backup, Restored, _secret), Is.EqualTo(KeyOperationOutcome.Succeeded));
        }
        Assert.That(File.ReadAllBytes(Database), Is.EqualTo(original));
        Assert.That(File.ReadAllBytes(Path.Combine(Ring, "current")), Is.EqualTo(pointer));
    }

    [TestCase("secret"), TestCase("database"), TestCase("export"), TestCase("manifest"), TestCase("sidecar"), TestCase("collision")]
    [Platform("Win")]
    public async Task WrongOrDamagedRecoverySetCannotPublish(string damage)
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.That(await EncryptedMaintenance.BackupAsync(Database, Ring, Backup, _secret), Is.EqualTo(KeyOperationOutcome.Succeeded));
        var secret = _secret.ToArray();
        if (damage == "secret") secret[0] ^= 1;
        else if (damage == "collision") { Directory.CreateDirectory(Restored); File.WriteAllText(Path.Combine(Restored, "keep"), "known-good"); }
        else if (damage == "sidecar") File.WriteAllText(Path.Combine(Backup, "database.sqlite-wal"), "untrusted");
        else
        {
            var file = Path.Combine(Backup, damage switch { "database" => "database.sqlite", "export" => "keys.hvkr", _ => "manifest.hvbm" });
            var bytes = File.ReadAllBytes(file); bytes[^1] ^= 1; File.WriteAllBytes(file, bytes);
        }
        Assert.That(await EncryptedMaintenance.RecoverAsync(Backup, Restored, secret), Is.EqualTo(KeyOperationOutcome.Failed));
        if (damage == "collision") Assert.That(File.ReadAllText(Path.Combine(Restored, "keep")), Is.EqualTo("known-good"));
        else Assert.That(Directory.Exists(Restored), Is.False);
    }

    [TestCase("orphan"), TestCase("owner"), TestCase("metadata"), TestCase("schema"), TestCase("envelope"), TestCase("reminder"), TestCase("evidence"), TestCase("relationship")]
    [Platform("Win")]
    public async Task InvalidStoredStateIsRejectedBeforeBackupPublication(string damage)
    {
        if (!OperatingSystem.IsWindows()) return;
        await using (var db = new SqliteDatabase(Database).CreateContext())
        {
            switch (damage)
            {
                case "orphan": await db.Memberships.Where(row => row.ActorId == _data.Editor).ExecuteUpdateAsync(set => set.SetProperty(row => row.ActorId, Guid.NewGuid())); break;
                case "owner": await db.Memberships.ExecuteUpdateAsync(set => set.SetProperty(row => row.Role, (int)VaultRole.Viewer)); break;
                case "metadata": db.AssetAttributes.Add(new AssetAttributeRow { AssetId = _data.Asset, Name = "PRIVATE-0", Value = "ordinary" }); await db.SaveChangesAsync(); break;
                case "schema": await db.Database.ExecuteSqlRawAsync("DELETE FROM __EFMigrationsHistory WHERE MigrationId = (SELECT MAX(MigrationId) FROM __EFMigrationsHistory)"); break;
                case "envelope": await db.SensitiveAttributes.ExecuteUpdateAsync(set => set.SetProperty(row => row.Envelope, new byte[] { 1 })); break;
                case "reminder": await db.Reminders.ExecuteUpdateAsync(set => set.SetProperty(row => row.Action, " ")); break;
                case "evidence": await db.AssetEvidence.ExecuteUpdateAsync(set => set.SetProperty(row => row.Content, " ")); break;
                case "relationship":
                    var other = Guid.NewGuid();
                    db.Vaults.Add(new VaultRow { Id = other, Name = "Other", Type = 0, Status = 0 });
                    db.Memberships.Add(new MembershipRow { VaultId = other, ActorId = _data.Owner, Role = 0 });
                    await db.SaveChangesAsync();
                    await db.Relationships.ExecuteUpdateAsync(set => set.SetProperty(row => row.VaultId, other)); break;
            }
        }
        Assert.That(await EncryptedMaintenance.BackupAsync(Database, Ring, Backup, _secret), Is.EqualTo(KeyOperationOutcome.Failed));
        Assert.That(Directory.Exists(Backup), Is.False);
    }

    [Test, Platform("Win")]
    public async Task HostExclusionAndMissingHistoricalKeysFailClosed()
    {
        if (!OperatingSystem.IsWindows()) return;
        using (PrivateOperatorFiles.AcquireDatabase(Database))
            Assert.That(await EncryptedMaintenance.RotateAsync(Database, Ring, Exports, _secret), Is.EqualTo(KeyOperationOutcome.Failed));
        using var missing = new MissingCustody();
        await Assert.ThrowsAsync<InvalidOperationException>(() => EncryptedDatabaseMaintenance.Validate(new SqliteDatabase(Database), missing, default));
    }

    [Test, Platform("Win")]
    public async Task AuthenticatedExportWithoutHistoricalKeyCannotRestore()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.That(await EncryptedMaintenance.BackupAsync(Database, Ring, Backup, _secret), Is.EqualTo(KeyOperationOutcome.Succeeded));
        var export = Path.Combine(Backup, "keys.hvkr"); var copy = Path.Combine(Backup, "database.sqlite");
        using var payload = RecoveryPackage.Open(File.ReadAllBytes(export), _secret);
        foreach (var key in payload.Keys.Values) CryptographicOperations.ZeroMemory(key);
        payload.Keys.Clear();
        File.WriteAllBytes(export, RecoveryPackage.Seal(payload, _secret));
        File.WriteAllBytes(Path.Combine(Backup, "manifest.hvbm"), BackupManifest.Seal(copy, export, payload.Id, payload.Generation, _secret));
        Assert.That(await EncryptedMaintenance.RecoverAsync(Backup, Restored, _secret), Is.EqualTo(KeyOperationOutcome.Failed));
        Assert.That(Directory.Exists(Restored), Is.False);
    }

    [Test, Platform("Win")]
    public async Task FullRingRejectsRotationWithoutLosingExistingKeysOrRecords()
    {
        if (!OperatingSystem.IsWindows()) return;
        var before = await Envelopes();
        using var payload = WindowsKeyCustody.Load(Ring, out _);
        while (payload.Keys.Count < KeyRingPayload.MaximumKeys) payload.Keys.Add(Guid.NewGuid(), RandomNumberGenerator.GetBytes(32));
        var package = Path.Combine(Exports, "full.hvkr"); PrivateKeyFiles.WriteNew(package, RecoveryPackage.Seal(payload, _secret));
        var full = Path.Combine(_root, "full-ring"); WindowsKeyCustody.Recover(package, full, _secret);
        Assert.That(await EncryptedMaintenance.RotateAsync(Database, full, Exports, _secret), Is.EqualTo(KeyOperationOutcome.Failed));
        Assert.That((await Envelopes()).All(pair => pair.Value.SequenceEqual(before[pair.Key])), Is.True);
        using var retained = WindowsKeyCustody.Load(full, out _);
        Assert.That(retained.Keys, Has.Count.EqualTo(KeyRingPayload.MaximumKeys));
    }

    [Test, Platform("Win")]
    public async Task ExistingBackupAndCanceledOperationsAreNeverPublishedOver()
    {
        if (!OperatingSystem.IsWindows()) return;
        Directory.CreateDirectory(Backup); File.WriteAllText(Path.Combine(Backup, "keep"), "existing");
        Assert.That(await EncryptedMaintenance.BackupAsync(Database, Ring, Backup, _secret), Is.EqualTo(KeyOperationOutcome.Failed));
        Assert.That(File.ReadAllText(Path.Combine(Backup, "keep")), Is.EqualTo("existing"));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => EncryptedMaintenance.BackupAsync(Database, Ring, Restored, _secret, cancellation.Token));
        Assert.That(Directory.Exists(Restored), Is.False);
    }

    [Test]
    public async Task UnsupportedPlatformsFailWithoutArtifacts()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.That(await EncryptedMaintenance.BackupAsync("relative", Ring, Backup, _secret), Is.EqualTo(KeyOperationOutcome.Failed));
            Assert.That(Directory.Exists(Backup), Is.False);
            return;
        }
        Assert.That(await EncryptedMaintenance.RotateAsync(Database, Ring, Exports, _secret), Is.EqualTo(KeyOperationOutcome.UnsupportedPlatform));
        Assert.That(await EncryptedMaintenance.BackupAsync(Database, Ring, Backup, _secret), Is.EqualTo(KeyOperationOutcome.UnsupportedPlatform));
        Assert.That(await EncryptedMaintenance.RecoverAsync(Backup, Restored, _secret), Is.EqualTo(KeyOperationOutcome.UnsupportedPlatform));
        Assert.That(Directory.GetFileSystemEntries(_root), Is.Empty);
    }
    private async Task AddRotationRows()
    {
        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException();
        using var session = SensitiveStorageSession.OpenWindows(Ring, Exports, _secret);
        var store = session.CreateStore(new SqliteDatabase(Database));
        for (var i = 1; i < 130; i++)
            Assert.That((await store.AddAsync(_data.Asset, _data.Owner, "private-" + i, MaintenanceData.Value, AttributeSensitivity.Sensitive, default)).Outcome, Is.EqualTo(SensitiveAttributeOutcome.Succeeded));
    }
    private async Task<Dictionary<Guid, byte[]>> Envelopes()
    {
        await using var db = new SqliteDatabase(Database).CreateContext();
        return await db.SensitiveAttributes.AsNoTracking().ToDictionaryAsync(row => row.Id, row => row.Envelope);
    }
    private sealed class MissingCustody : IEncryptionKeyCustody, IDisposable
    {
        public WriteKeySession? CreateVerifiedWriteSession() => null;
        public ReadKeyLease? FindReadKey(Guid id) => null;
        public void Dispose() { }
    }
}

internal sealed class MaintenanceData
{
    internal const string Value = " fictional-maintenance-secret-فارسی-😀 \r\n";
    private const string Password = "fictional maintenance passphrase";
    internal Guid Owner, Editor, Vault, Asset, Attribute;
    private string _stamp = "";
    private string _invitation = "";
    private string _oldCookie = "";
    private string _recovery = "";
    internal static async Task<MaintenanceData> Create(string path, SensitiveStorageSession session, int count = 1)
    {
        var data = new MaintenanceData(); var database = new SqliteDatabase(path); await database.MigrateAsync();
        var protection = new EphemeralDataProtectionProvider();
        using (var services = new ServiceCollection().AddHomeVaultAccounts(path, protection).BuildServiceProvider())
        using (var scope = services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<HomeVaultUser>>();
            var owner = new HomeVaultUser { UserName = "owner@example.invalid", Email = "owner@example.invalid" };
            var editor = new HomeVaultUser { UserName = "editor@example.invalid", Email = "editor@example.invalid" };
            Assert.That((await users.CreateAsync(owner, Password)).Succeeded, Is.True);
            Assert.That((await users.CreateAsync(editor, Password)).Succeeded, Is.True);
            data.Owner = owner.Id; data.Editor = editor.Id; data._stamp = owner.SecurityStamp!;
            data._invitation = (await scope.ServiceProvider.GetRequiredService<AccountOperations>().IssueAsync("invited@example.invalid", false))!.Secret;
        }
        using (var services = new ServiceCollection().AddHomeVaultAccounts(path, protection).BuildServiceProvider())
        using (var scope = services.CreateScope())
        {
            data._recovery = (await scope.ServiceProvider.GetRequiredService<AccountOperations>().IssueAsync("owner@example.invalid", true))!.Secret;
        }
        using (var services = new ServiceCollection().AddHomeVaultAccounts(path, protection).BuildServiceProvider())
        using (var scope = services.CreateScope())
        {
            data._stamp = (await scope.ServiceProvider.GetRequiredService<UserManager<HomeVaultUser>>().FindByIdAsync(data.Owner.ToString()))!.SecurityStamp!;
        }
        var ticket = new Microsoft.AspNetCore.Authentication.AuthenticationTicket(new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity([new(System.Security.Claims.ClaimTypes.NameIdentifier, data.Owner.ToString()), new("homevault:stamp", data._stamp)], "HomeVault")), "HomeVault");
        data._oldCookie = CookieFormat(protection).Protect(ticket);
        Assert.That(CookieFormat(protection).Unprotect(data._oldCookie), Is.Not.Null);
        data.Vault = Guid.NewGuid(); data.Asset = Guid.NewGuid();
        await new SqliteVaultRepository(database).AddAsync(HomeVault.Domain.Vaults.Vault.Create(data.Vault, "Fictional", VaultType.Personal, data.Owner).Vault!, default);
        await new SqliteVaultMembershipStore(database).AddAsync(data.Vault, data.Owner, data.Editor, VaultRole.Editor, default);
        await new SqliteAssetRegistrationStore(database).RegisterAsync(HomeVault.Domain.Assets.Asset.Create(data.Asset, data.Vault, "Fictional").Asset!, data.Owner, default);
        var second = Guid.NewGuid();
        await new SqliteAssetRegistrationStore(database).RegisterAsync(HomeVault.Domain.Assets.Asset.Create(second, data.Vault, "Second").Asset!, data.Owner, default);
        await using (var db = database.CreateContext())
        {
            db.AssetEvidence.Add(new EvidenceRow { AssetId = data.Asset, Id = Guid.NewGuid(), Kind = (int)EvidenceKind.Note, Label = "Note", Content = "fictional ordinary note" });
            db.Reminders.Add(new ReminderRow { Id = Guid.NewGuid(), VaultId = data.Vault, AssetId = data.Asset, Action = "fictional action", DueAtUtcTicks = DateTimeOffset.UtcNow.UtcTicks });
            db.Relationships.Add(new RelationshipRow { Id = Guid.NewGuid(), VaultId = data.Vault, SourceAssetId = data.Asset, TargetAssetId = second });
            await db.SaveChangesAsync();
        }
        var store = session.CreateStore(database);
        for (var i = 0; i < count; i++)
        {
            var result = await store.AddAsync(data.Asset, data.Owner, "private-" + i, Value, AttributeSensitivity.Sensitive, default);
            Assert.That(result.Outcome, Is.EqualTo(SensitiveAttributeOutcome.Succeeded)); data.Attribute = result.Id!.Value;
        }
        return data;
    }
    internal async Task CheckReads(SqliteDatabase database, SensitiveStorageSession session)
    {
        var store = session.CreateStore(database);
        Assert.That((await store.ReadAsync(Asset, Owner, Attribute, default)).ReadValue(), Is.EqualTo(Value));
        Assert.That((await store.ReadAsync(Asset, Editor, Attribute, default)).Outcome, Is.EqualTo(SensitiveAttributeOutcome.Forbidden));
        Assert.That((await store.ReadAsync(Asset, Guid.NewGuid(), Attribute, default)).Outcome, Is.EqualTo(SensitiveAttributeOutcome.Unavailable));
    }
    internal async Task CheckAccounts(string database, string keys)
    {
        using var provider = WindowsSessionKeys.Open(keys);
        using var services = new ServiceCollection().AddHomeVaultAccounts(database, provider).BuildServiceProvider();
        Assert.That(CookieFormat(provider).Unprotect(_oldCookie), Is.Null);
        using (var scope = services.CreateScope())
            Assert.That(await scope.ServiceProvider.GetRequiredService<AccountOperations>().ValidateSessionAsync(Owner, _stamp), Is.False);
        using (var scope = services.CreateScope())
            Assert.That(await scope.ServiceProvider.GetRequiredService<AccountOperations>().RedeemAsync("invited@example.invalid", _invitation, Password, false), Is.False);
        using (var scope = services.CreateScope())
            Assert.That(await scope.ServiceProvider.GetRequiredService<AccountOperations>().RedeemAsync("owner@example.invalid", _recovery, Password, true), Is.False);
        using (var scope = services.CreateScope())
            Assert.That((await scope.ServiceProvider.GetRequiredService<AccountOperations>().LoginAsync("owner@example.invalid", Password))?.Id, Is.EqualTo(Owner));
    }
    private static Microsoft.AspNetCore.Authentication.TicketDataFormat CookieFormat(IDataProtectionProvider provider) =>
        new(provider.CreateProtector("Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationMiddleware", "HomeVault", "v2"));
}
