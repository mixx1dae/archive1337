using System.Buffers.Binary;
using SA.Core;

namespace SA.Format;

/// <summary>Заголовок тому, 41 байт відкритого тексту (FORMAT_SPEC §1.1).</summary>
public sealed record VolumeHeader(
    byte Version,
    ushort VolumeIndex,
    ushort VolumeCount,
    byte[] Salt,
    byte[] Nonce,
    uint CiphertextLength)
{
    public const int Size = 41;
    public const int TagSize = 32;
    public const int MinVolumeLength = Size + TagSize; // 73
    public const int SaltSize = 16;
    public const int NonceSize = 12;

    public static readonly byte[] Magic = { 0x53, 0x41, 0x52, 0x43 }; // "SARC"

    public byte[] ToBytes()
    {
        if (Salt.Length != SaltSize) throw new ArgumentException("Сіль має бути 16 байтів.");
        if (Nonce.Length != NonceSize) throw new ArgumentException("Nonce має бути 12 байтів.");

        var b = new byte[Size];
        Magic.CopyTo(b, 0);
        b[4] = Version;
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(5), VolumeIndex);
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(7), VolumeCount);
        Salt.CopyTo(b, 9);
        Nonce.CopyTo(b, 25);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(37), CiphertextLength);
        return b;
    }

    /// <summary>
    /// Кроки 1–3 розділу 4.3: довжина ≥ 73 (E_SHORT), magic і version (E_MAGIC),
    /// точна відповідність довжини тому полю ciphertext_len (E_LENGTH).
    /// </summary>
    public static VolumeHeader Parse(ReadOnlySpan<byte> volume)
    {
        if (volume.Length < MinVolumeLength)
            throw new ArchiveException(ArchiveErrorCode.E_SHORT,
                $"том має {volume.Length} байтів, мінімум {MinVolumeLength}.");

        if (!volume[..4].SequenceEqual(Magic.AsSpan()) || volume[4] != 1)
            throw new ArchiveException(ArchiveErrorCode.E_MAGIC, "невірний magic або version.");

        ushort index = BinaryPrimitives.ReadUInt16BigEndian(volume[5..]);
        ushort count = BinaryPrimitives.ReadUInt16BigEndian(volume[7..]);
        byte[] salt = volume.Slice(9, SaltSize).ToArray();
        byte[] nonce = volume.Slice(25, NonceSize).ToArray();
        uint ctLen = BinaryPrimitives.ReadUInt32BigEndian(volume[37..]);

        if ((long)volume.Length != Size + (long)ctLen + TagSize)
            throw new ArchiveException(ArchiveErrorCode.E_LENGTH,
                "довжина тому не відповідає ciphertext_len.");

        return new VolumeHeader(volume[4], index, count, salt, nonce, ctLen);
    }
}
