using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using HomeVault.Infrastructure.Encryption;
using NUnit.Framework;

namespace HomeVault.Infrastructure.Tests;

public sealed class EncryptionEnvelopeTests
{
    private static readonly Guid KeyId = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
    private static readonly EncryptionContext Context = new(Guid.Parse("11111111-1111-1111-1111-111111111111"), Guid.Parse("22222222-2222-2222-2222-222222222222"), Guid.Parse("33333333-3333-3333-3333-333333333333"));
    // Independently assembled header/AAD and framework AES-GCM; fixed wire-format regression fixture.
    private const string Fixture = "485641450100112233445566778899AABBCCDDEEFF000102030405060708090A0B000000043367A56FBE1BDC88AE285BAFDAA7F01F4CBAA45B";
    private static byte[] FixtureKey() => Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();

    // GCM specification cases 13/14, also published in dotnet/runtime AesGcmTests.
    [TestCase(0, "", "530f8afbc74536b9a963b4f1c4cb738b")]
    [TestCase(16, "cea7403d4d606b6e074ec5d3baf39d18", "d0d1c8a799996bf0265b98b5d48ab919")]
    public void PublishedAes256KnownAnswers(int length, string expectedCiphertext, string expectedTag)
    {
        var plaintext = new byte[length]; var ciphertext = new byte[length]; var tag = new byte[16];
        using var aes = new AesGcm(new byte[32], 16);
        aes.Encrypt(new byte[12], plaintext, ciphertext, tag);
        Assert.That(ciphertext, Is.EqualTo(Convert.FromHexString(expectedCiphertext)));
        Assert.That(tag, Is.EqualTo(Convert.FromHexString(expectedTag)));
        using var lease = new ReadKeyLease(KeyId, new byte[32]); var restored = new byte[length];
        lease.Decrypt(new byte[12], ciphertext, tag, restored, []);
        Assert.That(restored, Is.EqualTo(plaintext));
    }

