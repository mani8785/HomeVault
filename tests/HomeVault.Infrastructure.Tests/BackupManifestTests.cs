using System.Security.Cryptography;
using HomeVault.Infrastructure.Encryption;
using NUnit.Framework;

namespace HomeVault.Infrastructure.Tests;

public sealed class BackupManifestTests
{
    [Test]
    public void EveryAuthenticatedByteAndBothFileContentsAreBound()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var db = Path.Combine(root, "db"); var export = Path.Combine(root, "export");
            File.WriteAllBytes(db, [1, 2, 3]); File.WriteAllBytes(export, [4, 5, 6]);
            var secret = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
            var id = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
            var manifest = BackupManifest.Seal(db, export, id, 7, secret, new byte[32]);
            Assert.That(manifest, Has.Length.EqualTo(173));
            Assert.That(BackupManifest.Verify(manifest, db, export, secret), Is.EqualTo((id, 7UL)));
            // Independently derive HKDF via RFC 5869 extract/expand, rather than the production HKDF helper.
            var prk = HMACSHA256.HashData(new byte[32], secret);
            var info = "HomeVault.BackupManifest.v1"u8.ToArray().Concat(new byte[] { 1 }).ToArray();
            var key = HMACSHA256.HashData(prk, info);
            Assert.That(manifest[141..], Is.EqualTo(HMACSHA256.HashData(key, manifest[..141])));
            for (var i = 0; i < manifest.Length; i++)
            {
                var altered = manifest.ToArray(); altered[i] ^= 1;
                Assert.Throws<InvalidOperationException>(() => BackupManifest.Verify(altered, db, export, secret));
            }
            Assert.Throws<InvalidOperationException>(() => BackupManifest.Verify(manifest[..^1], db, export, secret));
            Assert.Throws<InvalidOperationException>(() => BackupManifest.Verify(manifest.Concat(new byte[] { 0 }).ToArray(), db, export, secret));
            Assert.Throws<InvalidOperationException>(() => BackupManifest.Verify(manifest, export, db, secret));
            File.WriteAllBytes(db, [1, 2, 4]);
            Assert.Throws<InvalidOperationException>(() => BackupManifest.Verify(manifest, db, export, secret));
            File.WriteAllBytes(db, [1, 2, 3]); File.WriteAllBytes(export, [4, 5]);
            Assert.Throws<InvalidOperationException>(() => BackupManifest.Verify(manifest, db, export, secret));
        }
        finally { Directory.Delete(root, true); }
    }
}
