namespace SA.Compression.BitIO;

/// <summary>Бітове читання MSB-first.</summary>
public sealed class BitReader
{
    private readonly byte[] _data;
    private long _position; // у бітах

    public BitReader(byte[] data) => _data = data ?? throw new ArgumentNullException(nameof(data));

    public long BitsRemaining => (long)_data.Length * 8 - _position;

    public int ReadBit()
    {
        if (BitsRemaining <= 0)
            throw new EndOfStreamException("Кінець бітового потоку.");

        int byteIndex = (int)(_position >> 3);
        int shift = 7 - (int)(_position & 7);
        _position++;
        return (_data[byteIndex] >> shift) & 1;
    }

    public uint ReadBits(int count)
    {
        if (count < 0 || count > 32)
            throw new ArgumentOutOfRangeException(nameof(count));
        uint value = 0;
        for (int i = 0; i < count; i++)
            value = (value << 1) | (uint)ReadBit();
        return value;
    }

    /// <summary>true, якщо всі непрочитані біти нульові (перевірка доповнення, E_CODE).</summary>
    public bool RemainingBitsAreZero()
    {
        while (BitsRemaining > 0)
            if (ReadBit() != 0) return false;
        return true;
    }
}