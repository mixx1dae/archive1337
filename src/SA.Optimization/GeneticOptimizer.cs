using System.Globalization;

using SA.Core;

namespace SA.Optimization;

public sealed class ArchiverChromosome
{
    public int WindowSize { get; set; }       // 1024 .. 32768
    public int MinMatchLen { get; set; }      // 3 .. 8
    public double RejectEntropy { get; set; } // 0.85 .. 0.99

    public ArchiverChromosome Clone() => new()
    {
        WindowSize = WindowSize,
        MinMatchLen = MinMatchLen,
        RejectEntropy = RejectEntropy
    };
}

/// <summary>
/// Базовий генетичний алгоритм підбору параметрів архіватора (3 гени, турнір, 1-точковий кросовер, елітизм).
/// </summary>
public sealed class GeneticOptimizer
{
    private readonly Random _rng;
    private readonly int[] _allowedWindows = [1024, 2048, 4096, 8192, 16384, 32768];

    public GeneticOptimizer(int seed = 2001)
    {
        _rng = new Random(seed);
    }

    /// <summary>
    /// Єдина метрика Q з розділу 4. Якщо хоча б 1 файл відновлено з помилкою -> Q = 0.
    /// </summary>
    public static double CalculateQ(
        long originalBytes,
        long archiveBytes,
        double elapsedSeconds,
        bool allFilesValid,
        TeamConfig config)
    {
        if (!allFilesValid || originalBytes <= 0)
            return 0.0;

        double s = (double)originalBytes / (originalBytes + archiveBytes);
        double t = config.Q.TRefSeconds / (config.Q.TRefSeconds + Math.Max(elapsedSeconds, 1e-6));
        return config.Q.Alpha * s + config.Q.Beta * t;
    }

    private ArchiverChromosome RandomChromosome() => new()
    {
        WindowSize = _allowedWindows[_rng.Next(_allowedWindows.Length)],
        MinMatchLen = _rng.Next(3, 9),
        RejectEntropy = Math.Round(0.85 + _rng.NextDouble() * 0.14, 3)
    };

    private ArchiverChromosome Crossover(ArchiverChromosome p1, ArchiverChromosome p2)
    {
        int cut = _rng.Next(1, 3);
        return new ArchiverChromosome
        {
            WindowSize = p1.WindowSize,
            MinMatchLen = cut == 1 ? p2.MinMatchLen : p1.MinMatchLen,
            RejectEntropy = p2.RejectEntropy
        };
    }

    private void Mutate(ArchiverChromosome ch, double rate = 0.2)
    {
        if (_rng.NextDouble() < rate)
            ch.WindowSize = _allowedWindows[_rng.Next(_allowedWindows.Length)];
        if (_rng.NextDouble() < rate)
            ch.MinMatchLen = Math.Clamp(ch.MinMatchLen + (_rng.Next(2) == 0 ? -1 : 1), 3, 8);
        if (_rng.NextDouble() < rate)
            ch.RejectEntropy = Math.Round(Math.Clamp(ch.RejectEntropy + (_rng.NextDouble() * 0.06 - 0.03), 0.85, 0.99), 3);
    }

    public ArchiverChromosome Optimize(
        Func<ArchiverChromosome, double> evaluateFitness,
        int popSize = 12,
        int generations = 10,
        string? logCsvPath = "evolution_log.csv")
    {
        var population = Enumerable.Range(0, popSize).Select(_ => RandomChromosome()).ToList();
        var logLines = new List<string> { "generation,best_Q,mean_Q,window_size,min_match_len,reject_entropy" };

        ArchiverChromosome globalBest = population[0].Clone();

        for (int gen = 0; gen <= generations; gen++)
        {
            var scored = population
                .Select(ch => (Score: evaluateFitness(ch), Chrom: ch))
                .OrderByDescending(x => x.Score)
                .ToList();

            double bestQ = scored[0].Score;
            double meanQ = scored.Average(x => x.Score);
            globalBest = scored[0].Chrom.Clone();

            logLines.Add(string.Format(
                CultureInfo.InvariantCulture,
                "{0},{1:F6},{2:F6},{3},{4},{5:F3}",
                gen, bestQ, meanQ, globalBest.WindowSize, globalBest.MinMatchLen, globalBest.RejectEntropy));

            if (gen == generations)
                break;

            var nextPop = new List<ArchiverChromosome>
            {
                scored[0].Chrom.Clone(),
                scored[1].Chrom.Clone()
            };

            while (nextPop.Count < popSize)
            {
                var p1 = Tournament(scored);
                var p2 = Tournament(scored);
                var child = Crossover(p1, p2);
                Mutate(child);
                nextPop.Add(child);
            }

            population = nextPop;
        }

        if (!string.IsNullOrWhiteSpace(logCsvPath))
            File.WriteAllLines(logCsvPath, logLines);

        return globalBest;
    }

    private ArchiverChromosome Tournament(List<(double Score, ArchiverChromosome Chrom)> scored)
    {
        var a = scored[_rng.Next(scored.Count)];
        var b = scored[_rng.Next(scored.Count)];
        return a.Score >= b.Score ? a.Chrom : b.Chrom;
    }
}