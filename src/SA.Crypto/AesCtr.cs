using System.Buffers.Binary;
using System.Security.Cryptography;

namespace SA.Crypto;

/// <summary>AES-256-CTR: counter_block(i) = nonce(12) || uint32_BE(i), i від 1 (FORMAT_SPEC §4.2).</summary>
public static class AesCtr
{
    public const int KeyLength = 32;
    public const int NonceLength = 12;
    private const int BlockSize = 16;

    public static byte[] GenerateNonce() => RandomNumberGenerator.GetBytes(NonceLength);

    /// <summary>Шифрування і розшифрування збігаються. <paramref name="firstBlock"/> — номер першого блока лічильника.</summary>
    public static byte[] Transform(byte[] key, byte[] nonce, byte[] data, uint firstBlock = 1)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(nonce);
        ArgumentNullException.ThrowIfNull(data);
        if (key.Length != KeyLength)
            throw new ArgumentException("Ключ AES-256 має бути 32 байти.", nameof(key));
        if (nonce.Length != NonceLength)
            throw new ArgumentException("Nonce має бути 12 байтів.", nameof(nonce));

        long blocks = ((long)data.Length + BlockSize - 1) / BlockSize;
        if ((long)firstBlock + blocks - 1 > uint.MaxValue)
            throw new ArgumentException("Переповнення 32-бітного лічильника CTR.", nameof(data));

        var result = new byte[data.Length];
        using var aes = Aes.Create();
        aes.Key = key;

        Span<byte> counter = stackalloc byte[BlockSize];
        Span<byte> ks = stackalloc byte[BlockSize];
        nonce.CopyTo(counter);

        for (long b = 0; b < blocks; b++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(counter[NonceLength..], (uint)(firstBlock + b));
            aes.EncryptEcb(counter, ks, PaddingMode.None);
            int off = (int)(b * BlockSize);
            int n = Math.Min(BlockSize, data.Length - off);
            for (int i = 0; i < n; i++) result[off + i] = (byte)(data[off + i] ^ ks[i]);
        }
        return result;
    }
}
