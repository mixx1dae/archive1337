using System.Text;

namespace SA.Attack;

/// <summary>
/// Базовий модуль атаки (розділ 3.7): підбір ключа слабкого шифру (XOR або Віженер)
/// генетичним алгоритмом без знання ключа.
/// </summary>
public static class WeakCipherAttack
{
    public static (byte[] Key, string PlainText) CrackXorWithGa(
        ReadOnlySpan<byte> ciphertext,
        int keyLength,
        int popSize = 40,
        int generations = 80,
        int seed = 2001)
    {
        var rng = new Random(seed);
        byte[] sample = ciphertext[..Math.Min(ciphertext.Length, 1500)].ToArray();

        double Fitness(byte[] key)
        {
            double score = 0;
            for (int i = 0; i < sample.Length; i++)
            {
                byte b = (byte)(sample[i] ^ key[i % keyLength]);
                score += ScorePlainByte(b);
            }
            return score;
        }

        var pop = Enumerable.Range(0, popSize)
            .Select(_ => Enumerable.Range(0, keyLength).Select(__ => (byte)rng.Next(256)).ToArray())
            .ToList();

        byte[] bestKey = pop[0];

        for (int gen = 0; gen < generations; gen++)
        {
            var scored = pop.Select(k => (Score: Fitness(k), Key: k))
                            .OrderByDescending(x => x.Score)
                            .ToList();

            bestKey = scored[0].Key;
            var nextPop = new List<byte[]> { (byte[])scored[0].Key.Clone(), (byte[])scored[1].Key.Clone() };

            while (nextPop.Count < popSize)
            {
                var p1 = scored[rng.Next(popSize / 2)].Key;
                var p2 = scored[rng.Next(popSize / 2)].Key;
                byte[] child = new byte[keyLength];
                int cut = rng.Next(1, Math.Max(1, keyLength));

                for (int i = 0; i < keyLength; i++)
                {
                    child[i] = i < cut ? p1[i] : p2[i];
                    if (rng.NextDouble() < 0.2)
                        child[i] = (byte)rng.Next(256);
                }
                nextPop.Add(child);
            }
            pop = nextPop;
        }

        byte[] fullPlain = new byte[ciphertext.Length];
        for (int i = 0; i < ciphertext.Length; i++)
            fullPlain[i] = (byte)(ciphertext[i] ^ bestKey[i % keyLength]);

        return (bestKey, Encoding.UTF8.GetString(fullPlain));
    }

    public static (byte[] Key, string PlainText) CrackVigenereBytesWithGa(
        ReadOnlySpan<byte> ciphertext,
        int keyLength,
        int popSize = 40,
        int generations = 80,
        int seed = 2001)
    {
        var rng = new Random(seed);
        byte[] sample = ciphertext[..Math.Min(ciphertext.Length, 1500)].ToArray();

        double Fitness(byte[] key)
        {
            double score = 0;
            for (int i = 0; i < sample.Length; i++)
            {
                byte b = (byte)((sample[i] - key[i % keyLength]) & 0xFF);
                score += ScorePlainByte(b);
            }
            return score;
        }

        var pop = Enumerable.Range(0, popSize)
            .Select(_ => Enumerable.Range(0, keyLength).Select(__ => (byte)rng.Next(256)).ToArray())
            .ToList();

        byte[] bestKey = pop[0];

        for (int gen = 0; gen < generations; gen++)
        {
            var scored = pop.Select(k => (Score: Fitness(k), Key: k))
                            .OrderByDescending(x => x.Score)
                            .ToList();

            bestKey = scored[0].Key;
            var nextPop = new List<byte[]> { (byte[])scored[0].Key.Clone(), (byte[])scored[1].Key.Clone() };

            while (nextPop.Count < popSize)
            {
                var p1 = scored[rng.Next(popSize / 2)].Key;
                var p2 = scored[rng.Next(popSize / 2)].Key;
                byte[] child = new byte[keyLength];
                int cut = rng.Next(1, Math.Max(1, keyLength));

                for (int i = 0; i < keyLength; i++)
                {
                    child[i] = i < cut ? p1[i] : p2[i];
                    if (rng.NextDouble() < 0.2)
                        child[i] = (byte)rng.Next(256);
                }
                nextPop.Add(child);
            }
            pop = nextPop;
        }

        byte[] fullPlain = new byte[ciphertext.Length];
        for (int i = 0; i < ciphertext.Length; i++)
            fullPlain[i] = (byte)((ciphertext[i] - bestKey[i % keyLength]) & 0xFF);

        return (bestKey, Encoding.UTF8.GetString(fullPlain));
    }

    private static double ScorePlainByte(byte b)
    {
        if (b == 32) return 2.2;                                  // пробіл
        if (b is 101 or 116 or 97 or 111 or 105 or 110) return 1.6; // e, t, a, o, i, n
        if ((b >= 97 && b <= 122) || (b >= 65 && b <= 90)) return 1.1;
        if (b >= 32 && b <= 126 || b == 10 || b == 13) return 0.3;
        return -3.0;
    }
}