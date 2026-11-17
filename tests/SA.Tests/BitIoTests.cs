using Xunit;
using SA.Compression.BitIO;
using SA.Core;

namespace SA.Tests;


public class BitIoTests
{
    private static void AssertCode(ArchiveErrorCode code, Action action)
    {
        var ex = Assert.Throws<ArchiveException>(action);
        Assert.Equal(code, ex.Code);
    }

    [Fact]
    public void BitWriter_MsbFirst()
    {
        var w = new BitWriter();
        w.Write(1, 1); w.Write(0, 1); w.Write(1, 1);
        Assert.Equal(new byte[] { 0xA0 }, w.ToArray());
    }

    [Fact]
    public void BitWriter_Empty_GivesNoBytes()
        => Assert.Equal(0, new BitWriter().ToArray().Length);

    [Fact]
    public void BitWriter_ExactByteBoundary_NoPadding()
    {
        var w = new BitWriter();
        w.Write(0xAB, 8);
        w.Write(0xCDEF, 16);
        Assert.Equal(new byte[] { 0xAB, 0xCD, 0xEF }, w.ToArray());
    }

    [Fact]
    public void BitWriter_BitLength_ZeroBitsAndBadCounts()
    {
        var w = new BitWriter();
        w.Write(0, 3);
        w.Write(123, 0); // нуль бітів нічого не пише
        Assert.Equal(3L, w.BitLength);

        Assert.Throws<ArgumentOutOfRangeException>(() => w.Write(1, 33));
        Assert.Throws<ArgumentOutOfRangeException>(() => w.Write(1, -1));
    }

    [Fact]
    public void BitReader_MatchesWriter()
    {
        var w = new BitWriter();
        w.Write(0b1101, 4);
        w.Write(0, 1);
        var r = new BitReader(w.ToArray());

        Assert.Equal(1, r.ReadBit());
        Assert.Equal(1, r.ReadBit());
        Assert.Equal(0, r.ReadBit());
        Assert.Equal(1, r.ReadBit());
        Assert.Equal(0, r.ReadBit());
        r.VerifyPadding(); // решта 3 біти нульові: не кидає
    }

    [Fact]
    public void BitReader_PastEnd_GivesE_CODE()
    {
        var r = new BitReader(new byte[] { 0xFF });
        r.ReadBits(8);
        AssertCode(ArchiveErrorCode.E_CODE, () => r.ReadBit());
    }

    [Fact]
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
    }

    [Fact]
    public void RoundTrip_VariousWidths()
    {
        var w = new BitWriter();
        w.Write(5, 3); w.Write(0x1FFF, 13); w.Write(0, 1); w.Write(0xDEADBEEF, 32);
        var r = new BitReader(w.ToArray());
        Assert.Equal(5u, r.ReadBits(3));
        Assert.Equal(0x1FFFu, r.ReadBits(13));
        Assert.Equal(0u, r.ReadBits(1));
        Assert.Equal(0xDEADBEEFu, r.ReadBits(32));
    }
}
