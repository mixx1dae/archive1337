using System.Text;

namespace SA.Tests.Compression;

/// <summary>Детерміновані тестові дані (фіксований seed — тести відтворювані).</summary>
internal static class TestData
{
    // Блок з розд. 8 FORMAT_SPEC (57 байтів): три таблиці + token_count=14 + stream_len=7 + потік.
    public const string SpecBlockHex =
        "0009002003006404006504006804006C02006F03007204007703010003000200CF0100FF0100010004010000000E00000007ED06A3F3159D18";

    public static byte[] SpecBlock => Convert.FromHexString(SpecBlockHex);
    public static byte[] HelloWorld40 => Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("hello world ", 40)));

    public static byte[] Random(int length, int seed)
    {
        var data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }

    public static byte[] Text(int length, int seed)
    {
        string[] words = { "the", "quick", "brown", "fox", "jumps", "over", "lazy", "dog", "archive", "block", "huffman", "stream" };
        var rng = new Random(seed);
        var sb = new StringBuilder();
        while (sb.Length < length) sb.Append(words[rng.Next(words.Length)]).Append(' ');
        return Encoding.ASCII.GetBytes(sb.ToString(0, length));
    }

    /// <summary>Малий алфавіт, багато коротких повторів — навантаження на ДП.</summary>
    public static byte[] SmallAlphabet(int length, int seed)
    {
        var rng = new Random(seed);
        var data = new byte[length];
        for (int i = 0; i < length; i++) data[i] = (byte)"abc "[rng.Next(4)];
        return data;
    }

    /// <summary>Плавний градієнт із шумом (растр): багато коротких збігів, де «модель ДП» помиляється.</summary>
    public static byte[] NoisyGradient(int width, int height, int seed)
    {
        var rng = new Random(seed);
        var data = new byte[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                data[y * width + x] = (byte)Math.Clamp((x * 255 / width + y * 255 / height) / 2 + rng.Next(-3, 4), 0, 255);
        return data;
    }

    /// <summary>Випадковий початок + далекий повтор (відстань близько вікна).</summary>
    public static byte[] FarRepeat()
    {
        var head = Random(30000, 7);
        return head.Concat(head.Take(500)).ToArray();
    }
}
