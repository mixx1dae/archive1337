using SA.Compression;

namespace SA.Tests;

[TestClass]
public class PassThroughCompressorTests
{
    [TestMethod]
    public void RoundTrip_NoCompressionFlagSet()
    {
        var c = new PassThroughCompressor();
        byte[] raw = { 1, 2, 3, 4, 5 };
        var block = c.Compress(raw);

        Assert.IsTrue(block.NoCompression);
        Assert.AreEqual(raw.Length, block.Data.Length);
        CollectionAssert.AreEqual(raw, c.Decompress(block.Data, raw.Length, true));
    }
}