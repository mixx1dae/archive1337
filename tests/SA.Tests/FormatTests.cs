using System.Text.Json;
using SA.Compression;
using SA.Core;
using SA.Crypto;
using SA.Format;

namespace SA.Tests;

[TestClass]
public class FormatTests
{
    // ref_01 із розділу 8 специфікації, зібраний за її описом (пароль algo2026, salt 00..0F,
    // nonce 0102030405060708 00000000). Реальний ref_01_single_file.arc порівнюється окремим тестом.
    private const string RefHex = "534152430100000001000102030405060708090a0b0c0d0e0f010203040506070800000000000000638e51d20ba4b860bbc8d7c95e2f288c9c60bc2258dd8c9b10efb0c821d60f993f27c61c7b117447279bf77344cc0b173e4789531c90eb0596aa90d1a631bf276b5013e6624652314ebbc1bd1f798a972607ffd7b1562c87e9b60ef303826647536fc2628df5569014e07f49e411af265bc27006cbe3b2394a1f91b6b2838cdd2749b72e";
    private const string PayloadHex = "00010000000968656c6c6f2e747874000001e07020884600010001000000000000000001e000000039000009002003006404006504006804006c02006f03007204007703010003000200cf0100ff0100010004010000000e00000007ed06a3f3159d18";

    private static readonly byte[] Salt = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
    private static readonly byte[] Prefix = { 1, 2, 3, 4, 5, 6, 7, 8 };
    private static readonly byte[] RefNonce = Convert.FromHexString("010203040506070800000000");
    private static readonly DerivedKeys Keys = KeyDerivation.Derive("algo2026", Salt);
    private static readonly PassThroughCompressor Pass = new();
    private static readonly DecodeOnlyCompressor Decoder = new();

    private static byte[] RefVolume() => Convert.FromHexString(RefHex);
    private static byte[] RefPayload() => Convert.FromHexString(PayloadHex);

    private static void AssertCode(ArchiveErrorCode code, Action action)
    {
        var ex = Assert.ThrowsExactly<ArchiveException>(action);
        Assert.AreEqual(code, ex.Code);
    }

    // ---------- CRC32 ----------

    [TestMethod]
    public void Crc32_KnownVectors()
    {
        Assert.AreEqual(0u, Crc32.Compute(ReadOnlySpan<byte>.Empty));
        Assert.AreEqual(0xCBF43926u, Crc32.Compute("123456789"u8));
        byte[] hello = Enumerable.Range(0, 40).SelectMany(_ => "hello world "u8.ToArray()).ToArray();
        Assert.AreEqual(480, hello.Length);
        Assert.AreEqual(0x70208846u, Crc32.Compute(hello)); // crc32 із ref_01
    }

    // ---------- заголовок ----------

    [TestMethod]
    public void Header_ParsesRef01Fields()
    {
        var h = VolumeHeader.Parse(RefVolume());
        Assert.AreEqual((byte)1, h.Version);
        Assert.AreEqual((ushort)0, h.VolumeIndex);
        Assert.AreEqual((ushort)1, h.VolumeCount);
        CollectionAssert.AreEqual(Salt, h.Salt);
        CollectionAssert.AreEqual(RefNonce, h.Nonce);
        Assert.AreEqual(99u, h.CiphertextLength);
        Assert.AreEqual(172, RefVolume().Length);
    }

    [TestMethod]
    public void Header_ToBytes_RoundTrips()
    {
        var h = VolumeHeader.Parse(RefVolume());
        byte[] bytes = h.ToBytes();
        Assert.AreEqual(VolumeHeader.Size, bytes.Length);
        CollectionAssert.AreEqual(RefVolume()[..41], bytes);
    }

    [TestMethod]
    public void Header_Errors()
    {
        byte[] v = RefVolume();

        AssertCode(ArchiveErrorCode.E_SHORT, () => VolumeHeader.Parse(v[..72]));

        byte[] badMagic = (byte[])v.Clone(); badMagic[0] = (byte)'X';
        AssertCode(ArchiveErrorCode.E_MAGIC, () => VolumeHeader.Parse(badMagic));

        byte[] badVersion = (byte[])v.Clone(); badVersion[4] = 2;
        AssertCode(ArchiveErrorCode.E_MAGIC, () => VolumeHeader.Parse(badVersion));

        AssertCode(ArchiveErrorCode.E_LENGTH, () => VolumeHeader.Parse(v[..^1]));
        AssertCode(ArchiveErrorCode.E_LENGTH, () => VolumeHeader.Parse(v.Append((byte)0).ToArray()));
    }

