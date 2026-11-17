using Xunit;
using SA.Compression;
using SA.Compression.BitIO;
using SA.Compression.Huffman;
using SA.Core;
namespace SA.Tests.Compression;

public class HuffmanTests
{
    [Fact]
    public void CanonicalCodes_MatchSpecExample()
    {
        // Таблиця LIT з розд. 8: символ → довжина
        var lengths = new int[257];
        lengths[32] = 3; lengths[100] = 4; lengths[101] = 4; lengths[104] = 4; lengths[108] = 2;
        lengths[111] = 3; lengths[114] = 4; lengths[119] = 3; lengths[256] = 3;
        var code = new CanonicalCode(lengths);

        Assert.Equal(0b010u, code.Codes[32]);
        Assert.Equal(0b1100u, code.Codes[100]);
        Assert.Equal(0b1101u, code.Codes[101]);
        Assert.Equal(0b1110u, code.Codes[104]);
        Assert.Equal(0b00u, code.Codes[108]);
        Assert.Equal(0b011u, code.Codes[111]);
        Assert.Equal(0b1111u, code.Codes[114]);
        Assert.Equal(0b100u, code.Codes[119]);
        Assert.Equal(0b101u, code.Codes[256]);
    }

    [Fact]
    public void Builder_SingleSymbol_GetsLengthOne_AndCodeZero()
    {
        var freq = new long[257];
        freq[65] = 1000;
        var lengths = HuffmanBuilder.BuildLengths(freq);
        Assert.Equal(1, lengths[65]);
        Assert.Equal(0u, new CanonicalCode(lengths).Codes[65]);
    }

    [Fact]
    public void Builder_TwoSymbols_BothLengthOne()
    {
        var freq = new long[10];
        freq[2] = 5; freq[7] = 1;
        var lengths = HuffmanBuilder.BuildLengths(freq);
        Assert.Equal(1, lengths[2]);
        Assert.Equal(1, lengths[7]);
    }

    [Fact]
    public void Builder_FibonacciFrequencies_AreLimitedTo15Bits_AndKraftIsExact()
    {
        // Частоти Фібоначчі дають найглибше дерево: без обмеження довжини сягнули б ~40.
        var freq = new long[40];
        long a = 1, b = 1;
        for (int i = 0; i < 40; i++) { freq[i] = a; (a, b) = (b, a + b); }
        var lengths = HuffmanBuilder.BuildLengths(freq);

        Assert.True(lengths.Max() <= 15);
        Assert.All(lengths, l => Assert.True(l >= 1));            // символи не злито і не втрачено
        long kraft = lengths.Sum(l => 1L << (15 - l));
        Assert.Equal(1L << 15, kraft);
    }

    [Fact]
    public void Builder_FrequentSymbolsGetShorterCodes()
    {
        var freq = new long[4];
        freq[0] = 100; freq[1] = 50; freq[2] = 10; freq[3] = 1;
        var l = HuffmanBuilder.BuildLengths(freq);
        Assert.True(l[0] <= l[1] && l[1] <= l[2] && l[2] <= l[3]);
    }

    [Fact]
    public void EncodeDecodeSymbols_RoundTrip()
    {
        var rng = new Random(3);
        var freq = new long[50];
        var message = new int[2000];
        for (int i = 0; i < message.Length; i++)
        {
            message[i] = (int)Math.Min(49, Math.Abs(rng.NextDouble() * rng.NextDouble() * 50));
            freq[message[i]]++;
        }
        var code = new CanonicalCode(HuffmanBuilder.BuildLengths(freq));
        var w = new BitWriter();
        foreach (int s in message) code.Write(w, s);

        var r = new BitReader(w.ToArray());
        foreach (int s in message) Assert.Equal(s, code.ReadSymbol(r));
    }

    // ---- таблиця кодів (розд. 3.3) ----

