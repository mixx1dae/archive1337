<<<<<<< HEAD
using SA.Compression;
=======
/* using SA.Compression;
>>>>>>> 0e5ee03d2c2be78bee0b57f623a074bcbc82ba4a

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
<<<<<<< HEAD
}
=======
} */
>>>>>>> 0e5ee03d2c2be78bee0b57f623a074bcbc82ba4a
