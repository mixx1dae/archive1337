using SA.Compression;
using Xunit;

namespace SA.Tests.Compression;

/// <summary>Контрольний приклад з розділу 8 FORMAT_SPEC_P2 (hello.txt, 480 байтів).</summary>
public class SpecExampleTests
{
    [Fact]
    public void Decoder_ReadsSpecBlock()
    {
        byte[] decoded = BlockDecoder.Decode(TestData.SpecBlock, 480);
        Assert.Equal(TestData.HelloWorld40, decoded);
    }

    [Fact]
    public void SpecBlock_Is57Bytes()
    {
        Assert.Equal(57, TestData.SpecBlock.Length);   // coded_len = 0x39 у записі блоку
    }

    [Theory]
    [InlineData(ParseStrategy.Greedy)]
    [InlineData(ParseStrategy.Lazy)]
    public void Encoder_ProducesExactlyTheSpecBlock(ParseStrategy strategy)
    {
        var result = BlockCodec.Compress(TestData.HelloWorld40, new CodecOptions { Parse = strategy });
        Assert.False(result.NoCompression);
        Assert.Equal(TestData.SpecBlock, result.Data);
    }
}
