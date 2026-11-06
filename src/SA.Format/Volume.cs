using SA.Core;
using SA.Crypto;

namespace SA.Format;

public sealed record OpenedVolume(VolumeHeader Header, VolumePayload Payload);

/// <summary>Збирання тому: заголовок, AES-256-CTR, HMAC (Encrypt-then-MAC).</summary>
public static class VolumeWriter
{
    public const byte FormatVersion = 1;

    /// <summary>Рекомендований nonce зі специфікації §4.2: 8 байтів префікса архіву || uint32_BE(volume_index).</summary>
    public static byte[] MakeNonce(ReadOnlySpan<byte> archivePrefix8, ushort volumeIndex)
    {
        if (archivePrefix8.Length != 8)
            throw new ArgumentException("Префікс nonce має бути 8 байтів.", nameof(archivePrefix8));
        var nonce = new byte[VolumeHeader.NonceSize];
        archivePrefix8.CopyTo(nonce);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(8), volumeIndex);
        return nonce;
    }

    public static byte[] Build(
        DerivedKeys keys, byte[] salt, byte[] nonce, ushort volumeIndex, ushort volumeCount, VolumePayload payload)
        => BuildRaw(keys, salt, nonce, volumeIndex, volumeCount, payload.Serialize());

    /// <summary>Те саме для вже серіалізованого навантаження (потрібно й для тестів на пошкоджені дані).</summary>
    public static byte[] BuildRaw(
        DerivedKeys keys, byte[] salt, byte[] nonce, ushort volumeIndex, ushort volumeCount, byte[] plainPayload)
    {
        byte[] ciphertext = AesCtr.Transform(keys.EncKey, nonce, plainPayload);
        var header = new VolumeHeader(FormatVersion, volumeIndex, volumeCount, salt, nonce, (uint)ciphertext.Length);

        var volume = new byte[VolumeHeader.Size + ciphertext.Length + VolumeHeader.TagSize];
        header.ToBytes().CopyTo(volume, 0);
        ciphertext.CopyTo(volume, VolumeHeader.Size);

        int authLen = VolumeHeader.Size + ciphertext.Length;
        byte[] tag = ArchiveMac.Compute(keys.MacKey, new ReadOnlyMemory<byte>(volume, 0, authLen));
        tag.CopyTo(volume, authLen);
        return volume;
    }
}

/// <summary>Читання одного тому в порядку розділу 4.3: довжина, magic, HMAC, розшифрування, розбір.</summary>
public static class VolumeReader
{
    /// <param name="allowPrediction">
    /// PREDICTION є командним розширенням. Без підтримки том з цим прапорцем відхиляється з E_PAYLOAD.
    /// </param>
    public static OpenedVolume Open(byte[] volume, DerivedKeys keys, bool allowPrediction = false)
    {
        var header = VolumeHeader.Parse(volume); // E_SHORT, E_MAGIC, E_LENGTH

        int authLen = VolumeHeader.Size + (int)header.CiphertextLength;
        ArchiveMac.Verify(                         // E_HMAC, до будь-якого розшифрування
            keys.MacKey,
            volume.AsSpan(authLen, VolumeHeader.TagSize),
            new ReadOnlyMemory<byte>(volume, 0, authLen));

        byte[] ciphertext = volume.AsSpan(VolumeHeader.Size, (int)header.CiphertextLength).ToArray();
        byte[] plain = AesCtr.Transform(keys.EncKey, header.Nonce, ciphertext);

        var payload = VolumePayload.Parse(plain);  // E_PAYLOAD
        if (!allowPrediction && (payload.Flags & VolumePayload.FlagPrediction) != 0)
            throw new ArchiveException(ArchiveErrorCode.E_PAYLOAD, "невідоме розширення PREDICTION.");

        return new OpenedVolume(header, payload);
    }
}
