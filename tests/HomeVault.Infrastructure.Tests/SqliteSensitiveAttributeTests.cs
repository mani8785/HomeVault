using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HomeVault.Application.Assets;
using HomeVault.Domain.Assets;
using HomeVault.Domain.Vaults;
using HomeVault.Infrastructure.Encryption;
using HomeVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NUnit.Framework;

namespace HomeVault.Infrastructure.Tests;

public sealed class SqliteSensitiveAttributeTests
{
    private string _directory = null!;
    private SqliteDatabase _database = null!;
    private SensitiveStorageSession _session = null!;
    private ISensitiveAttributeStore _store = null!;
    private readonly FictionalCustody _custody = new();
    private Guid _owner, _vault, _asset;
    private const string Secret = " fictional-private-sentinel-فارسی-😀 \r\n\t";

    [SetUp]
    public async Task SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "HomeVault-sensitive-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _database = new SqliteDatabase(Path.Combine(_directory, "data.db"));
        await _database.MigrateAsync();
        _session = new SensitiveStorageSession(_custody); _store = _session.CreateStore(_database);
        _owner = Guid.NewGuid(); _vault = Guid.NewGuid(); _asset = Guid.NewGuid();
        await new SqliteVaultRepository(_database).AddAsync(Vault.Create(_vault, "Vault", VaultType.Personal, _owner).Vault!, default);
        await new SqliteAssetRegistrationStore(_database).RegisterAsync(Asset.Create(_asset, _vault, "Asset").Asset!, _owner, default);
    }
    [TearDown]
    public void TearDown() { _session.Dispose(); _custody.Missing = false; Directory.Delete(_directory, true); }
    private Task<SensitiveAttributeResult> Add(string name = " Private/طبقه ", string value = Secret) => _store.AddAsync(_asset, _owner, name, value, AttributeSensitivity.Sensitive, default);

    [Test]
    public async Task ExactTextStableIdentityAndOrdinaryCoexistence()
    {
        var added = await Add(); Assert.That(added.Outcome, Is.EqualTo(SensitiveAttributeOutcome.Succeeded));
        var id = added.Id!.Value;
        var read = await _store.ReadAsync(_asset, _owner, id, default);
        Assert.That(read.ReadValue(), Is.EqualTo(Secret));
        Assert.That(JsonSerializer.Serialize(read), Does.Not.Contain("fictional-private"));
        Assert.That(read.ToString(), Is.EqualTo("SensitiveAttributeResult"));
        Assert.That((await _store.ListAsync(_asset, _owner, default)).Metadata!.Single().Name, Is.EqualTo("Private/طبقه"));
        var ordinary = new SqliteOrdinaryAttributeStore(_database);
        Assert.That(await ordinary.AddAsync(_asset, _owner, "PRIVATE/طبقه", "ordinary", AttributeSensitivity.Ordinary, default), Is.EqualTo(AttributeOutcome.DuplicateName));
        Assert.That(await ordinary.ChangeAsync(_asset, _owner, "Private/طبقه", "overwrite", default), Is.EqualTo(AttributeOutcome.Unavailable));
        Assert.That(await ordinary.RemoveAsync(_asset, _owner, "Private/طبقه", default), Is.EqualTo(AttributeOutcome.Unavailable));
        Assert.That(await ordinary.ListAsync(_asset, _owner, default), Is.Empty);
        Assert.That(await ordinary.AddAsync(_asset, _owner, "ordinary", "visible", AttributeSensitivity.Ordinary, default), Is.EqualTo(AttributeOutcome.Succeeded));
        Assert.That((await Add("ORDINARY")).Outcome, Is.EqualTo(SensitiveAttributeOutcome.DuplicateName));
        Assert.That((await _store.ChangeAsync(_asset, _owner, id, Secret + "changed", default)).Outcome, Is.EqualTo(SensitiveAttributeOutcome.Succeeded));
        Assert.That((await _store.ListAsync(_asset, _owner, default)).Metadata!.Single().Id, Is.EqualTo(id));
        Assert.That((await _store.ReadAsync(_asset, _owner, id, default)).ReadValue(), Is.EqualTo(Secret + "changed"));
        await _store.RemoveAsync(_asset, _owner, id, default);
        Assert.That((await Add()).Id, Is.Not.EqualTo(id));
    }

    [TestCase(0), TestCase(1), TestCase(2), TestCase(3)]
    public async Task SensitiveRoleMatrixAndArchivedReads(int role)
    {
        var id = (await Add()).Id!.Value; var actor = Guid.NewGuid();
        await using (var db = _database.CreateContext())
        { db.Memberships.Add(new MembershipRow { VaultId = _vault, ActorId = actor, Role = role }); await db.SaveChangesAsync(); }
        var allowed = role <= 1;
        var expected = allowed ? SensitiveAttributeOutcome.Succeeded : SensitiveAttributeOutcome.Forbidden;
        Assert.That((await _store.ListAsync(_asset, actor, default)).Metadata, Has.Count.EqualTo(1));
        var reads = _custody.Reads;
        Assert.That((await _store.ReadAsync(_asset, actor, id, default)).Outcome, Is.EqualTo(expected));
        if (!allowed) Assert.That(_custody.Reads, Is.EqualTo(reads));
        Assert.That((await _store.ChangeAsync(_asset, actor, id, "replacement", default)).Outcome, Is.EqualTo(expected));
        Assert.That((await _store.AddAsync(_asset, actor, "new", Secret, AttributeSensitivity.Sensitive, default)).Outcome, Is.EqualTo(expected));
        await new SqliteVaultArchiveStore(_database).ArchiveAsync(_vault, _owner, default);
        Assert.That((await _store.ReadAsync(_asset, actor, id, default)).Outcome, Is.EqualTo(expected));
        Assert.That((await _store.RemoveAsync(_asset, actor, id, default)).Outcome, Is.EqualTo(allowed ? SensitiveAttributeOutcome.Archived : SensitiveAttributeOutcome.Forbidden));
    }

    [Test]
    public async Task DeniedAndMetadataOperationsNeverRequestKeys()
    {
        var id = (await Add()).Id!.Value; var reads = _custody.Reads;
        _custody.Missing = true;
        Assert.That((await _store.ListAsync(_asset, _owner, default)).Outcome, Is.EqualTo(SensitiveAttributeOutcome.Succeeded));
        foreach (var asset in new[] { _asset, Guid.NewGuid() })
            Assert.That((await _store.ReadAsync(asset, Guid.NewGuid(), id, default)).Outcome, Is.EqualTo(SensitiveAttributeOutcome.Unavailable));
        Assert.That(_custody.Reads, Is.EqualTo(reads));
        Assert.That((await _store.ReadAsync(_asset, _owner, id, default)).Outcome, Is.EqualTo(SensitiveAttributeOutcome.SensitiveUnavailable));
    }

    [Test]
    public async Task TamperingAndAttributeSubstitutionFailWithoutPlaintext()
    {
        var first = (await Add()).Id!.Value; var second = (await Add("other")).Id!.Value;
        await using var db = _database.CreateContext();
        var envelope = await db.SensitiveAttributes.Where(row => row.Id == first).Select(row => row.Envelope).SingleAsync();
        await db.SensitiveAttributes.Where(row => row.Id == second).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Envelope, envelope));
        Assert.That((await _store.ReadAsync(_asset, _owner, second, default)).Outcome, Is.EqualTo(SensitiveAttributeOutcome.SensitiveUnavailable));
        envelope[^1] ^= 1;
        await db.SensitiveAttributes.Where(row => row.Id == first).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Envelope, envelope));
        var failed = await _store.ReadAsync(_asset, _owner, first, default);
        Assert.Throws<InvalidOperationException>(() => failed.ReadValue());
        Assert.That(failed.Outcome, Is.EqualTo(SensitiveAttributeOutcome.SensitiveUnavailable));
    }

    [Test]
    public async Task InvalidTextAndSqlFailureLeaveNoRowOrPlaintext()
    {
        using var diagnostics = new CapturedCommands();
        Assert.That((await Add("blank", " \t")).Outcome, Is.EqualTo(SensitiveAttributeOutcome.InvalidValue));
        Assert.That((await Add("unicode", "\ud800")).Outcome, Is.EqualTo(SensitiveAttributeOutcome.InvalidValue));
        await using var db = _database.CreateContext();
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER FailSensitive BEFORE INSERT ON SensitiveAssetAttributes BEGIN SELECT RAISE(ABORT, 'fictional failure'); END;");
        await Assert.ThrowsAsync<DbUpdateException>(() => Add());
        Assert.That(await db.SensitiveAttributes.CountAsync(), Is.Zero);
        Assert.That(diagnostics.Commands, Is.GreaterThan(0));
        Assert.That(string.Join("\n", diagnostics.Text), Does.Not.Contain("fictional-private-sentinel"));
        foreach (var file in Directory.GetFiles(_directory))
            Assert.That(File.ReadAllBytes(file).AsSpan().IndexOf(Encoding.UTF8.GetBytes(Secret)), Is.EqualTo(-1));
    }

    [TestCase(false), TestCase(true)]
    public async Task MovingEncryptedRowToAnotherAssetOrVaultFailsAuthentication(bool differentVault)
    {
        var id = (await Add()).Id!.Value;
        var vault = differentVault ? Guid.NewGuid() : _vault;
        if (differentVault) await new SqliteVaultRepository(_database).AddAsync(Vault.Create(vault, "other", VaultType.Personal, _owner).Vault!, default);
        var asset = Guid.NewGuid();
        await new SqliteAssetRegistrationStore(_database).RegisterAsync(Asset.Create(asset, vault, "target").Asset!, _owner, default);
        await using var db = _database.CreateContext();
        await db.SensitiveAttributes.Where(row => row.Id == id).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.AssetId, asset));
        Assert.That((await _store.ReadAsync(asset, _owner, id, default)).Outcome, Is.EqualTo(SensitiveAttributeOutcome.SensitiveUnavailable));
    }

    [Test]
    public async Task RawRowsAndVerifiedBackupContainCiphertextAndRemainReadable()
    {
        var id = (await Add()).Id!.Value;
        var backupPath = Path.Combine(_directory, "backup.db"); await _database.CreateVerifiedCopyAsync(backupPath);
        foreach (var file in Directory.GetFiles(_directory))
        {
            var bytes = File.ReadAllBytes(file);
            Assert.That(bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(Secret)), Is.EqualTo(-1));
            Assert.That(bytes.AsSpan().IndexOf(Encoding.Unicode.GetBytes(Secret)), Is.EqualTo(-1));
        }
        var copiedStore = _session.CreateStore(new SqliteDatabase(backupPath));
        Assert.That((await copiedStore.ReadAsync(_asset, _owner, id, default)).ReadValue(), Is.EqualTo(Secret));
        await using var db = _database.CreateContext();
        Assert.That((await db.SensitiveAttributes.SingleAsync()).Envelope.AsSpan(0, 4).SequenceEqual("HVAE"u8), Is.True);
    }

    [TestCase("WAL", "-wal"), TestCase("DELETE", "-journal")]
    public async Task LiveJournalAndWalHaveNoPlaintextAndInterruptedWriteRollsBack(string mode, string suffix)
    {
        var path = Path.Combine(_directory, "data.db");
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        { command.CommandText = "PRAGMA journal_mode=" + mode; Assert.That((await command.ExecuteScalarAsync())!.ToString()!.ToUpperInvariant(), Is.EqualTo(mode)); }
        // A committed encrypted write produces real WAL frames; a small uncommitted
        // transaction can remain entirely in SQLite's page cache until commit.
        if (mode == "WAL") await Add("committed WAL record");
        var inspected = false;
        var failing = new SqliteSensitiveAttributeStore(_database, _session, () =>
        {
            var journal = path + suffix;
            Assert.That(File.Exists(journal), Is.True);
            using var file = new FileStream(journal, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var bytes = new MemoryStream(); file.CopyTo(bytes);
            Assert.That(bytes.Length, Is.GreaterThan(0));
            Assert.That(bytes.ToArray().AsSpan().IndexOf(Encoding.UTF8.GetBytes(Secret)), Is.EqualTo(-1));
            Assert.That(bytes.ToArray().AsSpan().IndexOf(Encoding.Unicode.GetBytes(Secret)), Is.EqualTo(-1));
            inspected = true; throw new IOException("Fictional interruption");
        });
        await Assert.ThrowsAsync<IOException>(() => failing.AddAsync(_asset, _owner, "journal", Secret, AttributeSensitivity.Sensitive, default));
        Assert.That(inspected, Is.True);
        Assert.That((await _store.ListAsync(_asset, _owner, default)).Metadata, Has.Count.EqualTo(mode == "WAL" ? 1 : 0));
    }

    [Test, Platform("Win")]
    public async Task WindowsRecoveredRingAuthenticatesCopiedDatabaseRecords()
    {
        if (!OperatingSystem.IsWindows()) return;
        var ring = Path.Combine(_directory, "keys"); var exports = Path.Combine(_directory, "exports");
        PrivateKeyFiles.CreateDirectory(exports);
        var secret = RandomNumberGenerator.GetBytes(32);
        try
        {
            WindowsKeyCustody.Initialize(ring, exports, secret);
            using var original = SensitiveStorageSession.OpenWindows(ring, exports, secret);
            var store = original.CreateStore(_database);
            var id = (await store.AddAsync(_asset, _owner, "portable", Secret, AttributeSensitivity.Sensitive, default)).Id!.Value;
            var backup = Path.Combine(_directory, "portable.db"); await _database.CreateVerifiedCopyAsync(backup);
            using var current = WindowsKeyCustody.Load(ring, out var generation);
            var export = Directory.GetFiles(exports, $"*-{generation}.hvkr").Single();
            var recovered = Path.Combine(_directory, "recovered"); WindowsKeyCustody.Recover(export, recovered, secret);
            using var restored = SensitiveStorageSession.OpenWindows(recovered, exports, secret);
            Assert.That((await restored.CreateStore(new SqliteDatabase(backup)).ReadAsync(_asset, _owner, id, default)).ReadValue(), Is.EqualTo(Secret));
        }
        finally { CryptographicOperations.ZeroMemory(secret); }
    }

    [Test]
    public async Task ConcurrentOrdinaryAndSensitiveAddsCannotCreateDuplicateNames()
    {
        var ordinary = new SqliteOrdinaryAttributeStore(_database);
        var first = Task.Run(() => _store.AddAsync(_asset, _owner, "collision", Secret, AttributeSensitivity.Sensitive, default));
        var second = Task.Run(() => ordinary.AddAsync(_asset, _owner, "COLLISION", "visible", AttributeSensitivity.Ordinary, default));
        await Task.WhenAll(first, second);
        Assert.That((first.Result.Outcome == SensitiveAttributeOutcome.Succeeded ? 1 : 0) + (second.Result == AttributeOutcome.Succeeded ? 1 : 0), Is.EqualTo(1));
        Assert.That(first.Result.Outcome, Is.AnyOf(SensitiveAttributeOutcome.Succeeded, SensitiveAttributeOutcome.DuplicateName));
        Assert.That(second.Result, Is.AnyOf(AttributeOutcome.Succeeded, AttributeOutcome.DuplicateName));
    }

    [Test]
    public async Task UpgradePreservesPopulatedOrdinaryDataAndRejectsDestructiveDowngrade()
    {
        var older = new SqliteDatabase(Path.Combine(_directory, "upgrade.db"));
        await using (var db = older.CreateContext())
        {
            var previous = db.Database.GetMigrations().Reverse().Skip(1).First();
            await db.GetService<IMigrator>().MigrateAsync(previous);
            db.Vaults.Add(new VaultRow { Id = _vault, Name = "old", Status = 0, Type = 0 });
            db.Memberships.Add(new MembershipRow { VaultId = _vault, ActorId = _owner, Role = 0 });
            db.Assets.Add(new AssetRow { Id = _asset, VaultId = _vault, Name = "preserved" });
            db.AssetAttributes.Add(new AssetAttributeRow { AssetId = _asset, Name = "ordinary", Value = "kept exactly ", Sensitivity = 0 });
            await db.SaveChangesAsync();
        }
        await older.MigrateAsync();
        Assert.That((await new SqliteOrdinaryAttributeStore(older).ListAsync(_asset, _owner, default))!.Single().Value, Is.EqualTo("kept exactly "));
        Assert.That((await _session.CreateStore(older).AddAsync(_asset, _owner, "private", Secret, AttributeSensitivity.Sensitive, default)).Outcome, Is.EqualTo(SensitiveAttributeOutcome.Succeeded));
        await using var upgraded = older.CreateContext();
        var target = upgraded.Database.GetMigrations().Reverse().Skip(1).First();
        await Assert.ThrowsAsync<NotSupportedException>(() => upgraded.GetService<IMigrator>().MigrateAsync(target));
        Assert.That(await upgraded.SensitiveAttributes.CountAsync(), Is.EqualTo(1));
    }

    [TestCase(false), TestCase(true)]
    public async Task IndependentPermissionOrArchiveMutationSerializesWithSensitiveWrite(bool archive)
    {
        var actor = Guid.NewGuid();
        await using (var db = _database.CreateContext())
        { db.Memberships.Add(new MembershipRow { VaultId = _vault, ActorId = actor, Role = 1 }); await db.SaveChangesAsync(); }
        var write = Task.Run(() => _store.AddAsync(_asset, actor, "race", Secret, AttributeSensitivity.Sensitive, default));
        var restriction = Task.Run(async () =>
        {
            if (archive) await new SqliteVaultArchiveStore(_database).ArchiveAsync(_vault, _owner, default);
            else await new SqliteVaultMembershipStore(_database).ChangeRoleAsync(_vault, _owner, actor, VaultRole.Viewer, default);
        });
        await Task.WhenAll(write, restriction);
        var denied = archive ? SensitiveAttributeOutcome.Archived : SensitiveAttributeOutcome.Forbidden;
        Assert.That(write.Result.Outcome, Is.AnyOf(SensitiveAttributeOutcome.Succeeded, denied));
        Assert.That((await _store.AddAsync(_asset, actor, "after", Secret, AttributeSensitivity.Sensitive, default)).Outcome, Is.EqualTo(denied));
        Assert.That((await _store.ListAsync(_asset, actor, default)).Metadata, Has.Count.EqualTo(write.Result.Outcome == SensitiveAttributeOutcome.Succeeded ? 1 : 0));
    }

    private sealed class CapturedCommands : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly List<IDisposable> _subscriptions = [];
        private readonly IDisposable _all;
        internal readonly List<string> Text = [];
        internal int Commands;
        internal CapturedCommands() => _all = DiagnosticListener.AllListeners.Subscribe(this);
        public void OnNext(DiagnosticListener listener)
        { if (listener.Name == "Microsoft.EntityFrameworkCore") _subscriptions.Add(listener.Subscribe(this)); }
        public void OnNext(KeyValuePair<string, object?> item)
        {
            if (item.Value is not CommandEventData command) return;
            Commands++; Text.Add(command.Command.CommandText);
            foreach (DbParameter parameter in command.Command.Parameters)
                Text.Add(parameter.Value is byte[] bytes ? Encoding.UTF8.GetString(bytes) : parameter.Value?.ToString() ?? "");
            if (item.Value is CommandErrorEventData failed) Text.Add(failed.Exception.ToString());
        }
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void Dispose() { _all.Dispose(); foreach (var subscription in _subscriptions) subscription.Dispose(); }
    }

    private sealed class FictionalCustody : IEncryptionKeyCustody
    {
        private readonly Dictionary<Guid, byte[]> _keys = [];
        internal bool Missing;
        internal int Reads;
        public WriteKeySession CreateVerifiedWriteSession()
        { var id = Guid.NewGuid(); var key = RandomNumberGenerator.GetBytes(32); _keys.Add(id, key); return new WriteKeySession(id, key.ToArray()); }
        public ReadKeyLease? FindReadKey(Guid id)
        { Reads++; return !Missing && _keys.TryGetValue(id, out var key) ? new ReadKeyLease(id, key.ToArray()) : null; }
    }
}