    // ---------- читання тому ----------

    [TestMethod]
    public void Writer_ReproducesRef01_FromPayload()
    {
        byte[] built = VolumeWriter.BuildRaw(Keys, Salt, RefNonce, 0, 1, RefPayload());
        CollectionAssert.AreEqual(RefVolume(), built);
    }

    [TestMethod]
    public void Open_ParsesRef01Payload()
    {
        var opened = VolumeReader.Open(RefVolume(), Keys);

        Assert.AreEqual(1, opened.Payload.Files.Count);
        var f = opened.Payload.Files[0];
        Assert.AreEqual("hello.txt", f.Name);
        Assert.AreEqual(480u, f.OrigSize);
        Assert.AreEqual(0x70208846u, f.Crc);
        Assert.AreEqual((byte)0, f.FileType);
        Assert.AreEqual((byte)1, f.Strategy);

        Assert.AreEqual(1, opened.Payload.Blocks.Count);
        var b = opened.Payload.Blocks[0];
        Assert.AreEqual((ushort)0, b.FileIndex);
        Assert.AreEqual(0u, b.BlockSeq);
        Assert.AreEqual(480u, b.RawLen);
        Assert.AreEqual(57u, b.CodedLen);
        Assert.IsFalse(b.NoCompression);
        Assert.AreEqual(57, opened.Payload.BlockData[0].Length);
        // перші два байти блока: nsym = 9 для таблиці LIT
        Assert.AreEqual((byte)0, opened.Payload.BlockData[0][0]);
        Assert.AreEqual((byte)9, opened.Payload.BlockData[0][1]);
    }

    [TestMethod]
    public void Payload_SerializeParse_RoundTrips()
    {
        var parsed = VolumePayload.Parse(RefPayload());
        CollectionAssert.AreEqual(RefPayload(), parsed.Serialize());
    }

    [TestMethod]
    public void Open_TamperedCiphertext_Tag_Header_AndWrongPassword_GiveHmacError()
    {
        byte[] v = RefVolume();

        byte[] ct = (byte[])v.Clone(); ct[50] ^= 0x01;
        AssertCode(ArchiveErrorCode.E_HMAC, () => VolumeReader.Open(ct, Keys));

        byte[] tag = (byte[])v.Clone(); tag[^1] ^= 0x01;
        AssertCode(ArchiveErrorCode.E_HMAC, () => VolumeReader.Open(tag, Keys));

        byte[] nonce = (byte[])v.Clone(); nonce[30] ^= 0x01;
        AssertCode(ArchiveErrorCode.E_HMAC, () => VolumeReader.Open(nonce, Keys));

        var wrong = KeyDerivation.Derive("wrong-password", Salt);
        AssertCode(ArchiveErrorCode.E_HMAC, () => VolumeReader.Open(v, wrong));
    }

    // ---------- перевірки навантаження ----------

    private static void AssertPayloadRejected(Action<byte[]> mutate)
    {
        byte[] p = RefPayload();
        mutate(p);
        byte[] vol = VolumeWriter.BuildRaw(Keys, Salt, RefNonce, 0, 1, p);
        AssertCode(ArchiveErrorCode.E_PAYLOAD, () => VolumeReader.Open(vol, Keys));
    }

    [TestMethod]
    public void Payload_RejectsReservedAndUnknownBits()
    {
        AssertPayloadRejected(p => p[3] = 1);      // reserved
        AssertPayloadRejected(p => p[2] = 0x02);   // невідомий біт flags
        AssertPayloadRejected(p => p[23] = 7);     // file_type > 6
        AssertPayloadRejected(p => p[41] = 0x02);  // невідомий біт block_flags
        AssertPayloadRejected(p => p[41] = 0x01);  // NO_COMPRESSION, але coded_len != raw_len
        AssertPayloadRejected(p => p[28] = 5);     // file_index поза таблицею файлів
    }

