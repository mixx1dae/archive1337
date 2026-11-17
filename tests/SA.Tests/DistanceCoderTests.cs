using Xunit;
using SA.Compression;

namespace SA.Tests;


public class DistanceCoderTests
{
    [Fact]
    public void Dist12_MatchesSpec()
    {
        var (code, extra, count) = DistanceCoder.Encode(12);
        Assert.Equal(4, code);
        Assert.Equal(3u, extra);
        Assert.Equal(3, count);
    }

    [Fact]
    public void Boundaries()
    {
        Assert.Equal(0, DistanceCoder.Encode(1).Code);
        Assert.Equal(1, DistanceCoder.Encode(2).Code);
        Assert.Equal(15, DistanceCoder.Encode(32768).Code);
    }

    [Fact]
    public void RoundTrip_AllDistances()
    {
        for (int dist = 1; dist <= 32768; dist++)
        {
            var (code, extra, count) = DistanceCoder.Encode(dist);
            Assert.True(code <= 15);
            Assert.Equal(count, DistanceCoder.ExtraBitCount(code));
            Assert.Equal(dist, DistanceCoder.Decode(code, extra));
        }
    }

    [Fact]
    public void OutOfRange_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DistanceCoder.Encode(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DistanceCoder.Encode(32769));
        Assert.Throws<ArgumentOutOfRangeException>(() => DistanceCoder.Decode(16, 0));
    }
}
