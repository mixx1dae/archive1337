using SA.Compression.BitIO;

namespace SA.Tests;

[TestClass]
public class BitIoTests
{
    [TestMethod]
    public void BitWriter_MsbFirst()
    {
        var w = new BitWriter();
        w.WriteBit(1); w.WriteBit(0); w.WriteBit(1);
        CollectionAssert.AreEqual(new byte[] { 0xA0 }, w.ToArray());
    }

    [TestMethod]
    public void BitWriter_Empty_GivesNoBytes()
        => Assert.AreEqual(0, new BitWriter().ToArray().Length);

    [TestMethod]
    public void BitWriter_ExactByteBoundary_NoPadding()
    {
        var w = new BitWriter();
        w.WriteBits(0xAB, 8);
        w.WriteBits(0xCDEF, 16);
        CollectionAssert.AreEqual(new byte[] { 0xAB, 0xCD, 0xEF }, w.ToArray());
    }

    [TestMethod]
    public void BitReader_MatchesWriter()
    {
        var w = new BitWriter();
        w.WriteBits(0b1101, 4);
        w.WriteBit(0);
        var r = new BitReader(w.ToArray());

        Assert.AreEqual(1, r.ReadBit());
        Assert.AreEqual(1, r.ReadBit());
        Assert.AreEqual(0, r.ReadBit());
        Assert.AreEqual(1, r.ReadBit());
        Assert.AreEqual(0, r.ReadBit());
        Assert.IsTrue(r.RemainingBitsAreZero());
    }

    [TestMethod]
    public void BitReader_PastEnd_Throws()
    {
        var r = new BitReader(new byte[] { 0xFF });
        r.ReadBits(8);
        Assert.ThrowsExactly<EndOfStreamException>(() => r.ReadBit());
    }

    [TestMethod]
    public void RoundTrip_VariousWidths()
    {
        var w = new BitWriter();
        w.WriteBits(5, 3); w.WriteBits(0x1FFF, 13); w.WriteBits(0, 1); w.WriteBits(0xDEADBEEF, 32);
        var r = new BitReader(w.ToArray());
        Assert.AreEqual(5u, r.ReadBits(3));
        Assert.AreEqual(0x1FFFu, r.ReadBits(13));
        Assert.AreEqual(0u, r.ReadBits(1));
        Assert.AreEqual(0xDEADBEEFu, r.ReadBits(32));
    }
}