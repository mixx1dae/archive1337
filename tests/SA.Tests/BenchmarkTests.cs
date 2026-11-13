using SA.Benchmark;
using SA.Compression;
using SA.Core;
using SA.Packing;

namespace SA.Tests;

[TestClass]
public class BenchmarkTests
{
    [TestMethod]
    public void QMetric_Components()
    {
        Assert.AreEqual(0.5, QMetric.S(100, 100), 1e-12);
        Assert.AreEqual(0.0, QMetric.S(0, 0), 1e-12);
        Assert.AreEqual(0.5, QMetric.T(10, 10), 1e-12);
        Assert.AreEqual(1.0, QMetric.T(10, 0), 1e-12);

        var q = new QConfig { Alpha = 0.7, Beta = 0.3, TRefSeconds = 10 };
        Assert.AreEqual(0.5, QMetric.Q(q, 100, 100, 10, allRestored: true), 1e-12);
    }

    [TestMethod]
    public void QMetric_IsZero_WhenDataIsNotRestored()
    {
        Assert.AreEqual(0.0, QMetric.Q(new QConfig(), 100, 10, 0.1, allRestored: false));
    }

    [TestMethod]
    public void Runner_RestoresFilesExactly_AndReportsQInUnitInterval()
    {
        var inputs = new List<InputFile>
        {
            new("a.txt", Enumerable.Repeat((byte)'a', 500).ToArray()),
            new("b.bin", Enumerable.Range(0, 300).Select(i => (byte)(i * 13)).ToArray()),
        };
        var cfg = TeamConfig.Default.WithPacking(700, 100);

        var r = CorpusRunner.Run(inputs, "pw", cfg, new PassThroughCompressor(), new ConstantClassifier(0));

        Assert.IsTrue(r.AllRestored, r.Error);
        Assert.AreEqual(800L, r.OriginalBytes);
        Assert.IsTrue(r.ArchiveBytes > r.OriginalBytes);   // без стиснення архів більший за вхід
        Assert.IsTrue(r.S > 0 && r.S < 0.5);
        Assert.IsTrue(r.Q > 0 && r.Q < 1);
    }

    [TestMethod]
    public void Runner_ReportsZeroQ_OnConfigurationError()
    {
        var inputs = new List<InputFile> { new("a", new byte[10]) };
        var cfg = TeamConfig.Default.WithPacking(1000, 0); // block_size недопустимий

        var r = CorpusRunner.Run(inputs, "pw", cfg, new PassThroughCompressor(), new ConstantClassifier(0));

        Assert.IsFalse(r.AllRestored);
        Assert.AreEqual(0.0, r.Q);
        Assert.IsNotNull(r.Error);
    }

    [TestMethod]
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

            Assert.AreEqual(3, files.Count);
            Assert.AreEqual(3, files.Select(f => f.Name).Distinct().Count());
            Assert.IsTrue(files.All(f => !f.Name.Contains('/') && !f.Name.Contains('\\')));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
