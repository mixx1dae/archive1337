using System.Diagnostics;
using System.Text;
using SA.Compression;

// Використання:
//   SA.CodecBench bench <каталог_корпусу> [розмір_блоку=65536] [повторів=3] [MinMatch=3] [MaxChain=64]
//        Порівняння Greedy / Lazy на кожному файлі (розмір, час), вивід — таблиці Markdown.
//        Перша підтека каталогу = група файлів.
//   SA.CodecBench enc <вхід> <вихід> <greedy|lazy>
//        Один блок → закодований блок (для перехресної перевірки з іншою реалізацією).
//   SA.CodecBench dec <вхід> <raw_len> <вихід>
//        Закодований блок → байти.

return args.Length == 0 ? Usage() : args[0] switch
{
    "bench" => Bench(args),
    "enc" => Enc(args),
    "dec" => Dec(args),
    _ => Usage(),
};

static int Usage()
{
    Console.Error.WriteLine("bench <dir> [blockSize] [repeats] [minMatch] [maxChain] | enc <in> <out> <strategy> | dec <in> <rawLen> <out>");
    return 2;
}

static ParseStrategy ParseStrategyArg(string s) => s.ToLowerInvariant() switch
{
    "greedy" => ParseStrategy.Greedy,
    "lazy" => ParseStrategy.Lazy,
    _ => throw new ArgumentException($"невідома стратегія {s}"),
};

static int Enc(string[] a)
{
    var raw = File.ReadAllBytes(a[1]);
    var block = BlockCodec.Compress(raw, new CodecOptions { Parse = ParseStrategyArg(a[3]) });
    File.WriteAllBytes(a[2], block.Data);
    Console.WriteLine($"{(block.NoCompression ? "stored" : "coded")} {block.CodedLength}");
    return 0;
}

static int Dec(string[] a)
{
    var coded = File.ReadAllBytes(a[1]);
    File.WriteAllBytes(a[3], BlockCodec.Decompress(coded, int.Parse(a[2]), false));
    return 0;
}

static int Bench(string[] a)
{
    string dir = a[1];
    int blockSize = a.Length > 2 ? int.Parse(a[2]) : 65536;
    int repeats = a.Length > 3 ? int.Parse(a[3]) : 3;
    int minMatch = a.Length > 4 ? int.Parse(a[4]) : 3;
    int maxChain = a.Length > 5 ? int.Parse(a[5]) : 64;
    var strategies = new[] { ParseStrategy.Greedy, ParseStrategy.Lazy };

    var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal).ToList();
    var perFile = new StringBuilder();

    // Розігрів JIT: інакше перший виміряний файл «платить» за компіляцію коду.
    var warm = files.Select(File.ReadAllBytes).FirstOrDefault(d => d.Length > 0);
    if (warm is not null)
        foreach (var st in strategies)
            for (int i = 0; i < 3; i++)
                BlockCodec.CompressFile(warm, blockSize, new CodecOptions { Parse = st, MinMatch = minMatch, MaxChain = maxChain });

    var groups = new SortedDictionary<string, long[]>();   // група → [вхід, розмір G, розмір L, мс G, мс L, блоків, збережено сирими]

    perFile.AppendLine("| Файл | Розмір | Greedy | Lazy | Lazy/Greedy | мс Greedy | мс Lazy |");
    perFile.AppendLine("|---|---:|---:|---:|---:|---:|---:|");

    foreach (var file in files)
    {
        var data = File.ReadAllBytes(file);
        if (data.Length == 0) continue;
        string rel = Path.GetRelativePath(dir, file);
        string group = rel.Contains(Path.DirectorySeparatorChar) ? rel.Split(Path.DirectorySeparatorChar)[0] : "(корінь)";

        var sizes = new long[2];
        var millis = new double[2];
        long blocksTotal = 0, stored = 0;
        for (int s = 0; s < 2; s++)
        {
            var options = new CodecOptions { Parse = strategies[s], MinMatch = minMatch, MaxChain = maxChain };
            double best = double.MaxValue;
            for (int r = 0; r < repeats; r++)
            {
                var sw = Stopwatch.StartNew();
                var blocks = BlockCodec.CompressFile(data, blockSize, options);
                sw.Stop();
                best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
                if (r == 0)
                {
                    sizes[s] = blocks.Sum(b => (long)b.CodedLength);
                    if (s == 1) { blocksTotal = blocks.Count; stored = blocks.Count(b => b.NoCompression); }
                    // Q = 0, якщо хоч один файл не відновився: перевіряємо ЗАВЖДИ.
                    if (!BlockCodec.DecompressBlocks(blocks).AsSpan().SequenceEqual(data))
                        throw new InvalidOperationException($"round-trip зламано: {rel} / {strategies[s]}");
                }
            }
            millis[s] = best;
        }

        perFile.AppendLine($"| {rel} | {data.Length} | {sizes[0]} | {sizes[1]} | {sizes[1] / (double)sizes[0]:P1} | {millis[0]:F0} | {millis[1]:F0} |");

        if (!groups.TryGetValue(group, out var g)) groups[group] = g = new long[7];
        g[0] += data.Length; g[1] += sizes[0]; g[2] += sizes[1];
        g[3] += (long)millis[0]; g[4] += (long)millis[1]; g[5] += blocksTotal; g[6] += stored;
    }

    Console.WriteLine($"Розмір блоку: {blockSize}, MaxChain={maxChain}, MinMatch={minMatch}, LazyMargin=1\n");
    Console.WriteLine("### По групах (сума байтів після стиснення)\n");
    Console.WriteLine("| Група | Вхід | Greedy | Lazy | Lazy vs Greedy | мс Greedy | мс Lazy | блоків | з них без стиснення |");
    Console.WriteLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|");
    string Pct(long x, long b) => $"{(x - b) / (double)b:+0.0%;-0.0%;0.0%}";
    var total = new long[7];
    foreach (var (name, g) in groups)
    {
        for (int i = 0; i < 7; i++) total[i] += g[i];
        Console.WriteLine($"| {name} | {g[0]} | {g[1]} | {g[2]} | {Pct(g[2], g[1])} | {g[3]} | {g[4]} | {g[5]} | {g[6]} |");
    }
    Console.WriteLine($"| **Разом** | {total[0]} | {total[1]} | {total[2]} | {Pct(total[2], total[1])} | {total[3]} | {total[4]} | {total[5]} | {total[6]} |");
    Console.WriteLine("\n### По файлах\n");
    Console.Write(perFile);
    return 0;
}
