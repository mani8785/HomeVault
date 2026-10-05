using System.Buffers.Binary;
using System.Security.Cryptography;
using HomeVault.Infrastructure.Encryption;
using NUnit.Framework;

namespace HomeVault.Infrastructure.Tests;

public sealed class RecoveryPackageTests
{
    [Test]
    public void FreshExportsRecoverEveryRetainedKey()
    {
        using var ring = new KeyRingPayload(Guid.NewGuid(), 7);
        ring.Keys.Add(Guid.NewGuid(), RandomNumberGenerator.GetBytes(32));
        ring.Keys.Add(Guid.NewGuid(), RandomNumberGenerator.GetBytes(32));
        var secret = RandomNumberGenerator.GetBytes(32);
        var first = RecoveryPackage.Seal(ring, secret);
        var second = RecoveryPackage.Seal(ring, secret);
        Assert.That(first, Is.Not.EqualTo(second));
        using var restored = RecoveryPackage.Open(first, secret);
        Assert.That(RecoveryPackage.Equal(ring, restored), Is.True);
        Assert.That(first.AsSpan(29, 32).SequenceEqual(second.AsSpan(29, 32)), Is.False);
        Assert.That(first.AsSpan(61, 12).SequenceEqual(second.AsSpan(61, 12)), Is.False);
        var owned = restored.Keys.Values.ToArray();
        restored.Dispose();
        Assert.That(owned.SelectMany(key => key), Is.All.Zero);
    }

    [Test]
    public void EveryByteIsAuthenticatedAndEveryTruncationRejected()
    {
        using var ring = new KeyRingPayload(Guid.NewGuid(), 1);
        ring.Keys.Add(Guid.NewGuid(), RandomNumberGenerator.GetBytes(32));
        var secret = RandomNumberGenerator.GetBytes(32);
        var package = RecoveryPackage.Seal(ring, secret);
        for (var i = 0; i < package.Length; i++)
        {
            var changed = package.ToArray(); changed[i] ^= 1;
            Assert.That(() => RecoveryPackage.Open(changed, secret), Throws.Exception, $"Mutation {i}");
            var shortened = package[..i];
            Assert.That(() => RecoveryPackage.Open(shortened, secret), Throws.Exception, $"Truncation {i}");
        }
        Assert.That(() => RecoveryPackage.Open([.. package, 0], secret), Throws.Exception);
        Assert.That(() => RecoveryPackage.Open(package, RandomNumberGenerator.GetBytes(32)), Throws.Exception);
        Assert.That(() => RecoveryPackage.Open(package, new byte[31]), Throws.Exception);
        Assert.That(() => RecoveryPackage.Open(new byte[RecoveryPackage.Limit + 1], secret), Throws.Exception);
    }

    [Test]
    public void RingRejectsDuplicateEmptyAndOversizedEntries()
    {
        using var ring = new KeyRingPayload(Guid.NewGuid(), 2);
        ring.Keys.Add(Guid.NewGuid(), new byte[32]);
        ring.Keys.Add(Guid.NewGuid(), new byte[32]);
        var encoded = ring.Encode();
        encoded.AsSpan(29, 16).CopyTo(encoded.AsSpan(77, 16));
        Assert.Throws<InvalidOperationException>(() => KeyRingPayload.Decode(encoded));
        encoded = ring.Encode(); encoded.AsSpan(29, 16).Clear();
        Assert.Throws<InvalidOperationException>(() => KeyRingPayload.Decode(encoded));
        encoded = ring.Encode(); BinaryPrimitives.WriteUInt32BigEndian(encoded.AsSpan(25), 4097);
        Assert.Throws<InvalidOperationException>(() => KeyRingPayload.Decode(encoded));
        encoded = ring.Encode(); encoded[0] = 2;
        Assert.Throws<InvalidOperationException>(() => KeyRingPayload.Decode(encoded));
        Assert.Throws<InvalidOperationException>(() => KeyRingPayload.Decode([.. ring.Encode(), 0]));
    }

    [Test]
    public void HkdfSha256MatchesRfc5869CaseOne()
    {
        var actual = HKDF.DeriveKey(HashAlgorithmName.SHA256, Enumerable.Repeat((byte)0x0b, 22).ToArray(), 42,
            Convert.FromHexString("000102030405060708090a0b0c"), Convert.FromHexString("f0f1f2f3f4f5f6f7f8f9"));
        Assert.That(Convert.ToHexString(actual).ToLowerInvariant(), Is.EqualTo("3cb25f25faacd57a90434f64d0362f2a2d2d0a90cf1a5a4c5db02d56ecc4c5bf34007208d5b887185865"));
    }
}
