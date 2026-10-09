using System.Security.Cryptography;
using HomeVault.Infrastructure.Encryption;
using HomeVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace HomeVault.Infrastructure.Tests;

public sealed class RotationCoreTests
{
    [TestCase(false), TestCase(true)]
    public async Task CrossPlatformRotationAuthenticatesRecordsAndDoesNotCommitFailedBatches(bool corrupt)
    {
        var root = Path.Combine(Path.GetTempPath(), "HomeVault-rotation-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "data.sqlite"); var database = new SqliteDatabase(path);
            using var custody = new Custody(); using var session = new SensitiveStorageSession(custody);
            var data = await MaintenanceData.Create(path, session);
            byte[] original;
            await using (var db = database.CreateContext())
            {
                original = (await db.SensitiveAttributes.SingleAsync()).Envelope.ToArray();
                if (corrupt)
                {
                    var changed = original.ToArray(); changed[^1] ^= 1;
                    await db.SensitiveAttributes.ExecuteUpdateAsync(set => set.SetProperty(row => row.Envelope, changed));
                }
            }
            if (corrupt)
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => EncryptedDatabaseMaintenance.Rotate(database, custody, custody.CreateVerifiedWriteSession, default));
                Assert.That(custody.Count, Is.EqualTo(1));
            }
            else
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => EncryptedDatabaseMaintenance.Rotate(database, custody, custody.CreateVerifiedWriteSession, default,
                    stage => { if (stage == "batch-written") throw new InvalidOperationException(); }));
                await using (var db = database.CreateContext()) Assert.That((await db.SensitiveAttributes.SingleAsync()).Envelope, Is.EqualTo(original));
                await EncryptedDatabaseMaintenance.Rotate(database, custody, custody.CreateVerifiedWriteSession, default);
                await data.CheckReads(database, session);
                Assert.That(custody.Count, Is.EqualTo(3));
            }
        }
        finally { Directory.Delete(root, true); }
    }
    private sealed class Custody : IEncryptionKeyCustody, IDisposable
    {
        private readonly Dictionary<Guid, byte[]> _keys = [];
        internal int Count => _keys.Count;
        public WriteKeySession CreateVerifiedWriteSession()
        { var id = Guid.NewGuid(); var key = RandomNumberGenerator.GetBytes(32); _keys.Add(id, key); return new WriteKeySession(id, key.ToArray()); }
        public ReadKeyLease? FindReadKey(Guid id) => _keys.TryGetValue(id, out var key) ? new ReadKeyLease(id, key.ToArray()) : null;
        public void Dispose() { foreach (var key in _keys.Values) CryptographicOperations.ZeroMemory(key); }
    }
}
