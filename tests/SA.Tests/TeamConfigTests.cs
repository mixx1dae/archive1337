using Xunit;
using SA.Core;

namespace SA.Tests;


public class TeamConfigTests
{
    private static string WriteTemp(string json)
    {
        string path = Path.Combine(Path.GetTempPath(), $"team_config_{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Default_MatchesTeachersConfig_AndIsValid()
    {
        var cfg = TeamConfig.Default;
        cfg.Validate();

        Assert.Equal(65_536, cfg.MaxVolumeBytes);
        Assert.Equal(16_384, cfg.BlockSize);
        Assert.Equal(30.0, cfg.Q.TRefSeconds, 1e-12);
        Assert.Equal(0.61, cfg.F1Min, 1e-12);
        Assert.Equal(9, cfg.DpCosts.LiteralBits);
        Assert.Equal(17, cfg.DpCosts.MatchBaseBits);
        Assert.Equal("algo2026", cfg.Password);
        Assert.Equal(3, cfg.PriorityOf(0));  // природний текст
        Assert.Equal(3, cfg.PriorityOf(1));  // вихідний код
        Assert.Equal(2, cfg.PriorityOf(3));  // растр без стиснення
        Assert.Equal(1, cfg.PriorityOf(5));  // вже стиснуте
        Assert.Equal(1, cfg.PriorityOf(6));  // штучні дані
    }

    [Fact]
    public void Load_ParsesNestedUkrainianSections()
    {
        string path = WriteTemp("""
            {
              "команда": "T99",
              "seed": 42,
              "пароль_сеансу": "pw",
              "метрика_Q": { "alpha": 0.6, "beta": 0.4, "t_ref_секунд": 5.0, "формула": "ігнорується" },
              "пакування": { "max_volume_bytes": 1000, "block_size": 64, "коментар_ліміту": "ігнорується" },
              "пріоритети_типів": {
                "природний текст": 7, "вихідний код": 6, "структуровані дані": 5, "растр без стиснення": 4,
                "виконуваний файл": 3, "вже стиснуте": 2, "штучні дані": 1
              },
              "класифікатор": { "F1_min": 0.9, "протокол_навчання": { "ініціалізацій": 5 } },
              "довідково": { "файлів_у_корпусі": 3, "сирий_розмір": 100, "розмір_архіву_опорного_пакувальника": 50, "коефіцієнт": 2.0 }
            }
            """);
        try
        {
            var cfg = TeamConfig.Load(path);
            Assert.Equal("T99", cfg.Team);
            Assert.Equal(42, cfg.Seed);
            Assert.Equal("pw", cfg.Password);
            Assert.Equal(0.6, cfg.Q.Alpha, 1e-12);
            Assert.Equal(5.0, cfg.Q.TRefSeconds, 1e-12);
            Assert.Equal(1000, cfg.MaxVolumeBytes);
            Assert.Equal(64, cfg.BlockSize);
            Assert.Equal(7, cfg.PriorityOf(0));
            Assert.Equal(1, cfg.PriorityOf(6));
            Assert.Equal(0.9, cfg.F1Min, 1e-12);
            Assert.Equal(5, cfg.Classifier.Training.Initializations);
            Assert.Equal(50L, cfg.Reference.ReferenceArchiveBytes);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_RejectsInvalidValues()
    {
        string[] bad =
        {
            """{ "метрика_Q": { "alpha": 0.5, "beta": 0.6, "t_ref_секунд": 10 } }""",
            """{ "метрика_Q": { "alpha": 0.5, "beta": 0.5, "t_ref_секунд": 0 } }""",
            """{ "пріоритети_типів": { "природний текст": 3 } }""",
            """{ "пріоритети_типів": { "природний текст": 3, "вихідний код": 3, "структуровані дані": 2, "растр без стиснення": 2, "виконуваний файл": 2, "вже стиснуте": 1, "штучні дані": 0 } }""",
            """{ "пакування": { "max_volume_bytes": 50, "block_size": 10 } }""",
            """{ "пакування": { "max_volume_bytes": 1000, "block_size": 0 } }""",
            """{ "класифікатор": { "F1_min": 1.5 } }""",
            "{ не json",
        };
        foreach (string json in bad)
        {
            string path = WriteTemp(json);
            try { Assert.Throws<InvalidDataException>(() => TeamConfig.Load(path)); }
            finally { File.Delete(path); }
        }
    }

    [Fact]
    public void PriorityOf_RejectsTypeAbove6()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TeamConfig.Default.PriorityOf(7));
    }

    [Fact]
    public void WithPacking_ChangesOnlyPackingSection()
    {
        var cfg = TeamConfig.Default.WithPacking(500, 50);
        Assert.Equal(500, cfg.MaxVolumeBytes);
        Assert.Equal(50, cfg.BlockSize);
        Assert.Equal(TeamConfig.Default.Q, cfg.Q);
        Assert.Equal(65_536, TeamConfig.Default.MaxVolumeBytes);
    }

    [Fact]
    public void ShippedTeamConfig_LoadsAndIsValid()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string path = Path.Combine(dir.FullName, "team_config.json");
            if (!File.Exists(path)) continue;

            var cfg = TeamConfig.Load(path);
            Assert.Equal(65_536, cfg.MaxVolumeBytes);
            Assert.Equal(16_384, cfg.BlockSize);
            Assert.Equal(0.61, cfg.F1Min, 1e-12);
            Assert.Equal(30.0, cfg.Q.TRefSeconds, 1e-12);
            Assert.Equal("algo2026", cfg.Password);
            Assert.Equal(128_968L, cfg.Reference.ReferenceArchiveBytes);
            return;
        }
        return;
    }
}
