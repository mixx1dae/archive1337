using System.Security.Cryptography;
using SA.Core;

namespace SA.Crypto;

/// <summary>HMAC-SHA256 над шифротекстом і службовими полями (Encrypt-then-MAC).</summary>
public static class ArchiveMac
{
    public const int TagLength = 32;

    /// <summary>Рахує тег над переданими частинами у вказаному порядку.</summary>
    public static byte[] Compute(ReadOnlySpan<byte> macKey, params ReadOnlyMemory<byte>[] parts)
    {
        using var h = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, macKey.ToArray());
        foreach (var p in parts) h.AppendData(p.Span);
        return h.GetHashAndReset();
    }

    /// <summary>
    /// Перевіряє тег порівнянням сталого часу. Викликати до розшифрування і до будь-якої
    /// обробки вмісту. Невдача дає E_HMAC (зокрема й за невірного пароля).
    /// </summary>
    public static void Verify(ReadOnlySpan<byte> macKey, ReadOnlySpan<byte> expectedTag, params ReadOnlyMemory<byte>[] parts)
    {
        byte[] actual = Compute(macKey, parts);
        if (!CryptographicOperations.FixedTimeEquals(actual, expectedTag))
            throw new ArchiveException(ArchiveErrorCode.E_HMAC,
                "HMAC не збігається: архів змінено або пароль невірний.");
    }
}
