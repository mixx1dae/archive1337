using System.Text.Json;
using System.Text.Json.Serialization;

namespace SA.Core;

public sealed record QConfig
{
    [JsonPropertyName("alpha")] public double Alpha { get; init; } = 0.7;
    [JsonPropertyName("beta")] public double Beta { get; init; } = 0.3;
    [JsonPropertyName("t_ref_секунд")] public double TRefSeconds { get; init; } = 30.0;
}

public sealed record PackingConfig
{
    [JsonPropertyName("max_volume_bytes")] public int MaxVolumeBytes { get; init; } = 65_536;
    [JsonPropertyName("block_size")] public int BlockSize { get; init; } = 16_384;
}

public sealed record DpCostConfig
{
    [JsonPropertyName("літерал_біт")] public int LiteralBits { get; init; } = 9;
    [JsonPropertyName("посилання_база_біт")] public int MatchBaseBits { get; init; } = 17;
}

public sealed record TrainingProtocol
{
    [JsonPropertyName("ініціалізацій")] public int Initializations { get; init; } = 3;
}

public sealed record ClassifierConfig
{
    [JsonPropertyName("F1_min")] public double F1Min { get; init; } = 0.61;

    /// <summary>Номер класу (рядком, «0»…«6») → назва типу. Назви збігаються з ключами пріоритетів.</summary>
    [JsonPropertyName("класи")]
    public Dictionary<string, string> Classes { get; init; } = new()
    {
        ["0"] = "природний текст",
        ["1"] = "вихідний код",
        ["2"] = "структуровані дані",
        ["3"] = "растр без стиснення",
        ["4"] = "виконуваний файл",
        ["5"] = "вже стиснуте",
        ["6"] = "штучні дані",
    };

    [JsonPropertyName("базова_лінія_мажоритарний_клас")] public double MajorityBaseline { get; init; } = 0.0357;
    [JsonPropertyName("базова_лінія_випадковий_вибір")] public double RandomBaseline { get; init; } = 0.1614;
    [JsonPropertyName("протокол_навчання")] public TrainingProtocol Training { get; init; } = new();
}

/// <summary>Довідкові числа опорного пакувальника викладача на корпусі команди.</summary>
public sealed record ReferenceInfo
{
    [JsonPropertyName("файлів_у_корпусі")] public int FileCount { get; init; } = 21;
    [JsonPropertyName("сирий_розмір")] public long RawBytes { get; init; } = 329_903;
    [JsonPropertyName("розмір_архіву_опорного_пакувальника")] public long ReferenceArchiveBytes { get; init; } = 128_968;
    [JsonPropertyName("коефіцієнт")] public double Ratio { get; init; } = 2.558;
}

/// <summary>
/// Налаштування команди (team_config.json від викладача). Структура ключів повторює видану:
/// секції «метрика_Q», «пакування», «пріоритети_типів», «фіксовані_вартості_ДП», «класифікатор», «довідково».
/// </summary>
public sealed record TeamConfig
{
    public const int TypeCount = 7;

    [JsonPropertyName("команда")] public string Team { get; init; } = "T01";
    [JsonPropertyName("seed")] public int Seed { get; init; } = 2001;
    [JsonPropertyName("версія_формату")] public int FormatVersion { get; init; } = 1;
    [JsonPropertyName("пароль_сеансу")] public string Password { get; init; } = "algo2026";
    [JsonPropertyName("метрика_Q")] public QConfig Q { get; init; } = new();
    [JsonPropertyName("пакування")] public PackingConfig Packing { get; init; } = new();

    /// <summary>Цінність блока за назвою типу файла (всі значення ≥ 1).</summary>
    [JsonPropertyName("пріоритети_типів")]
    public Dictionary<string, int> Priorities { get; init; } = new()
    {
        ["природний текст"] = 3,
        ["вихідний код"] = 3,
        ["структуровані дані"] = 2,
        ["растр без стиснення"] = 2,
        ["виконуваний файл"] = 2,
        ["вже стиснуте"] = 1,
        ["штучні дані"] = 1,
    };

    [JsonPropertyName("фіксовані_вартості_ДП")] public DpCostConfig DpCosts { get; init; } = new();
    [JsonPropertyName("класифікатор")] public ClassifierConfig Classifier { get; init; } = new();
    [JsonPropertyName("довідково")] public ReferenceInfo Reference { get; init; } = new();

    [JsonIgnore] public int MaxVolumeBytes => Packing.MaxVolumeBytes;
    [JsonIgnore] public int BlockSize => Packing.BlockSize;
    [JsonIgnore] public double F1Min => Classifier.F1Min;

    public static TeamConfig Default { get; } = new();

    /// <summary>Копія з іншими max_volume_bytes і block_size (для тестів і експериментів).</summary>
    public TeamConfig WithPacking(int maxVolumeBytes, int blockSize)
        => this with { Packing = new PackingConfig { MaxVolumeBytes = maxVolumeBytes, BlockSize = blockSize } };

    private static readonly JsonSerializerOptions Options = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static TeamConfig Load(string path)
    {
        string json = File.ReadAllText(path);
        TeamConfig? cfg;
        try { cfg = JsonSerializer.Deserialize<TeamConfig>(json, Options); }
        catch (JsonException ex) { throw new InvalidDataException($"team_config.json: {ex.Message}", ex); }

        if (cfg is null) throw new InvalidDataException("team_config.json порожній.");
        cfg.Validate();
        return cfg;
    }

    public int PriorityOf(byte fileType)
    {
        if (fileType >= TypeCount) throw new ArgumentOutOfRangeException(nameof(fileType));
        string name = Classifier.Classes[fileType.ToString()];
        return Priorities[name];
    }

    public void Validate()
    {
        // 41 (заголовок) + 32 (тег) + 6 (службові поля навантаження)
        if (MaxVolumeBytes <= 79)
            throw new InvalidDataException("max_volume_bytes має бути більшим за 79.");
        if (BlockSize < 1)
            throw new InvalidDataException("block_size має бути додатним.");
        if (Q.Alpha < 0 || Q.Beta < 0 || Math.Abs(Q.Alpha + Q.Beta - 1.0) > 1e-9)
            throw new InvalidDataException("метрика_Q: alpha і beta невід'ємні, alpha + beta = 1.");
        if (Q.TRefSeconds <= 0)
            throw new InvalidDataException("метрика_Q: t_ref_секунд має бути додатним.");
        if (F1Min is < 0 or > 1)
            throw new InvalidDataException("класифікатор: F1_min має бути в [0, 1].");

        for (int t = 0; t < TypeCount; t++)
        {
            if (!Classifier.Classes.TryGetValue(t.ToString(), out string? name))
                throw new InvalidDataException($"класифікатор: немає класу {t}.");
            if (!Priorities.TryGetValue(name, out int priority) || priority < 1)
                throw new InvalidDataException($"пріоритети_типів: для «{name}» потрібне значення ≥ 1.");
        }
    }
}
