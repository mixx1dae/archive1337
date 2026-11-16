using SA.Compression;
using SA.Compression.Lz77;
using SA.Core;
using Xunit;

namespace SA.Tests.Compression;

public class BlockCodecTests
{
    public static IEnumerable<object[]> Inputs()
    {
        foreach (var s in new[] { ParseStrategy.Greedy, ParseStrategy.Lazy })
        {
            yield return new object[] { s, "text", TestData.Text(30000, 11) };
            yield return new object[] { s, "zeros", new byte[5000] };
            yield return new object[] { s, "random", TestData.Random(5000, 12) };
            yield return new object[] { s, "small-alphabet", TestData.SmallAlphabet(25000, 13) };
            yield return new object[] { s, "far-repeat", TestData.FarRepeat() };
            yield return new object[] { s, "one-byte", new byte[] { 42 } };
            yield return new object[] { s, "two-bytes", new byte[] { 1, 2 } };
            yield return new object[] { s, "all-byte-values", Enumerable.Range(0, 256).Select(i => (byte)i).ToArray() };
        }
    }

    [Theory]
    [MemberData(nameof(Inputs))]
    public void RoundTrip(ParseStrategy strategy, string name, byte[] data)
    {
        var block = BlockCodec.Compress(data, new CodecOptions { Parse = strategy });
        Assert.Equal(data.Length, block.RawLength);
        var restored = BlockCodec.Decompress(block.Data, block.RawLength, block.NoCompression);
        Assert.True(data.AsSpan().SequenceEqual(restored), $"{strategy}/{name}");
    }

    [Fact]
    public void Zeros_CompressStrongly() // кейс 4: 5000 однакових байтів
    {
        var block = BlockCodec.Compress(new byte[5000]);
        Assert.False(block.NoCompression);
        Assert.True(block.CodedLength < 100, $"coded={block.CodedLength}");
    }

    [Fact]
    public void RandomData_FallsBackToNoCompression_WithCodedLenEqualRawLen()
    {
        var data = TestData.Random(4096, 21);
        var block = BlockCodec.Compress(data);
        Assert.True(block.NoCompression);
        Assert.Equal(data.Length, block.CodedLength);
        Assert.Equal(data, block.Data);
    }

    [Fact]
    public void CompressedBlock_NeverLargerThanRaw()
    {
        foreach (var data in new[] { TestData.Random(100, 1), TestData.Text(100, 2), new byte[50] })
            Assert.True(BlockCodec.Compress(data).CodedLength <= data.Length);
    }

    [Fact]
    public void ForceStore_KeepsRawBytes()
    {
        var data = TestData.Text(1000, 3);
        var block = BlockCodec.Compress(data, new CodecOptions { ForceStore = true });
        Assert.True(block.NoCompression);
        Assert.Equal(data, block.Data);
    }

    [Fact]
    public void DegenerateAlphabet_OneSymbolOnly_RoundTrips() // кейс 8: єдиний символ у дереві
    {
        var tokens = Enumerable.Range(0, 100).Select(_ => Token.Literal((byte)'a')).ToList();
        byte[] coded = BlockEncoder.Encode(tokens);
        Assert.Equal(new string('a', 100), System.Text.Encoding.ASCII.GetString(BlockDecoder.Decode(coded, 100)));
    }

    [Fact]
    public void CompressFile_SplitsIntoBlocks_AndRestoresFile()
    {
        var data = TestData.Text(100_000, 31);
        var blocks = BlockCodec.CompressFile(data, 32768);
        Assert.Equal(4, blocks.Count);
        Assert.Equal(32768, blocks[0].RawLength);
        Assert.Equal(100_000 - 3 * 32768, blocks[3].RawLength);
        Assert.Equal(data, BlockCodec.DecompressBlocks(blocks));
    }

    [Fact]
    public void CompressFile_EmptyInput_GivesNoBlocks()
    {
        Assert.Empty(BlockCodec.CompressFile(ReadOnlySpan<byte>.Empty, 1024));
    }

    [Fact]
    public void Options_Validation()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BlockCodec.Compress(new byte[10], new CodecOptions { MaxChain = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => BlockCodec.Compress(new byte[10], new CodecOptions { MinMatch = 2 }));
    }

    [Fact]
    public void LargerMinMatch_StillRoundTrips()
    {
        var data = TestData.Text(20000, 41);
        foreach (int m in new[] { 3, 4, 6, 10 })
        {
            var block = BlockCodec.Compress(data, new CodecOptions { MinMatch = m });
            Assert.Equal(data, BlockCodec.Decompress(block.Data, block.RawLength, block.NoCompression));
        }
    }

    [Fact]
    public void Decompress_NoCompression_WrongLength_IsEPayload()
    {
        var ex = Assert.Throws<ArcException>(() => BlockCodec.Decompress(new byte[5], 6, true));
        Assert.Equal(ErrorCodes.Payload, ex.Code);
    }
}
