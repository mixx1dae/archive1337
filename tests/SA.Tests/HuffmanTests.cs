using Xunit;
using SA.Compression;
using SA.Compression.BitIO;
using SA.Compression.Huffman;
using SA.Core;

namespace SA.Tests;


public class HuffmanTests
{
    private static void AssertCode(ArchiveErrorCode code, Action action)
    {
        var ex = Assert.Throws<ArchiveException>(action);
        Assert.Equal(code, ex.Code);
    }

    private static long KraftSum(int[] lengths)
        => lengths.Where(l => l > 0).Sum(l => 1L << (CodecConstants.MaxCodeLength - l));

    // ---------- канонічні коди ----------

    [Fact]
    public void CanonicalCode_MatchesSpecExample_Ref01Lit()
    {
        // таблиця LIT із прикладу ref_01: ' '=3, d=4, e=4, h=4, l=2, o=3, r=4, w=3, маркер 256=3
        var lengths = new int[257];
        lengths[' '] = 3; lengths['d'] = 4; lengths['e'] = 4; lengths['h'] = 4; lengths['l'] = 2;
        lengths['o'] = 3; lengths['r'] = 4; lengths['w'] = 3; lengths[256] = 3;

        var code = new CanonicalCode(lengths);

        Assert.Equal(0b00u, code.Codes['l']);
        Assert.Equal(0b010u, code.Codes[' ']);
        Assert.Equal(0b011u, code.Codes['o']);
        Assert.Equal(0b100u, code.Codes['w']);
        Assert.Equal(0b101u, code.Codes[256]);
        Assert.Equal(0b1100u, code.Codes['d']);
        Assert.Equal(0b1101u, code.Codes['e']);
        Assert.Equal(0b1110u, code.Codes['h']);
        Assert.Equal(0b1111u, code.Codes['r']);
    }

    [Fact]
    public void CanonicalCode_EncodeDecode_RoundTrip()
    {
        var lengths = new int[257];
        lengths['a'] = 1; lengths['b'] = 2; lengths['c'] = 3; lengths['d'] = 3;
        var code = new CanonicalCode(lengths);

        var w = new BitWriter();
        int[] message = { 'a', 'b', 'a', 'c', 'd', 'a', 'a', 'd' };
        foreach (int s in message) code.Write(w, s);

        var r = new BitReader(w.ToArray());
        foreach (int s in message) Assert.Equal(s, code.ReadSymbol(r));
    }

    [Fact]
    public void CanonicalCode_Degenerate_SingleSymbolIsOneZeroBit()
    {
        var lengths = new int[257];
        lengths[7] = 1;
        var code = new CanonicalCode(lengths);
        Assert.Equal(0u, code.Codes[7]);

        var w = new BitWriter();
        code.Write(w, 7); code.Write(w, 7);
        var r = new BitReader(w.ToArray());
        Assert.Equal(7, code.ReadSymbol(r));
        Assert.Equal(7, code.ReadSymbol(r));

        // біт 1 не відповідає жодному коду: E_CODE
        AssertCode(ArchiveErrorCode.E_CODE, () => code.ReadSymbol(new BitReader(new byte[] { 0xFF, 0xFF, 0xFF })));
    }

    // ---------- таблиці ----------

    [Fact]
    public void CodeTable_WriteRead_RoundTrip()
    {
        var lengths = new int[257];
        lengths[' '] = 2; lengths['a'] = 2; lengths['b'] = 2; lengths[256] = 2;

        var bytes = new List<byte>();
        CodeTable.Write(bytes, lengths);

        Assert.Equal(2 + 4 * 3, bytes.Count);
        int pos = 0;
        int[] back = CodeTable.Read(bytes.ToArray(), ref pos, 256, "LIT");
        Assert.Equal(lengths, back);
        Assert.Equal(bytes.Count, pos);
    }

    [Fact]
    public void CodeTable_Read_RejectsBrokenTables()
    {
        // nsym = 1, символ 65, довжина 2: вироджений код має мати довжину 1
        AssertCode(ArchiveErrorCode.E_CODE, () => { int p = 0; CodeTable.Read(new byte[] { 0, 1, 0, 65, 2 }, ref p, 256, "T"); });

        // символи не за зростанням (66, потім 65)
        AssertCode(ArchiveErrorCode.E_CODE, () => { int p = 0; CodeTable.Read(new byte[] { 0, 2, 0, 66, 1, 0, 65, 1 }, ref p, 256, "T"); });

        // порушена рівність Крафта: довжини 1 і 2
        AssertCode(ArchiveErrorCode.E_CODE, () => { int p = 0; CodeTable.Read(new byte[] { 0, 2, 0, 65, 1, 0, 66, 2 }, ref p, 256, "T"); });

        // довжина 16
        AssertCode(ArchiveErrorCode.E_CODE, () => { int p = 0; CodeTable.Read(new byte[] { 0, 1, 0, 65, 16 }, ref p, 256, "T"); });

        // символ поза алфавітом (maxSymbol = 15)
        AssertCode(ArchiveErrorCode.E_CODE, () => { int p = 0; CodeTable.Read(new byte[] { 0, 1, 0, 200, 1 }, ref p, 15, "T"); });

        // таблиця обірвана: заявлено 2 записи, є один
        AssertCode(ArchiveErrorCode.E_PAYLOAD, () => { int p = 0; CodeTable.Read(new byte[] { 0, 2, 0, 65, 1 }, ref p, 256, "T"); });

        // немає навіть nsym
        AssertCode(ArchiveErrorCode.E_PAYLOAD, () => { int p = 0; CodeTable.Read(new byte[] { 0 }, ref p, 256, "T"); });
    }

    // ---------- побудова довжин ----------

    [Fact]
    public void HuffmanBuilder_EmptyAndSingleSymbol()
    {
        Assert.True(HuffmanBuilder.BuildLengths(new long[257]).All(l => l == 0));

        var freq = new long[257];
        freq[42] = 1000;
        var lengths = HuffmanBuilder.BuildLengths(freq);
        Assert.Equal(1, lengths[42]);
        Assert.Equal(1, lengths.Count(l => l > 0));
    }

    [Fact]
    public void HuffmanBuilder_GivesCompletePrefixCode()
    {
        var freq = new long[257];
        foreach (char c in string.Concat(Enumerable.Repeat("hello world ", 40))) freq[c]++;
        freq[256] = 2;

        var lengths = HuffmanBuilder.BuildLengths(freq);

        for (int s = 0; s < freq.Length; s++)
            Assert.True((freq[s] > 0) == (lengths[s] > 0), $"символ {s}");
        Assert.Equal(1L << CodecConstants.MaxCodeLength, KraftSum(lengths));
        Assert.True(lengths.All(l => l <= CodecConstants.MaxCodeLength));
        // частіший символ не має довшого коду за рідший
        Assert.True(lengths['l'] <= lengths['w']);
    }

    [Fact]
    public void HuffmanBuilder_LimitsDepthTo15_OnFibonacciFrequencies()
    {
        // частоти Фібоначчі дають дерево глибини ~29 без обмеження
        var freq = new long[257];
        long a = 1, b = 1;
        for (int s = 0; s < 30; s++)
        {
            freq[s] = a;
            (a, b) = (b, a + b);
        }

        var lengths = HuffmanBuilder.BuildLengths(freq);

        Assert.True(lengths.All(l => l <= CodecConstants.MaxCodeLength));
        Assert.True(Enumerable.Range(0, 30).All(s => lengths[s] >= 1));
        Assert.Equal(1L << CodecConstants.MaxCodeLength, KraftSum(lengths));
    }
}
