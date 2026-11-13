using System.Diagnostics;
using SA.Core;
using SA.Format;
using SA.Packing;

namespace SA.Benchmark;

public sealed record BenchmarkResult(
    int FileCount,
    long OriginalBytes,
    long ArchiveBytes,
    int Volumes,
    double Seconds,
    bool AllRestored,
    string? Error,
    double S,
    double T,
    double Q);

/// <summary>Замір Q: пакування корпусу, розпакування, побайтове порівняння (скрипт виміру з плану П2).</summary>
public static class CorpusRunner
{
    /// <summary>
    /// Читає всі файли каталогу (включно з підкаталогами). Специфікація допускає в іменах лише
    /// прості імена, тому береться ім'я файла без шляху, заборонені символи замінюються на «_»,
    /// дублікати отримують префікс з номером.
    /// </summary>
    public static IReadOnlyList<InputFile> LoadDirectory(string directory)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<InputFile>();
        int number = 0;

        foreach (string path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                     .Where(p => !IsLabelsFile(directory, p))
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            number++;
            string name = Sanitize(Path.GetFileName(path));
            while (!used.Add(name)) name = $"{number}_{name}";
            result.Add(new InputFile(name, File.ReadAllBytes(path)));
        }
        return result;
    }

    /// <summary>labels.csv у корені корпусу містить розмітку типів і в сирий розмір корпусу не входить.</summary>
    private static bool IsLabelsFile(string root, string path)
        => string.Equals(Path.GetFileName(path), "labels.csv", StringComparison.OrdinalIgnoreCase)
           && string.Equals(Path.GetFullPath(Path.GetDirectoryName(path)!), Path.GetFullPath(root), StringComparison.Ordinal);

    private static string Sanitize(string name)
    {
        var chars = name.Select(c => c is '/' or '\\' or ':' or '\0' ? '_' : c).ToArray();
        return chars.Length == 0 ? "_" : new string(chars);
    }

    public static BenchmarkResult Run(
        IReadOnlyList<InputFile> files,
        string password,
        TeamConfig config,
        IBlockCompressor compressor,
        IFileClassifier classifier,
        PackingMethod method = PackingMethod.Dp)
    {
        long original = files.Sum(f => (long)f.Data.Length);
        long archive = 0;
        int volumeCount = 0;
        bool restored = false;
        string? error = null;

        var sw = Stopwatch.StartNew();
        try
        {
            var volumes = ArchiveBuilder.Build(files, password, config, compressor, classifier, method);
            volumeCount = volumes.Count;
            archive = volumes.Sum(v => (long)v.Length);

            var extracted = ArchiveReader.ExtractAll(volumes, password, compressor);
            restored = extracted.Count == files.Count;
            if (restored)
            {
                var byName = extracted.ToDictionary(e => e.Name, e => e.Data, StringComparer.Ordinal);
                foreach (var f in files)
                {
                    if (!byName.TryGetValue(f.Name, out var data) || !data.AsSpan().SequenceEqual(f.Data))
                    {
                        restored = false;
                        error = $"файл {f.Name} відновлено неточно";
                        break;
                    }
                }
            }
            else
            {
                error = "кількість відновлених файлів не збігається";
            }
        }
        catch (Exception ex) when (ex is ArchiveException or PackingCapacityException or ArgumentException or InvalidDataException)
        {
            error = ex.Message;
        }
        sw.Stop();

        double seconds = sw.Elapsed.TotalSeconds;
        return new BenchmarkResult(
            files.Count, original, archive, volumeCount, seconds, restored, error,
            QMetric.S(original, archive),
            QMetric.T(config.Q.TRefSeconds, seconds),
            QMetric.Q(config.Q, original, archive, seconds, restored));
    }
}
