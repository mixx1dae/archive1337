using SA.Core;

namespace SA.Tests;

[TestClass]
public class TeamConfigTests
{
    private static string WriteTemp(string json)
    {
        string path = Path.Combine(Path.GetTempPath(), $"team_config_{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    [TestMethod]
    public void Default_MatchesTeachersConfig_AndIsValid()
    {
        var cfg = TeamConfig.Default;
        cfg.Validate();

        Assert.AreEqual(65_536, cfg.MaxVolumeBytes);
        Assert.AreEqual(16_384, cfg.BlockSize);
        Assert.AreEqual(30.0, cfg.Q.TRefSeconds, 1e-12);
        Assert.AreEqual(0.61, cfg.F1Min, 1e-12);
        Assert.AreEqual(9, cfg.DpCosts.LiteralBits);
        Assert.AreEqual(17, cfg.DpCosts.MatchBaseBits);
        Assert.AreEqual("algo2026", cfg.Password);
        Assert.AreEqual(3, cfg.PriorityOf(0));  // природний текст
        Assert.AreEqual(3, cfg.PriorityOf(1));  // вихідний код
        Assert.AreEqual(2, cfg.PriorityOf(3));  // растр без стиснення
        Assert.AreEqual(1, cfg.PriorityOf(5));  // вже стиснуте
        Assert.AreEqual(1, cfg.PriorityOf(6));  // штучні дані
    }

    [TestMethod]
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
            Assert.AreEqual("T99", cfg.Team);
            Assert.AreEqual(42, cfg.Seed);
            Assert.AreEqual("pw", cfg.Password);
            Assert.AreEqual(0.6, cfg.Q.Alpha, 1e-12);
            Assert.AreEqual(5.0, cfg.Q.TRefSeconds, 1e-12);
            Assert.AreEqual(1000, cfg.MaxVolumeBytes);
            Assert.AreEqual(64, cfg.BlockSize);
            Assert.AreEqual(7, cfg.PriorityOf(0));
            Assert.AreEqual(1, cfg.PriorityOf(6));
            Assert.AreEqual(0.9, cfg.F1Min, 1e-12);
            Assert.AreEqual(5, cfg.Classifier.Training.Initializations);
            Assert.AreEqual(50L, cfg.Reference.ReferenceArchiveBytes);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
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
            try { Assert.ThrowsExactly<InvalidDataException>(() => TeamConfig.Load(path)); }
            finally { File.Delete(path); }
        }
    }

    [TestMethod]
    public void PriorityOf_RejectsTypeAbove6()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TeamConfig.Default.PriorityOf(7));
    }

    [TestMethod]
    public void WithPacking_ChangesOnlyPackingSection()
    {
        var cfg = TeamConfig.Default.WithPacking(500, 50);
        Assert.AreEqual(500, cfg.MaxVolumeBytes);
        Assert.AreEqual(50, cfg.BlockSize);
        Assert.AreEqual(TeamConfig.Default.Q, cfg.Q);
        Assert.AreEqual(65_536, TeamConfig.Default.MaxVolumeBytes);
    }

    [TestMethod]
    public void ShippedTeamConfig_LoadsAndIsValid()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string path = Path.Combine(dir.FullName, "team_config.json");
            if (!File.Exists(path)) continue;

            var cfg = TeamConfig.Load(path);
            Assert.AreEqual(65_536, cfg.MaxVolumeBytes);
            Assert.AreEqual(16_384, cfg.BlockSize);
            Assert.AreEqual(0.61, cfg.F1Min, 1e-12);
            Assert.AreEqual(30.0, cfg.Q.TRefSeconds, 1e-12);
            Assert.AreEqual("algo2026", cfg.Password);
            Assert.AreEqual(128_968L, cfg.Reference.ReferenceArchiveBytes);
            return;
        }
        Assert.Inconclusive("team_config.json не знайдено вище каталогу тестів.");
    }
}