    [TestMethod]
    public void Payload_RejectsInconsistentLengths()
    {
        byte[] p = RefPayload();

        byte[] extra = VolumeWriter.BuildRaw(Keys, Salt, RefNonce, 0, 1, p.Append((byte)0).ToArray());
        AssertCode(ArchiveErrorCode.E_PAYLOAD, () => VolumeReader.Open(extra, Keys));

        byte[] cut = VolumeWriter.BuildRaw(Keys, Salt, RefNonce, 0, 1, p[..^1]);
        AssertCode(ArchiveErrorCode.E_PAYLOAD, () => VolumeReader.Open(cut, Keys));
    }

    [TestMethod]
    public void Payload_PredictionFlag_RejectedByDefault_AllowedOnRequest()
    {
        byte[] p = RefPayload();
        p[2] = 0x01;
        byte[] vol = VolumeWriter.BuildRaw(Keys, Salt, RefNonce, 0, 1, p);

        AssertCode(ArchiveErrorCode.E_PAYLOAD, () => VolumeReader.Open(vol, Keys));
        Assert.AreEqual((byte)1, VolumeReader.Open(vol, Keys, allowPrediction: true).Payload.Flags);
    }

    [TestMethod]
    public void Payload_RejectsUnsafeFileNames()
    {
        foreach (string name in new[] { "", ".", "..", "../x", "a/b", "a\\b", "c:x", "a\0b", "/abs" })
        {
            var payload = new VolumePayload(0,
                new[] { new FileEntry(name, 0, 0, 0, 0) }, Array.Empty<BlockEntry>(), Array.Empty<byte[]>());
            byte[] vol = VolumeWriter.Build(Keys, Salt, RefNonce, 0, 1, payload);
            AssertCode(ArchiveErrorCode.E_PAYLOAD, () => VolumeReader.Open(vol, Keys));
        }
    }

    [TestMethod]
    public void Payload_AcceptsUnicodeSimpleName()
    {
        var payload = new VolumePayload(0,
            new[] { new FileEntry("файл-1.txt", 0, 0, 0, 0) }, Array.Empty<BlockEntry>(), Array.Empty<byte[]>());
        byte[] vol = VolumeWriter.Build(Keys, Salt, RefNonce, 0, 1, payload);
        Assert.AreEqual("файл-1.txt", VolumeReader.Open(vol, Keys).Payload.Files[0].Name);
    }

    // ---------- збирання архіву з томів ----------

    private static byte[] Volume(ushort idx, ushort count, FileEntry[] files, BlockEntry[] blocks, byte[][] data)
        => VolumeWriter.Build(Keys, Salt, VolumeWriter.MakeNonce(Prefix, idx), idx, count,
            new VolumePayload(0, files, blocks, data));

    private static BlockEntry Raw(ushort fileIndex, uint seq, byte[] data)
        => new(fileIndex, seq, (uint)data.Length, (uint)data.Length, BlockEntry.NoCompressionFlag);

    private static FileEntry Entry(string name, byte[] whole)
        => new(name, (uint)whole.Length, Crc32.Compute(whole), 0, 0);

    [TestMethod]
    public void Extract_SingleVolume_NoCompression()
    {
        byte[] data = "Hello, SARC!"u8.ToArray();
        byte[] vol = Volume(0, 1, new[] { Entry("a.txt", data) }, new[] { Raw(0, 0, data) }, new[] { data });

        var files = ArchiveReader.ExtractAll(new[] { vol }, "algo2026", Pass);

        Assert.AreEqual(1, files.Count);
        Assert.AreEqual("a.txt", files[0].Name);
        CollectionAssert.AreEqual(data, files[0].Data);
    }

    [TestMethod]
    public void Extract_EmptyFile_Works()
    {
        byte[] empty = Array.Empty<byte>();
        byte[] vol = Volume(0, 1, new[] { Entry("empty.bin", empty) }, Array.Empty<BlockEntry>(), Array.Empty<byte[]>());

        var files = ArchiveReader.ExtractAll(new[] { vol }, "algo2026", Pass);

        Assert.AreEqual(0, files.Single().Data.Length);
    }

