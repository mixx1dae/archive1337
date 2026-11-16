using System.Globalization;
using System.Text;
using SA.Compression;
using SA.Core;
using SA.Packing;

namespace SA.Benchmark;

public sealed record AblationRow(string Name, BenchmarkResult Result);

/// <summary>
/// Таблиця «вплив кожного блока на Q» (розділ 12 методички): повна система й система,
/// з якої по черзі вимкнено один реалізований блок. Блоки, яких ще немає, в таблицю не потрапляють.
/// </summary>
public static class Ablation
{
    public static IReadOnlyList<AblationRow> Run(
        IReadOnlyList<InputFile> files,
        string password,
        TeamConfig config,
        IBlockCompressor fullCompressor,
        IFileClassifier fullClassifier)
    {
        var store = new DecodeOnlyCompressor();          // Compress = NO_COMPRESSION
        var oneType = new ConstantClassifier(0);          // усі файли одного типу, однакові пріоритети

        return new[]
        {
            new AblationRow("повна система",
                CorpusRunner.Run(files, password, config, fullCompressor, fullClassifier, PackingMethod.Dp)),
            new AblationRow("без стиснення (NO_COMPRESSION)",
                CorpusRunner.Run(files, password, config, store, fullClassifier, PackingMethod.Dp)),
            new AblationRow("жадібний замість ДП",
                CorpusRunner.Run(files, password, config, fullCompressor, fullClassifier, PackingMethod.Greedy)),
            new AblationRow("без рюкзака (послідовне пакування)",
                CorpusRunner.Run(files, password, config, fullCompressor, fullClassifier, PackingMethod.Sequential)),
            new AblationRow("без класифікатора (один тип для всіх)",
                CorpusRunner.Run(files, password, config, fullCompressor, oneType, PackingMethod.Dp)),
        };
    }

    public static string ToMarkdown(IReadOnlyList<AblationRow> rows)
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("| Конфігурація | B_arc | Томів | t, с | S | T | Q | ΔQ | Відновлено |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---|");

        double baseQ = rows.Count > 0 ? rows[0].Result.Q : 0.0;
        foreach (var row in rows)
        {
            var r = row.Result;
            sb.AppendLine(string.Create(ci,
                $"| {row.Name} | {r.ArchiveBytes} | {r.Volumes} | {r.Seconds:F3} | {r.S:F4} | {r.T:F4} | {r.Q:F4} | {r.Q - baseQ:+0.0000;-0.0000;0.0000} | {(r.AllRestored ? "так" : "НІ")} |"));
        }
        return sb.ToString();
    }
}
