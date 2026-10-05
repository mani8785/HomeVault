using System.Buffers.Binary;
using System.Security.Cryptography;

namespace HomeVault.Infrastructure.Encryption;

/// <summary>Owned binary ring: version, network-order ID, generation, count, then fixed ID/key entries.</summary>
internal sealed class KeyRingPayload(Guid id, ulong generation) : IDisposable
{
    internal const int MaximumKeys = 4096;
    internal Guid Id { get; } = id;
    internal ulong Generation { get; set; } = generation;
    internal Dictionary<Guid, byte[]> Keys { get; } = [];
    internal byte[] Encode()
    {
        if (Id == Guid.Empty || Keys.Count > MaximumKeys) throw new InvalidOperationException();
        var bytes = new byte[29 + Keys.Count * 48]; bytes[0] = 1;
        Id.TryWriteBytes(bytes.AsSpan(1, 16), true, out _);
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(17, 8), Generation);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(25, 4), (uint)Keys.Count);
        var offset = 29;
        foreach (var entry in Keys.OrderBy(pair => pair.Key))
        {
            if (entry.Key == Guid.Empty || entry.Value.Length != 32) { CryptographicOperations.ZeroMemory(bytes); throw new InvalidOperationException(); }
            entry.Key.TryWriteBytes(bytes.AsSpan(offset, 16), true, out _); entry.Value.CopyTo(bytes, offset + 16); offset += 48;
        }
        return bytes;
    }
    internal static KeyRingPayload Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 29 || bytes[0] != 1) throw new InvalidOperationException();
        var id = new Guid(bytes.Slice(1, 16), true); var count = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(25, 4));
        if (id == Guid.Empty || count > MaximumKeys || bytes.Length != 29 + count * 48) throw new InvalidOperationException();
        var ring = new KeyRingPayload(id, BinaryPrimitives.ReadUInt64BigEndian(bytes.Slice(17, 8)));
        try
        {
            for (var offset = 29; offset < bytes.Length; offset += 48)
            {
                var keyId = new Guid(bytes.Slice(offset, 16), true);
                if (keyId == Guid.Empty || ring.Keys.ContainsKey(keyId)) throw new InvalidOperationException();
                ring.Keys.Add(keyId, bytes.Slice(offset + 16, 32).ToArray());
            }
            return ring;
        }
        catch { ring.Dispose(); throw; }
    }
    public void Dispose() { foreach (var key in Keys.Values) CryptographicOperations.ZeroMemory(key); Keys.Clear(); }
    public override string ToString() => nameof(KeyRingPayload);
}
