namespace SA.Compression;

/// <summary>Кодування відстані LZ77: dist (1..32768) ⇄ (code 0..15, extra біти).</summary>
public static class DistanceCoder
{
    public const int MinDistance = 1;
    public const int MaxDistance = 32768;
    public const int MaxCode = 15;

    public static (int Code, uint ExtraBits, int ExtraCount) Encode(int dist)
    {
        if (dist < MinDistance || dist > MaxDistance)
            throw new ArgumentOutOfRangeException(nameof(dist));

        uint d = (uint)(dist - 1);
        int code = d == 0 ? 0 : 32 - System.Numerics.BitOperations.LeadingZeroCount(d);
        int extraCount = Math.Max(0, code - 1);
        uint extraBits = extraCount > 0 ? d & ((1u << extraCount) - 1u) : 0u;
        return (code, extraBits, extraCount);
    }

    public static int ExtraBitCount(int code)
    {
        ValidateCode(code);
        return Math.Max(0, code - 1);
    }

    public static int Decode(int code, uint extraBits)
    {
        ValidateCode(code);
        if (code == 0) return 1;
        if (code == 1) return 2;
        return (int)((1u << (code - 1)) | extraBits) + 1;
    }

    private static void ValidateCode(int code)
    {
        if (code < 0 || code > MaxCode)
            throw new ArgumentOutOfRangeException(nameof(code), "Код відстані поза 0..15 (E_CODE).");
    }
}