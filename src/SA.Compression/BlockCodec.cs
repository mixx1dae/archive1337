using SA.Compression.Lz77;
using SA.Core;

namespace SA.Compression;

/// <summary>
/// Публічний фасад зони стиснення для базового рівня.
/// Керує процесами стиснення блоків, перевіркою ефективності та декомпресією.
/// </summary>
public static class BlockCodec
{
    /// <summary>
    /// Стискає один блок даних.
    /// Якщо результат виявився більшим або рівним вхідним даним — повертає оригінал із NoCompression.
    /// </summary>
    public static CompressedBlock Compress(ReadOnlySpan<byte> raw, CodecOptions? options = null)
    {
        options ??= new CodecOptions();
        options.Validate(); // Перевірка параметрів на коректність

        // Якщо ввімкнено примусовий режим без стиснення або даних менше мінімального збігу (3 байти)
        if (options.ForceStore || raw.Length < options.MinMatch)
            return new CompressedBlock(raw.ToArray(), raw.Length, true);

        // 1. Будуємо таблицю повторів LZ77
        var matches = MatchFinder.Build(raw, options.MaxChain, options.MinMatch);

        // 2. Розбираємо потік обраним парсером (Greedy або Lazy) на токени
        var tokens = CreateParser(options.Parse).Parse(raw, matches, options);

        // 3. Кодуємо токени за алгоритмом Гаффмана в бінарний блок
        byte[] encoded = BlockEncoder.Encode(tokens);

        // Оцінка: якщо стиснений блок вийшов не меншим за сирі дані, лишаємо оригінал
        return encoded.Length >= raw.Length
            ? new CompressedBlock(raw.ToArray(), raw.Length, true)
            : new CompressedBlock(encoded, raw.Length, false);
    }

    /// <summary>
    /// Відновлює блок із сирими байтами з закодованого масиву.
    /// </summary>
    public static byte[] Decompress(ReadOnlySpan<byte> coded, int rawLength, bool noCompression)
    {
        // Якщо блок зберігався як сирі байти без компресії
        if (noCompression)
        {
            if (coded.Length != rawLength)
                throw new ArchiveException(ArchiveErrorCode.E_PAYLOAD, "NO_COMPRESSION: coded_len не дорівнює raw_len");

            return coded.ToArray();
        }

        // Повне декодування стисненого блоку
        return BlockDecoder.Decode(coded, rawLength);
    }

    /// <summary>
    /// Нарізає весь файл на блоки по blockSize байтів і кожен стискає окремо.
    /// </summary>
    public static List<CompressedBlock> CompressFile(ReadOnlySpan<byte> data, int blockSize, CodecOptions? options = null)
    {
        if (blockSize < 1) 
            throw new ArgumentOutOfRangeException(nameof(blockSize));

        var blocks = new List<CompressedBlock>();
        for (int start = 0; start < data.Length; start += blockSize)
            blocks.Add(Compress(data.Slice(start, Math.Min(blockSize, data.Length - start)), options));

        return blocks;
    }

    /// <summary>
    /// Послідовно декомпресує список блоків і склеює їх в єдиний оригінальний масив байтів.
    /// </summary>
    public static byte[] DecompressBlocks(IEnumerable<CompressedBlock> blocks)
    {
        var result = new List<byte>();
        foreach (var b in blocks)
            result.AddRange(Decompress(b.Data, b.RawLength, b.NoCompression));

        return result.ToArray();
    }

    /// <summary>
    /// Фабрика створення парсера: підтримує лише Greedy та Lazy (базовий рівень).
    /// </summary>
    private static IParser CreateParser(ParseStrategy strategy) => strategy switch
    {
        ParseStrategy.Greedy => new GreedyParser(),
        ParseStrategy.Lazy => new LazyParser(),
        _ => throw new ArgumentOutOfRangeException(nameof(strategy)),
    };
}