    private static (byte[] Vol0, byte[] Vol1, byte[] Big, byte[] X) TwoVolumes()
    {
        byte[] big = Enumerable.Range(0, 100).Select(i => (byte)(i * 7)).ToArray();
        byte[] x = "xyz"u8.ToArray();
        byte[] p0 = big[..40], p1 = big[40..70], p2 = big[70..];

        // у томі 0 файл big має локальний індекс 0, у томі 1 індекс 1
        byte[] v0 = Volume(0, 2, new[] { Entry("big.bin", big) },
            new[] { Raw(0, 0, p0), Raw(0, 2, p2) }, new[] { p0, p2 });
        byte[] v1 = Volume(1, 2, new[] { Entry("x.txt", x), Entry("big.bin", big) },
            new[] { Raw(1, 1, p1), Raw(0, 0, x) }, new[] { p1, x });
        return (v0, v1, big, x);
    }

    [TestMethod]
    public void Extract_MultiVolume_OrderOfVolumesDoesNotMatter()
    {
        var (v0, v1, big, x) = TwoVolumes();

        foreach (var set in new[] { new[] { v0, v1 }, new[] { v1, v0 } })
        {
            var files = ArchiveReader.ExtractAll(set, "algo2026", Pass);
            Assert.AreEqual(2, files.Count);
            CollectionAssert.AreEqual(big, files.Single(f => f.Name == "big.bin").Data);
            CollectionAssert.AreEqual(x, files.Single(f => f.Name == "x.txt").Data);
        }
    }

    [TestMethod]
    public void Extract_VolumeSetErrors()
    {
        var (v0, v1, _, _) = TwoVolumes();

        AssertCode(ArchiveErrorCode.E_VOLUMES, () => ArchiveReader.ExtractAll(Array.Empty<byte[]>(), "algo2026", Pass));
        AssertCode(ArchiveErrorCode.E_VOLUMES, () => ArchiveReader.ExtractAll(new[] { v0 }, "algo2026", Pass));            // бракує тому
        AssertCode(ArchiveErrorCode.E_VOLUMES, () => ArchiveReader.ExtractAll(new[] { v0, v0 }, "algo2026", Pass));        // дубль індексу

        byte[] other = Volume(1, 3, new[] { Entry("x.txt", "xyz"u8.ToArray()) }, Array.Empty<BlockEntry>(), Array.Empty<byte[]>());
        AssertCode(ArchiveErrorCode.E_VOLUMES, () => ArchiveReader.ExtractAll(new[] { v0, other }, "algo2026", Pass));     // різне volume_count

        byte[] outOfRange = Volume(2, 2, new[] { Entry("x.txt", "xyz"u8.ToArray()) }, Array.Empty<BlockEntry>(), Array.Empty<byte[]>());
        AssertCode(ArchiveErrorCode.E_VOLUMES, () => ArchiveReader.ExtractAll(new[] { v0, outOfRange }, "algo2026", Pass)); // індекс ≥ count
    }

    [TestMethod]
    public void Extract_BlockSequenceErrors_GiveE_PAYLOAD()
    {
        byte[] data = new byte[20];
        byte[] a = data[..10], b = data[10..];

        byte[] gap = Volume(0, 1, new[] { Entry("f", data) },
            new[] { Raw(0, 0, a), Raw(0, 2, b) }, new[] { a, b });
        AssertCode(ArchiveErrorCode.E_PAYLOAD, () => ArchiveReader.ExtractAll(new[] { gap }, "algo2026", Pass));

        byte[] dup = Volume(0, 1, new[] { Entry("f", data) },
            new[] { Raw(0, 0, a), Raw(0, 0, b) }, new[] { a, b });
        AssertCode(ArchiveErrorCode.E_PAYLOAD, () => ArchiveReader.ExtractAll(new[] { dup }, "algo2026", Pass));

        byte[] notFromZero = Volume(0, 1, new[] { Entry("f", data) },
            new[] { Raw(0, 1, a), Raw(0, 2, b) }, new[] { a, b });
        AssertCode(ArchiveErrorCode.E_PAYLOAD, () => ArchiveReader.ExtractAll(new[] { notFromZero }, "algo2026", Pass));
    }

