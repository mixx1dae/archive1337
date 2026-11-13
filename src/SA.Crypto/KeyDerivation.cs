using System.Security.Cryptography;
using System.Text;

namespace SA.Crypto;

/// <summary>Пара ключів: K_enc для AES, K_mac для HMAC. Використовувати один ключ для обох не можна.</summary>
public readonly record struct DerivedKeys(byte[] EncKey, byte[] MacKey);

/// <summary>PBKDF2-HMAC-SHA256, 100 000 ітерацій, сіль 16 байт, результат 64 байти (методичка, п. 3.5).</summary>
public static class KeyDerivation
{
    public const int Iterations = 100_000;
    public const int SaltLength = 16;
    public const int KeyLength = 32;

    public static byte[] GenerateSalt() => RandomNumberGenerator.GetBytes(SaltLength);

    /// <summary>Пароль кодується в UTF-8. Якщо специфікація каже інакше, змініть тут.</summary>
    public static DerivedKeys Derive(string password, ReadOnlySpan<byte> salt)
    {
        ArgumentNullException.ThrowIfNull(password);
        return Derive(Encoding.UTF8.GetBytes(password), salt);
    }

    public static DerivedKeys Derive(ReadOnlySpan<byte> passwordBytes, ReadOnlySpan<byte> salt)
    {
        if (salt.Length != SaltLength)
            throw new ArgumentException($"Сіль має бути {SaltLength} байтів.", nameof(salt));

        // Один виклик на архів: сіль однакова для всіх томів, отже й ключі теж.
        byte[] okm = Rfc2898DeriveBytes.Pbkdf2(
            passwordBytes, salt, Iterations, HashAlgorithmName.SHA256, 2 * KeyLength);
        try
        {
            return new DerivedKeys(okm[..KeyLength], okm[KeyLength..]);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(okm);
        }
    }
}
