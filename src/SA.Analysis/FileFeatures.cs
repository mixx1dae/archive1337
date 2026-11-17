namespace SA.Analysis;

public static class FileFeatures
{
    public const int FeatureCount = 52;

    /// <summary>
    /// Витягує 52 числові ознаки з байтів файлу для перцептрона.
    /// </summary>
    public static double[] Extract(ReadOnlySpan<byte> data, int sampleLimit = 65_536)
    {
        double[] features = new double[FeatureCount];
        if (data.IsEmpty)
            return features;

        int n = Math.Min(data.Length, sampleLimit);
        int[] counts = new int[256];
        int printable = 0;
        var newlines = new List<int>();

        for (int i = 0; i < n; i++)
        {
            byte b = data[i];
            counts[b]++;
            if ((b >= 32 && b <= 126) || b == 9 || b == 10 || b == 13)
                printable++;
            if (b == 10)
                newlines.Add(i);
        }

        // [0] Нормована ентропія Шеннона (0..1)
        double entropy = 0.0;
        for (int i = 0; i < 256; i++)
        {
            if (counts[i] > 0)
            {
                double p = (double)counts[i] / n;
                entropy -= p * Math.Log2(p);
            }
        }
        features[0] = entropy / 8.0;

        // [1..32] Згорнута гістограма байтів (32 кошики по 8 значень)
        for (int bin = 0; bin < 32; bin++)
        {
            int sum = 0;
            for (int k = 0; k < 8; k++)
                sum += counts[bin * 8 + k];
            features[1 + bin] = (double)sum / n;
        }

        // [33] Частка друкованих ASCII-символів
        features[33] = (double)printable / n;

        // [34] Нормована середня довжина рядка
        if (newlines.Count >= 2)
        {
            double totalDiff = 0;
            for (int i = 1; i < newlines.Count; i++)
                totalDiff += newlines[i] - newlines[i - 1];
            features[34] = Math.Min((totalDiff / (newlines.Count - 1)) / 256.0, 1.0);
        }
        else
        {
            features[34] = 1.0;
        }

        // [35..50] Автокореляція (лаги 1..16)
        double mean = 0;
        for (int i = 0; i < n; i++) mean += data[i];
        mean /= n;

        double var = 0;
        for (int i = 0; i < n; i++) var += (data[i] - mean) * (data[i] - mean);
        var /= n;

        for (int lag = 1; lag <= 16; lag++)
        {
            if (var < 1e-9 || n <= lag)
            {
                features[34 + lag] = 0.0;
            }
            else
            {
                double cov = 0;
                for (int i = 0; i < n - lag; i++)
                    cov += (data[i] - mean) * (data[i + lag] - mean);
                cov /= (n - lag);
                features[34 + lag] = Math.Clamp(cov / var, -1.0, 1.0);
            }
        }

        // [51] Нормована ентропія біграм
        if (n >= 2)
        {
            var bigrams = new Dictionary<int, int>();
            for (int i = 0; i < n - 1; i++)
            {
                int bg = (data[i] << 8) | data[i + 1];
                bigrams[bg] = bigrams.GetValueOrDefault(bg, 0) + 1;
            }
            double bgEntropy = 0;
            int totalBg = n - 1;
            foreach (int c in bigrams.Values)
            {
                double p = (double)c / totalBg;
                bgEntropy -= p * Math.Log2(p);
            }
            features[51] = bgEntropy / 16.0;
        }

        return features;
    }

    /// <summary>
    /// Перевірка для стратегії NO_COMPRESSION (висока ентропія = вже стиснутий файл або шум).
    /// </summary>
    public static bool ShouldSkipCompression(ReadOnlySpan<byte> data, double entropyThreshold = 0.96)
    {
        double[] f = Extract(data);
        return f[0] >= entropyThreshold && f[51] >= 0.85;
    }
}