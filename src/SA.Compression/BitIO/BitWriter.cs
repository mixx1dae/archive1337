namespace SA.Compression.BitIO;

/// <summary>Бітовий запис MSB-first: перший біт стає 0x80 першого байта.</summary>
public sealed class BitWriter
{
    private readonly List<byte> _bytes = new();
    private int _current;   // накопичений неповний байт
    private int _bitCount;  // скільки бітів у _current (0..7)

    public long BitLength => (long)_bytes.Count * 8 + _bitCount;

    public void WriteBit(int bit)
    {
        if (bit != 0 && bit != 1)
            throw new ArgumentOutOfRangeException(nameof(bit));

        _current = (_current << 1) | bit;
        _bitCount++;
        if (_bitCount == 8)
        {
            _bytes.Add((byte)_current);
            _current = 0;
            _bitCount = 0;
        }
    }

    /// <summary>Записує <paramref name="count"/> молодших бітів value, старшим бітом уперед.</summary>
    public void WriteBits(uint value, int count)
    {
        if (count < 0 || count > 32)
            throw new ArgumentOutOfRangeException(nameof(count));
        for (int i = count - 1; i >= 0; i--)
            WriteBit((int)((value >> i) & 1u));
    }

    /// <summary>Повертає байти; останній байт доповнено нулями. Стан не змінюється.</summary>
    public byte[] ToArray()
    {
        var result = new List<byte>(_bytes);
        if (_bitCount > 0)
            result.Add((byte)(_current << (8 - _bitCount)));
        return result.ToArray();
    }
}