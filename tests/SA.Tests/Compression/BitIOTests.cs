using SA.Compression.BitIO;
using SA.Core;
using Xunit;

namespace SA.Tests.Compression;

public class BitIOTests
{
    [Fact]
    public void Writer_PacksMsbFirst_AndPadsWithZeros()
    {
        var w = new BitWriter();
        w.Write(0b101, 3);
        w.Write(0b1, 1);
        // 1011 0000 → 0xB0 (останні 4 біти — нульове доповнення)
        Assert.Equal(new byte[] { 0xB0 }, w.ToArray());
    }

    [Fact]
    public void Writer_HandlesValuesCrossingByteBoundary()
    {
        var w = new BitWriter();
        w.Write(0b111, 3);
        w.Write(0b0000_00101, 9);   // 9 біт: перетин межі байта
        // 111 000000101 → 1110 0000 | 0101 0000
        Assert.Equal(new byte[] { 0xE0, 0x50 }, w.ToArray());
        Assert.Equal(12, w.BitLength);
    }

    [Fact]
    public void Reader_ReadsBackWhatWriterWrote()
    {
        var rng = new Random(1);
        var w = new BitWriter();
        var expected = new List<(uint value, int bits)>();
        for (int i = 0; i < 500; i++)
        {
            int bits = rng.Next(1, 17);
            uint value = (uint)rng.Next(0, 1 << bits);
            expected.Add((value, bits));
            w.Write(value, bits);
        }
        var r = new BitReader(w.ToArray());
        foreach (var (value, bits) in expected)
            Assert.Equal(value, r.ReadBits(bits));
        r.VerifyPadding();
    }

    [Fact]
    public void Reader_ReadingPastEnd_IsECode()
    {
        var r = new BitReader(new byte[] { 0xFF });
        r.ReadBits(8);
        var ex = Assert.Throws<ArcException>(() => r.ReadBit());
        Assert.Equal(ErrorCodes.Code, ex.Code);
    }

    [Fact]
    public void VerifyPadding_NonZeroPadding_IsECode()
    {
        var r = new BitReader(new byte[] { 0b1010_0001 });
        r.ReadBits(4);
        var ex = Assert.Throws<ArcException>(() => r.VerifyPadding());
        Assert.Equal(ErrorCodes.Code, ex.Code);
    }

    [Fact]
    public void VerifyPadding_WholeExtraByte_IsEPayload()
    {
        var r = new BitReader(new byte[] { 0xAA, 0x00 });
        r.ReadBits(8);
        var ex = Assert.Throws<ArcException>(() => r.VerifyPadding());
        Assert.Equal(ErrorCodes.Payload, ex.Code);
    }
}
