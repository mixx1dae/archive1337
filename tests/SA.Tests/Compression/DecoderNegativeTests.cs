using SA.Compression;
using SA.Compression.Lz77;
using SA.Core;
using Xunit;

namespace SA.Tests.Compression;

/// <summary>Розбір потоку — це розбір НЕДОВІРЕНИХ даних: кожна вада дає код відмови, а не падіння.</summary>
public class DecoderNegativeTests
{
    private static string CodeOf(Action act) => Assert.Throws<ArcException>(act).Code;

    [Fact]
    public void Truncated_IsEPayload()
    {
        var block = TestData.SpecBlock;
        Assert.Equal(ErrorCodes.Payload, CodeOf(() => BlockDecoder.Decode(block.AsSpan(0, block.Length - 1), 480)));
    }

    [Fact]
    public void ExtraByteAtEnd_IsEPayload()
    {
        var block = TestData.SpecBlock.Append((byte)0).ToArray();
        Assert.Equal(ErrorCodes.Payload, CodeOf(() => BlockDecoder.Decode(block, 480)));
    }

    [Fact]
    public void NonZeroPadding_IsECode()
    {
        var block = TestData.SpecBlock;
        // потік: 14 елементів займають менше 7 байтів; останній байт 0x18 = 0001 1000 — біти доповнення нульові.
        // Встановлюємо останній біт — це біт доповнення.
        block[^1] |= 0x01;
        Assert.Equal(ErrorCodes.Code, CodeOf(() => BlockDecoder.Decode(block, 480)));
    }

    [Fact]
    public void WrongRawLength_IsEPayload()
    {
        Assert.Equal(ErrorCodes.Payload, CodeOf(() => BlockDecoder.Decode(TestData.SpecBlock, 479)));
        Assert.Equal(ErrorCodes.Payload, CodeOf(() => BlockDecoder.Decode(TestData.SpecBlock, 481)));
    }

    [Fact]
    public void ReferenceBeyondBuffer_IsECode()
    {
        // перший елемент — посилання назад на 5 байтів, коли вихід ще порожній
        byte[] block = BlockEncoder.Encode(new[] { Token.Match(5, 3), Token.Literal(1), Token.Literal(2) });
        Assert.Equal(ErrorCodes.Code, CodeOf(() => BlockDecoder.Decode(block, 6)));
    }

    [Fact]
    public void DistTableWithSymbolSixteen_IsECode()
    {
        var good = TestData.SpecBlock;
        // таблиця DIST у еталоні: 00 01 00 04 01 (5 байтів). Підміняємо символ 4 на 16.
        int index = IndexOf(good, new byte[] { 0x00, 0x01, 0x00, 0x04, 0x01 });
        good[index + 3] = 0x10;
        Assert.Equal(ErrorCodes.Code, CodeOf(() => BlockDecoder.Decode(good, 480)));
    }

    [Fact]
    public void TokenCountLargerThanRawLen_IsEPayload()
    {
        var block = TestData.SpecBlock;
        block[block.Length - 15] = 0xFF;    // старший байт token_count (4 + 4 + 7 байтів до кінця)
        Assert.Equal(ErrorCodes.Payload, CodeOf(() => BlockDecoder.Decode(block, 480)));
    }

    [Fact]
    public void EmptyInput_IsEPayload()
    {
        Assert.Equal(ErrorCodes.Payload, CodeOf(() => BlockDecoder.Decode(ReadOnlySpan<byte>.Empty, 10)));
    }

    [Fact]
    public void RandomGarbage_NeverCrashesWithAnythingButArcException()
    {
        var rng = new Random(77);
        for (int round = 0; round < 3000; round++)
        {
            var junk = new byte[rng.Next(0, 80)];
            rng.NextBytes(junk);
            try { BlockDecoder.Decode(junk, rng.Next(0, 200)); }
            catch (ArcException) { /* очікувано */ }
        }
    }

    [Fact]
    public void BitFlippedBlock_IsRejectedOrDecodesToSomethingElse_ButNeverCrashes()
    {
        var original = BlockCodec.Compress(TestData.Text(5000, 8));
        var rng = new Random(5);
        for (int round = 0; round < 500; round++)
        {
            var copy = (byte[])original.Data.Clone();
            copy[rng.Next(copy.Length)] ^= (byte)(1 << rng.Next(8));
            try { BlockDecoder.Decode(copy, original.RawLength); }
            catch (ArcException) { }
        }
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i <= haystack.Length - needle.Length; i++)
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle)) return i;
        throw new InvalidOperationException("шаблон не знайдено");
    }
}
