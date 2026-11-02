using SA.Compression;

namespace SA.Tests;

[TestClass]
public class DistanceCoderTests
{
    [TestMethod]
    public void Dist12_MatchesSpec()
    {
        var (code, extra, count) = DistanceCoder.Encode(12);
        Assert.AreEqual(4, code);
        Assert.AreEqual(3u, extra);
        Assert.AreEqual(3, count);
    }

    [TestMethod]
    public void Boundaries()
    {
        Assert.AreEqual(0, DistanceCoder.Encode(1).Code);
        Assert.AreEqual(1, DistanceCoder.Encode(2).Code);
        Assert.AreEqual(15, DistanceCoder.Encode(32768).Code);
    }

    [TestMethod]
    public void RoundTrip_AllDistances()
    {
        for (int dist = 1; dist <= 32768; dist++)
        {
            var (code, extra, count) = DistanceCoder.Encode(dist);
            Assert.IsTrue(code <= 15);
            Assert.AreEqual(count, DistanceCoder.ExtraBitCount(code));
            Assert.AreEqual(dist, DistanceCoder.Decode(code, extra), $"dist={dist}");
        }
    }

    [TestMethod]
    public void OutOfRange_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DistanceCoder.Encode(0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DistanceCoder.Encode(32769));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DistanceCoder.Decode(16, 0));
    }
}