    [Test]
    public void FixedEnvelopeAuthenticatesCanonicalNetworkOrderAndPurpose()
    {
        using var session = new WriteKeySession(KeyId, FixtureKey(), nonce => { for (var i = 0; i < nonce.Length; i++) nonce[i] = (byte)i; });
        Assert.That(Convert.ToHexString(session.Encrypt(Context, "test").ReadValue()), Is.EqualTo(Fixture));
        var custody = new TestCustody { Key = FixtureKey() };
        Assert.That(new EnvelopeEncryption(custody).Decrypt(Context, Convert.FromHexString(Fixture)).ReadValue(), Is.EqualTo("test"));
        Assert.That(custody.LastLeaseBuffer, Is.All.Zero);
    }
    [TestCase("")]
    [TestCase(" fictional private text \r\n\t")]
    [TestCase(" فارسی 😀 e\u0301 \0 ")]
    public void RoundTripsPreserveExactTextAndSafeSerialization(string text)
    {
        var custody = new TestCustody(); var service = new EnvelopeEncryption(custody);
        using var session = service.BeginWrite().ReadValue();
        var envelope = session.Encrypt(Context, text); Assert.That(envelope.Succeeded, Is.True);
        var decrypted = service.Decrypt(Context, envelope.ReadValue()); Assert.That(decrypted.ReadValue(), Is.EqualTo(text));
        Assert.That(JsonSerializer.Serialize(decrypted), Is.EqualTo("{\"Error\":0,\"Succeeded\":true}"));
        Assert.That(decrypted.ToString(), Is.EqualTo("EncryptionResult"));
        Assert.That(session.ToString(), Is.EqualTo("WriteKeySession"));
    }
    [Test]
    public void EveryEnvelopeByteMutationFailsWithoutPlaintext()
    {
        var original = Convert.FromHexString(Fixture); var service = new EnvelopeEncryption(new TestCustody { Key = FixtureKey() });
        for (var index = 0; index < original.Length; index++)
        {
            var mutated = original.ToArray(); mutated[index] ^= 1;
            var result = service.Decrypt(Context, mutated);
            Assert.That(result.Succeeded, Is.False, $"Unexpected success at byte {index}.");
            Assert.Throws<InvalidOperationException>(() => result.ReadValue());
        }
    }
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void SwappingAnyRecordIdentityFails(int identity)
    {
        var context = identity switch { 0 => Context with { VaultId = Guid.NewGuid() }, 1 => Context with { AssetId = Guid.NewGuid() }, _ => Context with { AttributeId = Guid.NewGuid() } };
        Assert.That(new EnvelopeEncryption(new TestCustody { Key = FixtureKey() }).Decrypt(context, Convert.FromHexString(Fixture)).Error, Is.EqualTo(EncryptionError.AuthenticationFailed));
    }
    [Test]
    public void InvalidFramingAndContextsNeverLookUpKeys()
    {
        var custody = new TestCustody { Key = FixtureKey() }; var service = new EnvelopeEncryption(custody); var fixture = Convert.FromHexString(Fixture);
        for (var length = 0; length < fixture.Length; length++) Assert.That(service.Decrypt(Context, fixture.AsSpan(0, length)).Succeeded, Is.False);
        Assert.That(service.Decrypt(Context, [.. fixture, 0]).Error, Is.EqualTo(EncryptionError.InvalidPayload));
        var version = fixture.ToArray(); version[4] = 2;
        Assert.That(service.Decrypt(Context, version).Error, Is.EqualTo(EncryptionError.UnsupportedVersion));
        var emptyId = fixture.ToArray(); emptyId.AsSpan(5, 16).Clear();
        Assert.That(service.Decrypt(Context, emptyId).Error, Is.EqualTo(EncryptionError.InvalidPayload));
        var huge = fixture.ToArray(); BinaryPrimitives.WriteUInt32BigEndian(huge.AsSpan(33, 4), uint.MaxValue);
        Assert.That(service.Decrypt(Context, huge).Error, Is.EqualTo(EncryptionError.TooLarge));
        Assert.That(service.Decrypt(default, fixture).Error, Is.EqualTo(EncryptionError.InvalidContext));
        Assert.That(custody.Reads, Is.Zero);
    }
    [Test]
    public void MissingWrongAndMismatchedKeysFailSafely()
    {
        var fixture = Convert.FromHexString(Fixture);
        var custody = new TestCustody { Missing = true };
        Assert.That(new EnvelopeEncryption(custody).Decrypt(Context, fixture).Error, Is.EqualTo(EncryptionError.MissingKey));
        custody.Missing = false;
        Assert.That(new EnvelopeEncryption(custody).Decrypt(Context, fixture).Error, Is.EqualTo(EncryptionError.AuthenticationFailed));
        custody.WrongId = true;
        Assert.That(new EnvelopeEncryption(custody).Decrypt(Context, fixture).Error, Is.EqualTo(EncryptionError.CustodyUnavailable));
        Assert.That(custody.LastLeaseBuffer, Is.All.Zero);
    }
    [Test]
    public void StrictEncodingAndSizeBoundPreserveDataWithoutReplacement()
    {
        var custody = new TestCustody(); var service = new EnvelopeEncryption(custody); using var session = service.BeginWrite().ReadValue();
        Assert.That(session.Encrypt(Context, "\ud800").Error, Is.EqualTo(EncryptionError.InvalidText));
        Assert.That(session.Encrypt(Context, null).Error, Is.EqualTo(EncryptionError.InvalidText));
        Assert.That(session.Encrypt(Context, new string('a', 1024 * 1024 + 1)).Error, Is.EqualTo(EncryptionError.TooLarge));
        Assert.That(session.Encrypt(Context, new string('é', 1024 * 1024)).Error, Is.EqualTo(EncryptionError.TooLarge));
        var maximum = new string('a', 1024 * 1024);
        Assert.That(service.Decrypt(Context, session.Encrypt(Context, maximum).ReadValue()).ReadValue(), Is.EqualTo(maximum));
        // Valid authenticated bytes with invalid UTF-8 must not be decoded with replacement characters.
        var envelope = EnvelopeFormat.Allocate(KeyId, new byte[12], 1);
        using var aes = new AesGcm(custody.Key, 16);
        aes.Encrypt(new byte[12], new byte[] { 0xff }, envelope.AsSpan(37, 1), envelope.AsSpan(38, 16), EnvelopeFormat.AssociatedData(envelope.AsSpan(0, 37), Context));
        Assert.That(service.Decrypt(Context, envelope).Error, Is.EqualTo(EncryptionError.InvalidText));
    }
    [Test]
    public void DuplicateNonceRetriesAreBoundedAndFailedReservationIsNotRefunded()
    {
        var calls = 0; using var session = new WriteKeySession(KeyId, new byte[32], nonce => { calls++; nonce.AsSpan().Clear(); });
        Assert.That(session.Encrypt(Context, "\ud800").Error, Is.EqualTo(EncryptionError.InvalidText));
        Assert.That(session.Encrypt(Context, "valid").Error, Is.EqualTo(EncryptionError.NonceUnavailable));
        Assert.That(calls, Is.EqualTo(10));
        var retries = 0;
        using var recover = new WriteKeySession(KeyId, new byte[32], nonce => { retries++; nonce[0] = retries <= 2 ? (byte)0 : (byte)1; });
        Assert.That(recover.Encrypt(Context, "one").Succeeded, Is.True);
        Assert.That(recover.Encrypt(Context, "two").Succeeded, Is.True);
        Assert.That(retries, Is.EqualTo(3));
    }
    [Test]
    public void ExactReservationLimitIsSharedAcrossConcurrentCallers()
    {
        var sequence = 0; var ownedKey = FixtureKey();
        using var session = new WriteKeySession(KeyId, ownedKey, nonce => BinaryPrimitives.WriteInt32BigEndian(nonce, ++sequence));
        // Each invalid text still consumes a successfully reserved nonce, without expensive payload allocation.
        Parallel.For(0, WriteKeySession.ReservationLimit, _ => Assert.That(session.Encrypt(Context, null).Error, Is.EqualTo(EncryptionError.InvalidText)));
        Assert.That(session.Encrypt(Context, "next").Error, Is.EqualTo(EncryptionError.SessionExhausted));
        Assert.That(sequence, Is.EqualTo(65_536));
        session.Dispose(); Assert.That(ownedKey, Is.All.Zero);
        Assert.That(session.Encrypt(Context, "next").Error, Is.EqualTo(EncryptionError.SessionDisposed));
    }
    [Test]
    public async Task ConcurrentSuccessfulWritesHaveDistinctNoncesAndRemainReadable()
    {
        var custody = new TestCustody(); var service = new EnvelopeEncryption(custody); using var session = service.BeginWrite().ReadValue();
        var results = await Task.WhenAll(Enumerable.Range(0, 128).Select(_ => Task.Run(() => session.Encrypt(Context, "fictional"))));
        Assert.That(results.Select(result => Convert.ToHexString(result.ReadValue().AsSpan(21, 12))).Distinct().Count(), Is.EqualTo(128));
        foreach (var result in results) Assert.That(service.Decrypt(Context, result.ReadValue()).ReadValue(), Is.EqualTo("fictional"));
    }
    [Test]
    public void ReadOnlyCustodyCannotIssueWritesAndProviderDetailsAreSuppressed()
    {
        var custody = new TestCustody { ReadOnly = true, Key = FixtureKey() }; var service = new EnvelopeEncryption(custody);
        Assert.That(service.BeginWrite().Error, Is.EqualTo(EncryptionError.CustodyUnavailable));
        Assert.That(service.Decrypt(Context, Convert.FromHexString(Fixture)).ReadValue(), Is.EqualTo("test"));
        custody.Fail = true;
        Assert.That(service.BeginWrite().Error, Is.EqualTo(EncryptionError.CustodyUnavailable));
        Assert.That(service.Decrypt(Context, Convert.FromHexString(Fixture)).Error, Is.EqualTo(EncryptionError.CustodyUnavailable));
        Assert.That(typeof(ReadKeyLease).GetMethods().Any(method => method.Name.Contains("Encrypt", StringComparison.Ordinal)), Is.False);
        Assert.That(typeof(WriteKeySession).IsPublic, Is.False);
    }
    [Test]
    public void DisposedReadLeaseCannotDecryptAndClearsOwnedBuffer()
    {
        var buffer = FixtureKey(); using var lease = new ReadKeyLease(KeyId, buffer); lease.Dispose(); lease.Dispose();
        Assert.That(buffer, Is.All.Zero);
        Assert.Throws<ObjectDisposedException>(() => lease.Decrypt(new byte[12], [], new byte[16], [], []));
    }
    [Test]
    public void EntropyFailureIsSafeAndDoesNotLeakExceptionMessage()
    {
        using var session = new WriteKeySession(KeyId, new byte[32], _ => throw new CryptographicException("fictional provider secret"));
        var result = session.Encrypt(Context, "fictional plaintext");
        Assert.That(result.Error, Is.EqualTo(EncryptionError.EncryptionFailed));
        Assert.That(JsonSerializer.Serialize(result), Does.Not.Contain("fictional"));
    }
    [Test]
    public void NewSessionsUseFreshKeysAndRetainedKeysRemainReadable()
    {
        var custody = new TestCustody(); var service = new EnvelopeEncryption(custody);
        using var first = service.BeginWrite().ReadValue(); var original = first.Encrypt(Context, "first").ReadValue(); first.Dispose();
        using var second = service.BeginWrite().ReadValue(); var later = second.Encrypt(Context, "second").ReadValue();
        Assert.That(original.AsSpan(5, 16).ToArray(), Is.Not.EqualTo(later.AsSpan(5, 16).ToArray()));
        custody.ReadOnly = true;
        Assert.That(service.BeginWrite().Error, Is.EqualTo(EncryptionError.CustodyUnavailable));
        Assert.That(service.Decrypt(Context, original).ReadValue(), Is.EqualTo("first"));
        Assert.That(service.Decrypt(Context, later).ReadValue(), Is.EqualTo("second"));
    }
    [Test]
    public void KeyIdIsAuthenticatedEvenWhenProviderReturnsSameKeyUnderAnotherId()
    {
        var envelope = Convert.FromHexString(Fixture); var otherId = Guid.NewGuid(); otherId.TryWriteBytes(envelope.AsSpan(5, 16), true, out _);
        var custody = new TestCustody(); custody.Retained.Add(otherId, FixtureKey());
        Assert.That(new EnvelopeEncryption(custody).Decrypt(Context, envelope).Error, Is.EqualTo(EncryptionError.AuthenticationFailed));
    }
    private sealed class TestCustody : IEncryptionKeyCustody
    {
        internal byte[] Key = RandomNumberGenerator.GetBytes(32);
        internal bool Missing, WrongId, ReadOnly, Fail;
        internal int Reads;
        internal byte[]? LastLeaseBuffer;
        internal readonly Dictionary<Guid, byte[]> Retained = [];
        public WriteKeySession? CreateVerifiedWriteSession()
        {
            if (Fail) throw new IOException("fictional private provider details");
            if (ReadOnly) return null;
            var id = Guid.NewGuid(); var fresh = RandomNumberGenerator.GetBytes(32);
            Retained.Add(id, fresh);
            return new WriteKeySession(id, fresh.ToArray());
        }
        public ReadKeyLease? FindReadKey(Guid keyId)
        {
            Reads++;
            if (Fail) throw new IOException("fictional private provider details");
            if (Missing) return null;
            var key = keyId == KeyId ? Key : Retained.GetValueOrDefault(keyId);
            if (key is null) return null;
            LastLeaseBuffer = key.ToArray();
            return new ReadKeyLease(WrongId ? Guid.NewGuid() : keyId, LastLeaseBuffer);
        }
    }
}
