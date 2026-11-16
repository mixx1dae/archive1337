using SA.Compression;
using SA.Core;
using SA.Format;
using SA.Packing;

namespace SA.Tests;

[TestClass]
public class VolumePackerTests
{
    private static PackingFile MakeFile(string name, byte type, int blocks, int blockSize, byte fill = 1)
    {
        var list = new List<PackingBlock>();
        var whole = new List<byte>();
        for (int i = 0; i < blocks; i++)
        {
            byte[] data = Enumerable.Repeat((byte)(fill + i), blockSize).ToArray();
            list.Add(new PackingBlock((uint)i, (uint)blockSize, data, true));
            whole.AddRange(data);
        }
        return new PackingFile(name, (uint)whole.Count, Crc32.Compute(whole.ToArray()), type, 0, list);
    }

    private static int VolumeLength(VolumePayload p) => VolumeHeader.Size + p.Serialize().Length + VolumeHeader.TagSize;

    [TestMethod]
    public void Pack_RespectsMaxVolumeBytes_AndPlacesEveryBlockExactlyOnce()
    {
        var files = new[]
        {
            MakeFile("a.bin", 0, 5, 50),
            MakeFile("b.bin", 1, 4, 50),
            MakeFile("c.bin", 2, 3, 50),
        };
        var cfg = TeamConfig.Default.WithPacking(500, 100);

        foreach (var method in new[] { PackingMethod.Dp, PackingMethod.Greedy })
        {
            var volumes = VolumePacker.Pack(files, cfg, method);

            Assert.IsTrue(volumes.Count > 1);
            var seen = new HashSet<string>();
            foreach (var v in volumes)
            {
                Assert.IsTrue(VolumeLength(v) <= cfg.MaxVolumeBytes, "том перевищує max_volume_bytes");
                foreach (var b in v.Blocks)
                    Assert.IsTrue(seen.Add($"{v.Files[b.FileIndex].Name}:{b.BlockSeq}"), "блок потрапив у том двічі");
            }
            Assert.AreEqual(12, seen.Count);
        }
    }

    [TestMethod]
    public void Pack_PrefersHigherPriorityBlock_WhenOnlyOneFits()
    {
        var files = new[]
        {
            MakeFile("lo", 5, 1, 100), // пріоритет 1
            MakeFile("hi", 0, 1, 100), // пріоритет 5
        };
        // W = рівно вага одного блока: 100 + 15
        int max = VolumePacker.VolumeOverhead
                  + VolumePacker.TableEntrySize("lo") + VolumePacker.TableEntrySize("hi") + 115;
        var cfg = TeamConfig.Default.WithPacking(max, 100);

        var volumes = VolumePacker.Pack(files, cfg);

        Assert.AreEqual(2, volumes.Count);
        var first = volumes[0].Blocks.Single();
        Assert.AreEqual("hi", volumes[0].Files[first.FileIndex].Name);
        // таблиця першого тому резервується для обох файлів, хоча блок лише з одного
        Assert.AreEqual(2, volumes[0].Files.Count);
        Assert.AreEqual(1, volumes[1].Files.Count);
    }

    [TestMethod]
    public void Pack_EmptyFile_IsListedOnlyInFirstVolume()
    {
        var empty = new PackingFile("empty", 0, 0, 0, 0, Array.Empty<PackingBlock>());
        var big = MakeFile("big", 0, 6, 100);
        var cfg = TeamConfig.Default.WithPacking(400, 100);

        var volumes = VolumePacker.Pack(new[] { empty, big }, cfg);

        Assert.IsTrue(volumes.Count > 1);
        Assert.IsTrue(volumes[0].Files.Any(f => f.Name == "empty"));
        Assert.IsFalse(volumes.Skip(1).Any(v => v.Files.Any(f => f.Name == "empty")));
    }

    [TestMethod]
    public void Pack_OnlyEmptyFiles_GivesOneVolumeWithoutBlocks()
    {
        var empty = new PackingFile("empty", 0, 0, 0, 0, Array.Empty<PackingBlock>());
        var volumes = VolumePacker.Pack(new[] { empty }, TeamConfig.Default);

        Assert.AreEqual(1, volumes.Count);
        Assert.AreEqual(0, volumes[0].Blocks.Count);
        Assert.AreEqual(1, volumes[0].Files.Count);
    }

