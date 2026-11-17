using System.Text.Json;

using SA.Core;

namespace SA.Analysis;

/// <summary>
/// Багатошаровий перцептрон для класифікації файлів (класи 0..6),
/// написаний з нуля без сторонніх ML-бібліотек.
/// </summary>
public sealed class PerceptronClassifier : IFileClassifier
{
    public int InputDim { get; set; } = FileFeatures.FeatureCount;
    public int HiddenDim { get; set; } = 32;
    public int NumClasses { get; set; } = TeamConfig.TypeCount;

    public double[][] W1 { get; set; } = [];
    public double[] B1 { get; set; } = [];
    public double[][] W2 { get; set; } = [];
    public double[] B2 { get; set; } = [];

    public PerceptronClassifier() { }

    public PerceptronClassifier(int seed)
    {
        var rng = new Random(seed);
        W1 = new double[HiddenDim][];
        B1 = new double[HiddenDim];
        W2 = new double[NumClasses][];
        B2 = new double[NumClasses];

        for (int j = 0; j < HiddenDim; j++)
        {
            W1[j] = new double[InputDim];
            for (int i = 0; i < InputDim; i++)
                W1[j][i] = (rng.NextDouble() * 2.0 - 1.0) * 0.2;
        }

        for (int k = 0; k < NumClasses; k++)
        {
            W2[k] = new double[HiddenDim];
            for (int j = 0; j < HiddenDim; j++)
                W2[k][j] = (rng.NextDouble() * 2.0 - 1.0) * 0.2;
        }
    }

    /// <summary>
    /// Головний метод інтерфейсу IFileClassifier: повертає тип файлу (0..6).
    /// </summary>
    public byte Classify(ReadOnlySpan<byte> data)
    {
        double[] features = FileFeatures.Extract(data);
        if (W1.Length == 0)
            return FallbackClassify(features);

        double[] probs = PredictProbs(features);
        int best = 0;
        for (int k = 1; k < NumClasses; k++)
            if (probs[k] > probs[best]) best = k;

        return (byte)best;
    }

    private static byte FallbackClassify(double[] f)
    {
        if (f[0] >= 0.96) return 5; // вже стиснуте / шум
        if (f[33] >= 0.85) return 0; // текст / код
        return 4;                    // бінарні дані
    }

    public double[] PredictProbs(double[] x)
    {
        double[] h = new double[HiddenDim];
        for (int j = 0; j < HiddenDim; j++)
        {
            double sum = B1[j];
            for (int i = 0; i < InputDim; i++)
                sum += W1[j][i] * x[i];
            h[j] = sum > 0 ? sum : 0;
        }

        double[] logits = new double[NumClasses];
        for (int k = 0; k < NumClasses; k++)
        {
            double sum = B2[k];
            for (int j = 0; j < HiddenDim; j++)
                sum += W2[k][j] * h[j];
            logits[k] = sum;
        }

        double maxLogit = logits.Max();
        double[] probs = new double[NumClasses];
        double expSum = 0;
        for (int k = 0; k < NumClasses; k++)
        {
            probs[k] = Math.Exp(logits[k] - maxLogit);
            expSum += probs[k];
        }
        for (int k = 0; k < NumClasses; k++)
            probs[k] /= expSum;

        return probs;
    }

    public void TrainEpoch(IReadOnlyList<double[]> X, IReadOnlyList<byte> Y, double lr = 0.03)
    {
        for (int idx = 0; idx < X.Count; idx++)
        {
            double[] x = X[idx];
            int target = Y[idx];

            double[] z1 = new double[HiddenDim];
            double[] h1 = new double[HiddenDim];
            for (int j = 0; j < HiddenDim; j++)
            {
                double s = B1[j];
                for (int i = 0; i < InputDim; i++) s += W1[j][i] * x[i];
                z1[j] = s;
                h1[j] = s > 0 ? s : 0;
            }

            double[] probs = PredictProbs(x);

            double[] dz2 = new double[NumClasses];
            for (int k = 0; k < NumClasses; k++)
                dz2[k] = probs[k] - (k == target ? 1.0 : 0.0);

            double[] dz1 = new double[HiddenDim];
            for (int j = 0; j < HiddenDim; j++)
            {
                double grad = 0;
                for (int k = 0; k < NumClasses; k++)
                    grad += W2[k][j] * dz2[k];
                dz1[j] = z1[j] > 0 ? grad : 0;
            }

            for (int k = 0; k < NumClasses; k++)
            {
                for (int j = 0; j < HiddenDim; j++)
                    W2[k][j] -= lr * dz2[k] * h1[j];
                B2[k] -= lr * dz2[k];
            }

            for (int j = 0; j < HiddenDim; j++)
            {
                for (int i = 0; i < InputDim; i++)
                    W1[j][i] -= lr * dz1[j] * x[i];
                B1[j] -= lr * dz1[j];
            }
        }
    }

    public double EvaluateMacroF1(IReadOnlyList<double[]> X, IReadOnlyList<byte> Y)
    {
        int[,] cm = new int[NumClasses, NumClasses];
        for (int i = 0; i < X.Count; i++)
        {
            double[] probs = PredictProbs(X[i]);
            int pred = 0;
            for (int k = 1; k < NumClasses; k++)
                if (probs[k] > probs[pred]) pred = k;
            cm[Y[i], pred]++;
        }

        double f1Sum = 0;
        for (int c = 0; c < NumClasses; c++)
        {
            int tp = cm[c, c];
            int fp = 0, fn = 0;
            for (int r = 0; r < NumClasses; r++) if (r != c) fp += cm[r, c];
            for (int col = 0; col < NumClasses; col++) if (col != c) fn += cm[c, col];

            int denom = 2 * tp + fp + fn;
            f1Sum += denom > 0 ? (2.0 * tp) / denom : 0.0;
        }
        return f1Sum / NumClasses;
    }

    /// <summary>
    /// Протокол навчання з методички: N ініціалізацій на train/, вибір найкращої за macro-F1 на valid/.
    /// </summary>
    public static (PerceptronClassifier BestModel, double BestF1) TrainWithProtocol(
        IReadOnlyList<double[]> trainX,
        IReadOnlyList<byte> trainY,
        IReadOnlyList<double[]> validX,
        IReadOnlyList<byte> validY,
        TeamConfig config,
        int epochs = 60)
    {
        int runs = config.Classifier.Training.Initializations;
        PerceptronClassifier? bestModel = null;
        double bestF1 = -1.0;

        for (int run = 0; run < runs; run++)
        {
            var model = new PerceptronClassifier(config.Seed + run * 17);
            for (int ep = 0; ep < epochs; ep++)
            {
                double lr = 0.04 * Math.Pow(0.98, ep);
                model.TrainEpoch(trainX, trainY, lr);
                double f1 = model.EvaluateMacroF1(validX, validY);
                if (f1 > bestF1)
                {
                    bestF1 = f1;
                    bestModel = LoadFromJson(JsonSerializer.Serialize(model));
                }
            }
        }

        return (bestModel!, bestF1);
    }

    public void Save(string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(this));

    public static PerceptronClassifier Load(string path) =>
        LoadFromJson(File.ReadAllText(path));

    private static PerceptronClassifier LoadFromJson(string json) =>
        JsonSerializer.Deserialize<PerceptronClassifier>(json)
        ?? throw new InvalidDataException("Не вдалося завантажити ваги перцептрона.");
}