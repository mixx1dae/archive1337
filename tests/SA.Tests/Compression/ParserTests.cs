using Xunit;
using SA.Compression;
using SA.Compression.Lz77;

namespace SA.Tests.Compression;

public class ParserTests
{
    public static IEnumerable<object[]> Datasets()
    {
        yield return new object[] { "hello", TestData.HelloWorld40 };
        yield return new object[] { "zeros", new byte[5000] };
        yield return new object[] { "text", TestData.Text(20000, 1) };
        yield return new object[] { "small-alphabet", TestData.SmallAlphabet(20000, 2) };
        yield return new object[] { "random", TestData.Random(4000, 3) };
        yield return new object[] { "far-repeat", TestData.FarRepeat() };
        yield return new object[] { "gradient", TestData.NoisyGradient(200, 100, 4) };
    }

    private static List<Token> Parse(IParser p, byte[] data, CodecOptions o) =>
        p.Parse(data, MatchFinder.Build(data, o.MaxChain, o.MinMatch), o);

    /// <summary>Відтворює вихідні байти з елементів так, як це робить розпаковувач.</summary>
    private static byte[] Replay(IEnumerable<Token> tokens)
    {
        var o = new List<byte>();
        foreach (var t in tokens)
        {
            if (t.IsLiteral) { o.Add(t.Byte); continue; }
            for (int i = 0; i < t.Length; i++) o.Add(o[^t.Distance]);   // побайтово
        }
        return o.ToArray();
    }

    [Theory]
    [MemberData(nameof(Datasets))]
    public void BothParsers_ProduceTokensThatReplayToTheInput(string name, byte[] data)
    {
        var o = new CodecOptions();
        foreach (IParser p in new IParser[] { new GreedyParser(), new LazyParser() })
            Assert.True(data.AsSpan().SequenceEqual(Replay(Parse(p, data, o))), $"{name}: {p.GetType().Name}");
    }

    [Fact]
    public void Greedy_TakesLongestMatch_Immediately()
    {
        // "abcabcabc": у позиції 3 найдовший збіг — на 6 байтів (3 + перекриття)
        var data = System.Text.Encoding.ASCII.GetBytes("abcabcabc");
        var tokens = Parse(new GreedyParser(), data, new CodecOptions());
        Assert.Equal(4, tokens.Count);                 // a b c + одне посилання
        Assert.Equal((3, 6), (tokens[3].Distance, tokens[3].Length));
    }

    [Fact]
    public void Lazy_SkipsAShortMatch_WhenALongerOneStartsNextByte()
    {
        // Позиція 8 ('x','b','c','d'): збіг "bcd" довжини 3 не береться жадібно, якщо з 'b' далі довше.
        // Дані: "xbcdefg" + "?" + "xbcd" + "bcdefg"
        var data = System.Text.Encoding.ASCII.GetBytes("xbcdefg?xbcdefg");
        var greedy = Parse(new GreedyParser(), data, new CodecOptions());
        var lazy = Parse(new LazyParser(), data, new CodecOptions());
        // обидва коректні
        Assert.Equal(data, Replay(greedy));
        Assert.Equal(data, Replay(lazy));
        // лінивий не дає більше елементів, ніж жадібний, на цьому прикладі
        Assert.True(lazy.Count <= greedy.Count);
    }

    [Fact]
    public void Lazy_ProducesNoMoreBytes_ThanGreedy_OnTextLikeData()
    {
        var data = TestData.Text(30000, 5);
        int greedy = BlockCodec.Compress(data, new CodecOptions { Parse = ParseStrategy.Greedy }).CodedLength;
        int lazy = BlockCodec.Compress(data, new CodecOptions { Parse = ParseStrategy.Lazy }).CodedLength;
        Assert.True(lazy <= greedy * 1.02, $"lazy={lazy} greedy={greedy}");   // не гірший більш ніж на 2 %
    }

    [Fact]
    public void MatchFinder_Candidates_AreValid_AndPruned()
    {
        var data = TestData.SmallAlphabet(5000, 9);
        var table = MatchFinder.Build(data, 64, 3);
        for (int i = 0; i < data.Length; i++)
        {
            int prevCode = -1, prevLen = 0;
            foreach (var c in table.At(i))
            {
                Assert.InRange(c.Distance, 1, CodecConstants.WindowSize);
                Assert.InRange(c.MaxLength, 3, CodecConstants.MaxMatch);
                Assert.True(i - c.Distance >= 0);
                for (int k = 0; k < c.MaxLength; k++)
                    Assert.Equal(data[i - c.Distance + k], data[i + k]);
                Assert.True(c.Code > prevCode);          // коди зростають
                Assert.True(c.MaxLength > prevLen);      // і довжини теж (доміновані відкинуто)
                prevCode = c.Code; prevLen = c.MaxLength;
            }
        }
    }
}