    [TestMethod]
    public void Pack_CapacityErrors_AreControlled()
    {
        // таблиця файлів не вміщується
        var tooSmall = TeamConfig.Default.WithPacking(VolumePacker.VolumeOverhead + 1, 100);
        Assert.ThrowsExactly<PackingCapacityException>(
            () => VolumePacker.Pack(new[] { MakeFile("file.bin", 0, 1, 10) }, tooSmall));

        // один блок більший за доступну місткість
        var file = MakeFile("f", 0, 1, 200);
        var cfg = TeamConfig.Default.WithPacking(VolumePacker.VolumeOverhead + VolumePacker.TableEntrySize("f") + 100, 100);
        Assert.ThrowsExactly<PackingCapacityException>(() => VolumePacker.Pack(new[] { file }, cfg));
    }

    // ---------- повний цикл ----------

    private static byte[] Bytes(int seed, int length)
    {
        var data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }

    [TestMethod]
    public void RoundTrip_ManyFiles_MultipleVolumes_BothMethods()
    {
        var cfg = TeamConfig.Default.WithPacking(600, 64);
        var inputs = new List<InputFile>
        {
            new("zero.bin", Array.Empty<byte>()),
            new("one.bin", Bytes(1, 1)),
            new("f63.bin", Bytes(2, 63)),
            new("f64.bin", Bytes(3, 64)),
            new("f65.bin", Bytes(4, 65)),
            new("mid.bin", Bytes(5, 200)),
            new("big.bin", Bytes(6, 1000)),
        };

        foreach (var method in new[] { PackingMethod.Dp, PackingMethod.Greedy })
        {
            var volumes = ArchiveBuilder.Build(inputs, "pw", cfg, new PassThroughCompressor(), new ConstantClassifier(0), method);

            Assert.IsTrue(volumes.Count > 1, "очікувалось кілька томів");
            foreach (var v in volumes) Assert.IsTrue(v.Length <= cfg.MaxVolumeBytes);

            // порядок томів не має значення
            var files = ArchiveReader.ExtractAll(volumes.Reverse().ToList(), "pw", new PassThroughCompressor());

            Assert.AreEqual(inputs.Count, files.Count);
            foreach (var input in inputs)
                CollectionAssert.AreEqual(input.Data, files.Single(f => f.Name == input.Name).Data);
        }
    }

    [TestMethod]
    public void Build_RejectsDuplicateAndUnsafeNames()
    {
        var cfg = TeamConfig.Default;
        var pass = new PassThroughCompressor();
        var cls = new ConstantClassifier(0);

        var dup = new List<InputFile> { new("a", new byte[1]), new("a", new byte[2]) };
        Assert.ThrowsExactly<ArgumentException>(() => ArchiveBuilder.Build(dup, "pw", cfg, pass, cls));

        var unsafeName = new List<InputFile> { new("../a", new byte[1]) };
        var ex = Assert.ThrowsExactly<ArchiveException>(() => ArchiveBuilder.Build(unsafeName, "pw", cfg, pass, cls));
        Assert.AreEqual(ArchiveErrorCode.E_PAYLOAD, ex.Code);
    }

    [TestMethod]
    public void Build_UsesClassifierTypeAndMarksStrategy()
    {
        var cfg = TeamConfig.Default;
        var inputs = new List<InputFile> { new("t.txt", Bytes(9, 10)) };

        var volumes = ArchiveBuilder.Build(inputs, "pw", cfg, new PassThroughCompressor(), new ConstantClassifier(3));
        var header = VolumeHeader.Parse(volumes[0]);
        var keys = SA.Crypto.KeyDerivation.Derive("pw", header.Salt);
        var opened = VolumeReader.Open(volumes[0], keys);

        Assert.AreEqual((byte)3, opened.Payload.Files[0].FileType);
        Assert.AreEqual((byte)1, opened.Payload.Files[0].Strategy); // як в еталонних архівах
    }

    [TestMethod]
    public void Build_EmptyFile_IsOneEmptyNoCompressionBlock_LikeReferenceArchive()
    {
        var inputs = new List<InputFile> { new("empty.dat", Array.Empty<byte>()) };
        var volumes = ArchiveBuilder.Build(inputs, "pw", TeamConfig.Default, new PassThroughCompressor(), new ConstantClassifier(6));

        Assert.AreEqual(1, volumes.Count);
        var header = VolumeHeader.Parse(volumes[0]);
        var opened = VolumeReader.Open(volumes[0], SA.Crypto.KeyDerivation.Derive("pw", header.Salt));

        var block = opened.Payload.Blocks.Single();
        Assert.AreEqual(0u, block.RawLen);
        Assert.AreEqual(0u, block.CodedLen);
        Assert.AreEqual(BlockEntry.NoCompressionFlag, block.BlockFlags);
        Assert.AreEqual(115, volumes[0].Length); // стільки ж, як у еталонному ref_02_empty_file.arc
    }
}