    [TestMethod]
    public void Extract_WrongCrc_GivesE_CRC_AndWrongSize_GivesE_PAYLOAD()
    {
        byte[] data = "abcdef"u8.ToArray();

        var badCrc = new FileEntry("f", (uint)data.Length, Crc32.Compute(data) ^ 1u, 0, 0);
        byte[] v1 = Volume(0, 1, new[] { badCrc }, new[] { Raw(0, 0, data) }, new[] { data });
        AssertCode(ArchiveErrorCode.E_CRC, () => ArchiveReader.ExtractAll(new[] { v1 }, "algo2026", Pass));

        var badSize = new FileEntry("f", (uint)data.Length + 1, Crc32.Compute(data), 0, 0);
        byte[] v2 = Volume(0, 1, new[] { badSize }, new[] { Raw(0, 0, data) }, new[] { data });
        AssertCode(ArchiveErrorCode.E_PAYLOAD, () => ArchiveReader.ExtractAll(new[] { v2 }, "algo2026", Pass));
    }

    [TestMethod]
    public void Extract_WrongPassword_GivesE_HMAC()
    {
        AssertCode(ArchiveErrorCode.E_HMAC, () => ArchiveReader.ExtractAll(new[] { RefVolume() }, "not-the-password", Pass));
    }

    private sealed class ThrowingCompressor : IBlockCompressor
    {
        public CompressedBlock Compress(ReadOnlySpan<byte> raw) => throw new NotSupportedException();

        public byte[] Decompress(ReadOnlySpan<byte> coded, int rawLength, bool noCompression)
            => throw new InvalidDataException("зіпсований потік");
    }

    [TestMethod]
    public void Extract_DecoderExceptions_AreMappedTo_E_CODE()
    {
        // ref_01 має стиснений блок, а ThrowingCompressor кидає звичайний виняток .NET
        AssertCode(ArchiveErrorCode.E_CODE,
            () => ArchiveReader.ExtractAll(new[] { RefVolume() }, "algo2026", new ThrowingCompressor()));
    }

    // ---------- еталонні файли викладача (якщо лежать поруч із тестами) ----------

    private static string? FindReferenceDir()
    {
        string[] candidates =
        {
            "reference",
            Path.Combine("data", "reference"),
            Path.Combine("tests", "data", "reference"),
            Path.Combine("tests", "SA.Tests", "TestData", "reference"),
        };
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            foreach (string c in candidates)
            {
                string p = Path.Combine(dir.FullName, c);
                if (Directory.Exists(p)) return p;
            }
        return null;
    }

    [TestMethod]
    public void RealRef01_MatchesReconstruction()
    {
        string? dir = FindReferenceDir();
        string? path = dir == null ? null : Path.Combine(dir, "ref_01_single_file.arc");
        if (path == null || !File.Exists(path))
            Assert.Inconclusive("Покладіть ref_01_single_file.arc у папку reference (див. FindReferenceDir).");

        byte[] real = File.ReadAllBytes(path!);
        Assert.AreEqual(Convert.ToHexStringLower(RefVolume()), Convert.ToHexStringLower(real),
            "Збірка за специфікацією не збіглася з еталоном: перевірте KDF, нумерацію CTR (з 1), поля заголовка.");
    }

    [TestMethod]
    public void RealBadArchives_ReturnExpectedCodes()
    {
        string? dir = FindReferenceDir();
        string? listPath = dir == null ? null : Path.Combine(dir, "expected_failures.json");
        if (listPath == null || !File.Exists(listPath))
            Assert.Inconclusive("reference/expected_failures.json не знайдено.");

        using var doc = JsonDocument.Parse(File.ReadAllText(listPath!));
        var failures = new List<string>();
        int checkedCount = 0;

        foreach (var entry in doc.RootElement.EnumerateObject())
        {
            string path = Path.Combine(dir!, entry.Name);
            if (!File.Exists(path)) continue;
            checkedCount++;

            var expected = Enum.Parse<ArchiveErrorCode>(entry.Value.GetProperty("очікуваний_код").GetString()!);
            try
            {
                ArchiveReader.ExtractAll(new[] { File.ReadAllBytes(path) }, "algo2026", Decoder);
                failures.Add($"{entry.Name}: очікувалось {expected}, але помилки не було");
            }
            catch (ArchiveException ex) when (ex.Code == expected) { }
            catch (Exception ex)
            {
                failures.Add($"{entry.Name}: очікувалось {expected}, отримано {ex.GetType().Name}: {ex.Message}");
            }
        }

        if (checkedCount == 0) Assert.Inconclusive("bad_*.arc не знайдено в папці reference.");
        Assert.AreEqual(0, failures.Count, string.Join(Environment.NewLine, failures));
    }

