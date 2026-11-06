using System.Security.Cryptography;
using System.Text;

namespace SA.Crypto;

/// <summary>Ключі, виведені з пароля: K_enc (AES-256) і K_mac (HMAC-SHA256) — розділені (FORMAT_SPEC §4.1).</summary>
public readonly record struct DerivedKeys(byte[] EncKey, byte[] MacKey);

/// <summary>PBKDF2-HMAC-SHA256, 100 000 ітерацій, 64 байти: перші 32 — K_enc, останні 32 — K_mac.</summary>
public static class KeyDerivation
{
    public const int SaltLength = 16;
    public const int Iterations = 100_000;

    public static byte[] GenerateSalt() => RandomNumberGenerator.GetBytes(SaltLength);

    public static DerivedKeys Derive(string password, ReadOnlySpan<byte> salt)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (salt.Length != SaltLength)
            throw new ArgumentException($"Сіль має бути {SaltLength} байтів.", nameof(salt));

        byte[] master = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, 64);
        return new DerivedKeys(master[..32], master[32..]);
    }
}