    [Fact]
    public void CodeTable_RoundTrip()
    {
        var lengths = new int[257];
        lengths[10] = 1; lengths[20] = 2; lengths[256] = 2;
        var bytes = new List<byte>();
        CodeTable.Write(bytes, lengths);
        int pos = 0;
        var read = CodeTable.Read(bytes.ToArray(), ref pos, 256, "LIT");
        Assert.Equal(lengths, read);
        Assert.Equal(bytes.Count, pos);
    }

    [Fact]
    public void CodeTable_SymbolSixteenInDist_IsECode()
    {
        // nsym=1, symbol=16, length=1
        byte[] table = { 0x00, 0x01, 0x00, 0x10, 0x01 };
        int pos = 0;
        var ex = Assert.Throws<ArchiveException>(() => CodeTable.Read(table, ref pos, 15, "DIST"));
        Assert.Equal(ArchiveErrorCode.E_CODE, ex.Code);
    }

    [Fact]
    public void CodeTable_IncompleteKraft_IsECode()
    {
        // дві довжини 1 і 2: сума 1/2 + 1/4 ≠ 1
        byte[] table = { 0x00, 0x02, 0x00, 0x01, 0x01, 0x00, 0x02, 0x02 };
        int pos = 0;
        var ex = Assert.Throws<ArchiveException>(() => CodeTable.Read(table, ref pos, 255, "LEN"));
        Assert.Equal(ArchiveErrorCode.E_CODE, ex.Code);
    }

    [Fact]
    public void CodeTable_LengthSixteen_IsECode()
    {
        byte[] table = { 0x00, 0x01, 0x00, 0x01, 0x10 };
        int pos = 0;
        var ex = Assert.Throws<ArchiveException>(() => CodeTable.Read(table, ref pos, 255, "LEN"));
        Assert.Equal(ArchiveErrorCode.E_CODE, ex.Code);
    }

    [Fact]
    public void CodeTable_SymbolsNotAscending_IsECode()
    {
        byte[] table = { 0x00, 0x02, 0x00, 0x05, 0x01, 0x00, 0x03, 0x01 };
        int pos = 0;
        var ex = Assert.Throws<ArchiveException>(() => CodeTable.Read(table, ref pos, 255, "LEN"));
        Assert.Equal(ArchiveErrorCode.E_CODE, ex.Code);
    }

    [Fact]
    public void CodeTable_SingleSymbolWithLengthTwo_IsECode()
    {
        byte[] table = { 0x00, 0x01, 0x00, 0x05, 0x02 };
        int pos = 0;
        var ex = Assert.Throws<ArchiveException>(() => CodeTable.Read(table, ref pos, 255, "LEN"));
        Assert.Equal(ArchiveErrorCode.E_CODE, ex.Code);
    }

    [Fact]
    public void CodeTable_Truncated_IsEPayload()
    {
        byte[] table = { 0x00, 0x03, 0x00, 0x01, 0x01 };   // обіцяно 3 записи, є 1
        int pos = 0;
        var ex = Assert.Throws<ArchiveException>(() => CodeTable.Read(table, ref pos, 255, "LEN"));
        Assert.Equal(ArchiveErrorCode.E_PAYLOAD, ex.Code);
    }

    [Fact]
    public void DistanceCoding_RoundTripsEveryDistance()
    {
        for (int dist = 1; dist <= CodecConstants.WindowSize; dist++)
        {
            int code = DistanceCoding.Code(dist);
            Assert.InRange(code, 0, 15);
            int extra = DistanceCoding.ExtraBits(code);
            uint value = DistanceCoding.ExtraValue(dist, extra);
            Assert.Equal(dist, DistanceCoding.Decode(code, value));
        }
    }

    [Fact]
    public void DistanceCoding_SpecExamples()
    {
        Assert.Equal(4, DistanceCoding.Code(12));                    // d=11 → 4 біти
        Assert.Equal(3u, DistanceCoding.ExtraValue(12, 3));          // extra = 3
        Assert.Equal(15, DistanceCoding.Code(32768));                // максимум
    }
}