    // ---------- справжні еталони викладача (out_p2_reference_T01.zip) ----------

    private static byte[]? ReadReference(string file)
    {
        string? dir = FindReferenceDir();
        if (dir == null) return null;
        string path = Path.Combine(dir, file);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    [TestMethod]
    public void RealReferences_ParseWithExpectedStructure()
    {
        // файли, блоки, volume_count. Значення звірено окремим еталонним декодером (Python) на цих же файлах.
        var expected = new (string File, int Files, int Blocks, int Volumes)[]
        {
            ("ref_01_single_file.arc", 1, 1, 1),
            ("ref_02_empty_file.arc", 1, 1, 1),
            ("ref_03_degenerate.arc", 1, 1, 1),
            ("ref_04_seven_groups.arc", 7, 7, 1),
            ("ref_05_no_compression.arc", 1, 1, 1),
            ("ref_06_two_volumes.part0.arc", 12, 14, 3),
            ("ref_06_two_volumes.part1.arc", 3, 3, 3),
            ("ref_06_two_volumes.part2.arc", 1, 1, 3),
        };

        int found = 0;
        var failures = new List<string>();
        foreach (var (file, nFiles, nBlocks, nVolumes) in expected)
        {
            byte[]? bytes = ReadReference(file);
            if (bytes == null) continue;
            found++;

            var header = VolumeHeader.Parse(bytes);
            var opened = VolumeReader.Open(bytes, KeyDerivation.Derive("algo2026", header.Salt));
            if (opened.Payload.Files.Count != nFiles || opened.Payload.Blocks.Count != nBlocks || header.VolumeCount != nVolumes)
                failures.Add($"{file}: файлів {opened.Payload.Files.Count}, блоків {opened.Payload.Blocks.Count}, томів {header.VolumeCount}");
        }

        if (found == 0) Assert.Inconclusive("Еталонні .arc не знайдено в папці reference.");
        Assert.AreEqual(0, failures.Count, string.Join(Environment.NewLine, failures));
    }

    [TestMethod]
    public void RealRef05_NoCompression_ExtractsWithPassThrough()
    {
        byte[]? bytes = ReadReference("ref_05_no_compression.arc");
        if (bytes == null) Assert.Inconclusive("ref_05_no_compression.arc не знайдено.");

        var file = ArchiveReader.ExtractAll(new[] { bytes! }, "algo2026", Pass).Single();

        Assert.AreEqual("packed.z", file.Name);
        Assert.AreEqual(12000, file.Data.Length);
        Assert.AreEqual(0x369BB1DDu, Crc32.Compute(file.Data));
    }

    [TestMethod]
    public void RealRef02_EmptyFile_Extracts()
    {
        byte[]? bytes = ReadReference("ref_02_empty_file.arc");
        if (bytes == null) Assert.Inconclusive("ref_02_empty_file.arc не знайдено.");

        var file = ArchiveReader.ExtractAll(new[] { bytes! }, "algo2026", Pass).Single();

        Assert.AreEqual("empty.dat", file.Name);
        Assert.AreEqual(0, file.Data.Length);
    }

    [TestMethod]
    public void RealReferences_ExtractMatchesJsonContents()
    {
        string? dir = FindReferenceDir();
        if (dir == null) Assert.Inconclusive("Папку reference не знайдено.");

        var failures = new List<string>();
        int checkedCount = 0;

        foreach (string jsonPath in Directory.GetFiles(dir!, "ref_*.json").OrderBy(p => p, StringComparer.Ordinal))
        {
            string stem = Path.GetFileNameWithoutExtension(jsonPath);
            using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
            var root = doc.RootElement;

            int volumeCount = root.GetProperty("томів").GetInt32();
            string password = root.TryGetProperty("пароль", out var pw) ? pw.GetString()! : "algo2026";

            var volumes = new List<byte[]>();
            bool missing = false;
            for (int i = 0; i < volumeCount; i++)
            {
                string name = volumeCount == 1 ? $"{stem}.arc" : $"{stem}.part{i}.arc";
                string path = Path.Combine(dir!, name);
                if (!File.Exists(path)) { missing = true; break; }
                volumes.Add(File.ReadAllBytes(path));
            }
            if (missing) continue;
            checkedCount++;

            try
            {
                var files = ArchiveReader.ExtractAll(volumes, password, Decoder);
                var content = root.GetProperty("вміст");
                int expectedCount = 0;
                foreach (var item in content.EnumerateObject())
                {
                    expectedCount++;
                    var file = files.FirstOrDefault(f => f.Name == item.Name);
                    if (file == null) { failures.Add($"{stem}: немає файла {item.Name}"); continue; }

                    int size = item.Value.GetProperty("розмір").GetInt32();
                    uint crc = Convert.ToUInt32(item.Value.GetProperty("crc32").GetString(), 16);
                    if (file.Data.Length != size || Crc32.Compute(file.Data) != crc)
                        failures.Add($"{stem}: {item.Name} не збігається з описом (розмір {file.Data.Length}, crc {Crc32.Compute(file.Data):X8})");
                }
                if (files.Count != expectedCount)
                    failures.Add($"{stem}: файлів {files.Count}, очікувалось {expectedCount}");
            }
            catch (ArchiveException ex)
            {
                failures.Add($"{stem}: {ex.Message}");
            }
        }

        if (checkedCount == 0) Assert.Inconclusive("ref_*.json не знайдено в папці reference.");
        Assert.AreEqual(0, failures.Count, string.Join(Environment.NewLine, failures));
    }

    // ---------- декодер блока (Гаффман + LZ77) ----------

    [TestMethod]
    public void BlockDecoder_DecodesRef01Block()
    {
        byte[] coded = VolumePayload.Parse(RefPayload()).BlockData[0];

        byte[] raw = BlockDecoder.Decode(coded, 480);

        byte[] expected = Enumerable.Range(0, 40).SelectMany(_ => "hello world "u8.ToArray()).ToArray();
        CollectionAssert.AreEqual(expected, raw);
    }

    [TestMethod]
    public void BlockDecoder_RejectsDamagedBlocks()
    {
        byte[] coded = VolumePayload.Parse(RefPayload()).BlockData[0];

        // обрізаний блок: stream_len не збігається із залишком
        AssertCode(ArchiveErrorCode.E_PAYLOAD, () => BlockDecoder.Decode(coded[..^1], 480));

        // неправильний raw_len: потік дає більше або менше байтів
        AssertCode(ArchiveErrorCode.E_PAYLOAD, () => BlockDecoder.Decode(coded, 479));
        AssertCode(ArchiveErrorCode.E_PAYLOAD, () => BlockDecoder.Decode(coded, 481));

        // ненульове доповнення в останньому байті потоку
        byte[] dirty = (byte[])coded.Clone();
        dirty[^1] |= 0x01;
        AssertCode(ArchiveErrorCode.E_CODE, () => BlockDecoder.Decode(dirty, 480));

        // порожній блок: немає навіть таблиць
        AssertCode(ArchiveErrorCode.E_PAYLOAD, () => BlockDecoder.Decode(Array.Empty<byte>(), 0));
    }

    [TestMethod]
    public void DecodeOnlyCompressor_Stores_And_Decodes()
    {
        byte[] raw = { 1, 2, 3 };
        var stored = Decoder.Compress(raw);
        Assert.IsTrue(stored.NoCompression);
        CollectionAssert.AreEqual(raw, Decoder.Decompress(stored.Data, raw.Length, true));

        AssertCode(ArchiveErrorCode.E_PAYLOAD, () => Decoder.Decompress(new byte[2], 3, true));
    }
}
