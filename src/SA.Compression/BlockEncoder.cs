using SA.Compression.BitIO;
using SA.Compression.Huffman;
using SA.Compression.Lz77;

namespace SA.Compression;

/// <summary>
/// Збирає закодований бінарний блок згідно з розділом 3.1 специфікації:
/// [таблиця LIT] | [таблиця LEN] | [таблиця DIST] | token_count (4) | stream_len (4) | бітовий потік.
/// </summary>
public static class BlockEncoder
{
    public static byte[] Encode(IReadOnlyList<Token> tokens)
    {
        // Крок 1. Рахуємо частоти появи кожного символу у трьох окремих алфавітах
        var litFreq = new long[CodecConstants.LitAlphabetSize];
        var lenFreq = new long[CodecConstants.LenAlphabetSize];
        var distFreq = new long[CodecConstants.DistAlphabetSize];

        foreach (var t in tokens)
        {
            if (t.IsLiteral)
            {
                // Якщо це звичайний байт — збільшуємо лічильник літерала
                litFreq[t.Byte]++;
            }
            else
            {
                // Валідація коректності меж посилання
                if (t.Length < CodecConstants.MinMatch || t.Length > CodecConstants.MaxMatch)
                    throw new ArgumentException($"довжина збігу {t.Length} поза 3…258");
                if (t.Distance < 1 || t.Distance > CodecConstants.WindowSize)
                    throw new ArgumentException($"відстань {t.Distance} поза 1…32768");

                litFreq[CodecConstants.MatchMarker]++;          // Додаємо маркер посилання 256
                lenFreq[t.Length - CodecConstants.MinMatch]++;  // Додаємо зміщену довжину
                distFreq[DistanceCoding.Code(t.Distance)]++;    // Додаємо код відстані
            }
        }

        // Крок 2. Будуємо оптимальні довжини і канонічні коди Гаффмана
        var litLengths = HuffmanBuilder.BuildLengths(litFreq);
        var lenLengths = HuffmanBuilder.BuildLengths(lenFreq);
        var distLengths = HuffmanBuilder.BuildLengths(distFreq);

        var litCode = new CanonicalCode(litLengths);
        var lenCode = new CanonicalCode(lenLengths);
        var distCode = new CanonicalCode(distLengths);

        // Крок 3. Формуємо бітовий потік даних (розділ 3.5 специфікації)
        var writer = new BitWriter();
        foreach (var t in tokens)
        {
            if (t.IsLiteral)
            {
                litCode.Write(writer, t.Byte); // Записуємо код звичайного байта
                continue;
            }

            // Записуємо маркер повтору
            litCode.Write(writer, CodecConstants.MatchMarker);

            // Записуємо код довжини
            lenCode.Write(writer, t.Length - CodecConstants.MinMatch);

            // Записуємо код відстані та її додаткові extra-біти
            int code = DistanceCoding.Code(t.Distance);
            distCode.Write(writer, code);

            int extra = DistanceCoding.ExtraBits(code);
            if (extra > 0)
                writer.Write(DistanceCoding.ExtraValue(t.Distance, extra), extra);
        }

        // Отримуємо байтовий масив потоку (неповний останній байт доповнюється нулями)
        byte[] stream = writer.ToArray();

        // Крок 4. Компонуємо фінальний бінарний блок у пам'яті
        var output = new List<byte>(stream.Length + 64);

        // Записуємо таблиці довжин кодів
        CodeTable.Write(output, litLengths);
        CodeTable.Write(output, lenLengths);
        CodeTable.Write(output, distLengths);

        // Записуємо кількість токенів і довжину потоку (по 4 байти Big-Endian)
        WriteUInt32(output, (uint)tokens.Count);
        WriteUInt32(output, (uint)stream.Length);

        // Дописуємо бітовий потік
        output.AddRange(stream);
        return output.ToArray();
    }

    /// <summary>
    /// Записує 32-бітне ціле в масив у порядку Big-Endian (старший байт іде першим)
    /// </summary>
    private static void WriteUInt32(List<byte> output, uint v)
    {
        output.Add((byte)(v >> 24));
        output.Add((byte)(v >> 16));
        output.Add((byte)(v >> 8));
        output.Add((byte)v);
    }
}