using Xunit;
using SA.Benchmark;
using SA.Compression;
using SA.Core;
using SA.Packing;

namespace SA.Tests;


public class BenchmarkTests
{
    [Fact]
    public void QMetric_Components()
    {
        Assert.Equal(0.5, QMetric.S(100, 100), 1e-12);
        Assert.Equal(0.0, QMetric.S(0, 0), 1e-12);
        Assert.Equal(0.5, QMetric.T(10, 10), 1e-12);
        Assert.Equal(1.0, QMetric.T(10, 0), 1e-12);

        var q = new QConfig { Alpha = 0.7, Beta = 0.3, TRefSeconds = 10 };
        Assert.Equal(0.5, QMetric.Q(q, 100, 100, 10, allRestored: true), 1e-12);
    }

    [Fact]
    public void QMetric_IsZero_WhenDataIsNotRestored()
    {
        Assert.Equal(0.0, QMetric.Q(new QConfig(), 100, 10, 0.1, allRestored: false));
    }

    [Fact]
    public void Runner_RestoresFilesExactly_AndReportsQInUnitInterval()
    {
        var inputs = new List<InputFile>
        {
            new("a.txt", Enumerable.Repeat((byte)'a', 500).ToArray()),
            new("b.bin", Enumerable.Range(0, 300).Select(i => (byte)(i * 13)).ToArray()),
        };
        var cfg = TeamConfig.Default.WithPacking(700, 100);

        var r = CorpusRunner.Run(inputs, "pw", cfg, new PassThroughCompressor(), new ConstantClassifier(0));

        Assert.True(r.AllRestored, r.Error);
        Assert.Equal(800L, r.OriginalBytes);
        Assert.True(r.ArchiveBytes > r.OriginalBytes);   // без стиснення архів більший за вхід
        Assert.True(r.S > 0 && r.S < 0.5);
        Assert.True(r.Q > 0 && r.Q < 1);
    }

    [Fact]
    public void Runner_ReportsZeroQ_OnConfigurationError()
    {
        var inputs = new List<InputFile> { new("a", new byte[10]) };
        var cfg = TeamConfig.Default.WithPacking(1000, 0); // block_size недопустимий

        var r = CorpusRunner.Run(inputs, "pw", cfg, new PassThroughCompressor(), new ConstantClassifier(0));

        Assert.False(r.AllRestored);
        Assert.Equal(0.0, r.Q);
        Assert.NotNull(r.Error);
    }

    [Fact]
    public void LoadDirectory_MakesNamesFlatAndUnique()
    {
        string root = Path.Combine(Path.GetTempPath(), $"corpus_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "x"));
        Directory.CreateDirectory(Path.Combine(root, "y"));
        try
        {
            File.WriteAllText(Path.Combine(root, "x", "same.txt"), "1");
            File.WriteAllText(Path.Combine(root, "y", "same.txt"), "2");
            File.WriteAllText(Path.Combine(root, "top.txt"), "3");

            var files = CorpusRunner.LoadDirectory(root);

            Assert.Equal(3, files.Count);
            Assert.Equal(3, files.Select(f => f.Name).Distinct().Count());
            Assert.True(files.All(f => !f.Name.Contains('/') && !f.Name.Contains('\\')));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static List<InputFile> SmallCorpus() => new()
    {
        new("a.txt", Enumerable.Repeat((byte)'a', 500).ToArray()),
        new("b.bin", Enumerable.Range(0, 300).Select(i => (byte)(i * 13)).ToArray()),
        new("c.dat", new byte[40]),
    };

    [Fact]
    public void FitnessEvaluator_CachesByGenes_AndCountsOnlyRealEvaluations()
    {
        var files = SmallCorpus();
        var evaluator = new FitnessEvaluator<int>(
            files, "pw",
            blockSize => new EvaluationSetup(
                TeamConfig.Default.WithPacking(700, blockSize), new PassThroughCompressor(), new ConstantClassifier(0)),
            blockSize => blockSize.ToString());

        double q100 = evaluator.Evaluate(100);
        double again = evaluator.Evaluate(100);   // з кешу
        double q50 = evaluator.Evaluate(50);

        Assert.Equal(q100, again);
        Assert.True(q100 > 0 && q100 < 1);
        Assert.True(q50 > 0 && q50 < 1);
        Assert.Equal(2, evaluator.Evaluations);
        Assert.Equal(1, evaluator.CacheHits);
        Assert.Equal(2, evaluator.History.Count);
        Assert.Equal(Math.Max(q100, q50), evaluator.BestSoFar, 1e-12);
        Assert.True(evaluator.History[1] >= evaluator.History[0]);   // крива найкращого не спадає
    }

    [Fact]
    public void FitnessEvaluator_InvalidGenes_GiveZeroFitness()
    {
        var evaluator = new FitnessEvaluator<int>(
            SmallCorpus(), "pw",
            blockSize => new EvaluationSetup(
                TeamConfig.Default.WithPacking(700, blockSize), new PassThroughCompressor(), new ConstantClassifier(0)),
            blockSize => blockSize.ToString());

        Assert.Equal(0.0, evaluator.Evaluate(0));   // block_size = 0 недопустимий
        Assert.Equal(1, evaluator.Evaluations);
    }

    [Fact]
    public void Ablation_ProducesAllRows_AndEveryVariantRestoresData()
    {
        var cfg = TeamConfig.Default.WithPacking(700, 100);

        var rows = Ablation.Run(SmallCorpus(), "pw", cfg, new PassThroughCompressor(), new ConstantClassifier(0));

        Assert.Equal(5, rows.Count);
        Assert.Equal("повна система", rows[0].Name);
        Assert.True(rows.All(r => r.Result.AllRestored), string.Join("; ", rows.Select(r => r.Result.Error)));
        // повна система тут теж без стиснення, тому «без стиснення» дає ті самі розміри
        Assert.Equal(rows[0].Result.ArchiveBytes, rows[1].Result.ArchiveBytes);

        string markdown = Ablation.ToMarkdown(rows);
        Assert.Contains("| Конфігурація |", markdown);
        Assert.Contains("без рюкзака", markdown);
    }
}
