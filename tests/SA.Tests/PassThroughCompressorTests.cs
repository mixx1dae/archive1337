using Xunit;
using SA.Compression;

namespace SA.Tests;


public class PassThroughCompressorTests
{
    [Fact]
    public void RoundTrip_NoCompressionFlagSet()
    {
        var c = new PassThroughCompressor();
        byte[] raw = { 1, 2, 3, 4, 5 };
        var block = c.Compress(raw);

        Assert.True(block.NoCompression);
        Assert.Equal(raw.Length, block.Data.Length);
        Assert.Equal(raw, c.Decompress(block.Data, raw.Length, true));
    }
}
