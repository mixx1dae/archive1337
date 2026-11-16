using System.Buffers.Binary;
using System.Security.Cryptography;

namespace SA.Crypto;

/// <summary>
/// AES-256-CTR. Блок лічильника: nonce(12) || uint32_BE(i), нумерація з 1.
/// У .NET немає вбудованого CTR, тому потік ключів будується через AES-ECB.
/// Шифрування і розшифрування збігаються (XOR із потоком ключів).
/// </summary>
public static class AesCtr
{
    public const int KeyLength = 32;
    public const int NonceLength = 12;
    public const int BlockSize = 16;

    /// <param name="firstBlock">Номер першого блока лічильника. За специфікацією 1.</param>
    public static byte[] Transform(
        ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> input, uint firstBlock = 1)
    {
        if (key.Length != KeyLength)
            throw new ArgumentException($"Ключ має бути {KeyLength} байтів.", nameof(key));
        if (nonce.Length != NonceLength)
            throw new ArgumentException($"Nonce має бути {NonceLength} байтів.", nameof(nonce));
        if (input.Length == 0) return Array.Empty<byte>();

        long blocks = ((long)input.Length + BlockSize - 1) / BlockSize;
        if ((long)firstBlock + blocks - 1 > uint.MaxValue)
            throw new ArgumentException("Лічильник CTR переповниться: дані завеликі для одного nonce.", nameof(input));

        var output = new byte[input.Length];
        using var aes = Aes.Create();
        aes.Key = key.ToArray();

        Span<byte> counter = stackalloc byte[BlockSize];
        Span<byte> keystream = stackalloc byte[BlockSize];
        nonce.CopyTo(counter);

        uint blockIndex = firstBlock;
        for (int offset = 0; offset < input.Length; offset += BlockSize)
        {
            BinaryPrimitives.WriteUInt32BigEndian(counter[NonceLength..], blockIndex);
            aes.EncryptEcb(counter, keystream, PaddingMode.None);

            int n = Math.Min(BlockSize, input.Length - offset);
            for (int j = 0; j < n; j++)
                output[offset + j] = (byte)(input[offset + j] ^ keystream[j]);

            blockIndex++;
        }

        CryptographicOperations.ZeroMemory(keystream);
        return output;
    }

    public static byte[] GenerateNonce() => RandomNumberGenerator.GetBytes(NonceLength);
}
