using SA.Core;
using SA.Packing;

namespace SA.Benchmark;

/// <summary>Усе, що потрібно для одного прогону системи за конкретними генами.</summary>
public sealed record EvaluationSetup(
    TeamConfig Config,
    IBlockCompressor Compressor,
    IFileClassifier Classifier,
    PackingMethod Method = PackingMethod.Dp);

/// <summary>
/// Пристосованість для генетичного алгоритму (зона П3): Q системи на корпусі за набором генів.
/// Результати кешуються за ключем генів; <see cref="Evaluations"/> рахує лише справжні (некешовані) обчислення,
/// щоб ГА і випадковий пошук порівнювалися за однакового бюджету обчислень. <see cref="History"/> дає криву
/// «найкраще на момент обчислення номер k» для графіка збіжності.
/// Q містить час T, тому два прогони однакових генів можуть трохи відрізнятися; кеш це згладжує.
/// </summary>
public sealed class FitnessEvaluator<TGenes> where TGenes : notnull
{
    private readonly IReadOnlyList<InputFile> _files;
    private readonly string _password;
    private readonly Func<TGenes, EvaluationSetup> _setupFactory;
    private readonly Func<TGenes, string> _keyOf;
    private readonly Dictionary<string, double> _cache = new(StringComparer.Ordinal);
    private readonly List<double> _history = new();
    private readonly object _lock = new();
    private double _best;

    public FitnessEvaluator(
        IReadOnlyList<InputFile> files,
        string password,
        Func<TGenes, EvaluationSetup> setupFactory,
        Func<TGenes, string> keyOf)
    {
        _files = files;
        _password = password;
        _setupFactory = setupFactory;
        _keyOf = keyOf;
    }

    /// <summary>Кількість справжніх обчислень Q.</summary>
    public int Evaluations { get; private set; }

    public int CacheHits { get; private set; }

    /// <summary>Найкраща Q на момент кожного справжнього обчислення (неспадна послідовність).</summary>
    public IReadOnlyList<double> History
    {
        get { lock (_lock) return _history.ToArray(); }
    }

    public double BestSoFar
    {
        get { lock (_lock) return _best; }
    }

    public double Evaluate(TGenes genes)
    {
        string key = _keyOf(genes);
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out double cached))
            {
                CacheHits++;
                return cached;
            }
        }

        EvaluationSetup setup = _setupFactory(genes);
        BenchmarkResult result = CorpusRunner.Run(
            _files, _password, setup.Config, setup.Compressor, setup.Classifier, setup.Method);
        double q = result.Q;

        lock (_lock)
        {
            _cache[key] = q;
            Evaluations++;
            if (q > _best) _best = q;
            _history.Add(_best);
        }
        return q;
    }
}
