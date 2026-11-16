<<<<<<< HEAD
using SA.Compression.BitIO;
using SA.Core;
=======
/* using SA.Compression.BitIO;
>>>>>>> 0e5ee03d2c2be78bee0b57f623a074bcbc82ba4a

namespace SA.Tests;

[TestClass]
public class BitIoTests
{
<<<<<<< HEAD
    private static void AssertCode(ArchiveErrorCode code, Action action)
    {
        var ex = Assert.ThrowsExactly<ArchiveException>(action);
        Assert.AreEqual(code, ex.Code);
    }

=======
>>>>>>> 0e5ee03d2c2be78bee0b57f623a074bcbc82ba4a
    [TestMethod]
    public void BitWriter_MsbFirst()
    {
        var w = new BitWriter();
<<<<<<< HEAD
        w.Write(1, 1); w.Write(0, 1); w.Write(1, 1);
=======
        w.WriteBit(1); w.WriteBit(0); w.WriteBit(1);
>>>>>>> 0e5ee03d2c2be78bee0b57f623a074bcbc82ba4a
        CollectionAssert.AreEqual(new byte[] { 0xA0 }, w.ToArray());
    }

    [TestMethod]
    public void BitWriter_Empty_GivesNoBytes()
        => Assert.AreEqual(0, new BitWriter().ToArray().Length);

    [TestMethod]
    public void BitWriter_ExactByteBoundary_NoPadding()
    {
        var w = new BitWriter();
<<<<<<< HEAD
        w.Write(0xAB, 8);
        w.Write(0xCDEF, 16);
=======
        w.WriteBits(0xAB, 8);
        w.WriteBits(0xCDEF, 16);
>>>>>>> 0e5ee03d2c2be78bee0b57f623a074bcbc82ba4a
        CollectionAssert.AreEqual(new byte[] { 0xAB, 0xCD, 0xEF }, w.ToArray());
    }

    [TestMethod]
<<<<<<< HEAD
    public void BitWriter_BitLength_ZeroBitsAndBadCounts()
    {
        var w = new BitWriter();
        w.Write(0, 3);
        w.Write(123, 0); // нуль бітів нічого не пише
        Assert.AreEqual(3L, w.BitLength);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => w.Write(1, 33));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => w.Write(1, -1));
    }

    [TestMethod]
    public void BitReader_MatchesWriter()
    {
        var w = new BitWriter();
        w.Write(0b1101, 4);
        w.Write(0, 1);
=======
    public void BitReader_MatchesWriter()
    {
        var w = new BitWriter();
        w.WriteBits(0b1101, 4);
        w.WriteBit(0);
>>>>>>> 0e5ee03d2c2be78bee0b57f623a074bcbc82ba4a
        var r = new BitReader(w.ToArray());

        Assert.AreEqual(1, r.ReadBit());
        Assert.AreEqual(1, r.ReadBit());
        Assert.AreEqual(0, r.ReadBit());
        Assert.AreEqual(1, r.ReadBit());
        Assert.AreEqual(0, r.ReadBit());
<<<<<<< HEAD
        r.VerifyPadding(); // решта 3 біти нульові: не кидає
    }

    [TestMethod]
    public void BitReader_PastEnd_GivesE_CODE()
    {
        var r = new BitReader(new byte[] { 0xFF });
        r.ReadBits(8);
        AssertCode(ArchiveErrorCode.E_CODE, () => r.ReadBit());
    }

    [TestMethod]
    public void BitReader_VerifyPadding_Errors()
    {
        // ненульове доповнення останнього байта
        var dirty = new BitReader(new byte[] { 0x81 });
        dirty.ReadBit();
        AssertCode(ArchiveErrorCode.E_CODE, () => dirty.VerifyPadding());

        // зайві цілі байти після потоку
        var extra = new BitReader(new byte[] { 0x80, 0x00 });
        extra.ReadBit();
        AssertCode(ArchiveErrorCode.E_PAYLOAD, () => extra.VerifyPadding());
=======
        Assert.IsTrue(r.RemainingBitsAreZero());
    }

    [TestMethod]
    public void BitReader_PastEnd_Throws()
    {
        var r = new BitReader(new byte[] { 0xFF });
        r.ReadBits(8);
        Assert.ThrowsExactly<EndOfStreamException>(() => r.ReadBit());
>>>>>>> 0e5ee03d2c2be78bee0b57f623a074bcbc82ba4a
    }

    [TestMethod]
    public void RoundTrip_VariousWidths()
    {
        var w = new BitWriter();
<<<<<<< HEAD
        w.Write(5, 3); w.Write(0x1FFF, 13); w.Write(0, 1); w.Write(0xDEADBEEF, 32);
=======
        w.WriteBits(5, 3); w.WriteBits(0x1FFF, 13); w.WriteBits(0, 1); w.WriteBits(0xDEADBEEF, 32);
>>>>>>> 0e5ee03d2c2be78bee0b57f623a074bcbc82ba4a
        var r = new BitReader(w.ToArray());
        Assert.AreEqual(5u, r.ReadBits(3));
        Assert.AreEqual(0x1FFFu, r.ReadBits(13));
        Assert.AreEqual(0u, r.ReadBits(1));
        Assert.AreEqual(0xDEADBEEFu, r.ReadBits(32));
    }
<<<<<<< HEAD
}
=======
} */
>>>>>>> 0e5ee03d2c2be78bee0b57f623a074bcbc82ba4a
