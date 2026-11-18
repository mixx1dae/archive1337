using SA.Analysis;
using SA.Attack;
using SA.Benchmark;
using SA.Compression;
using SA.Core;
using SA.Format;
using SA.Optimization;
using SA.Packing;

const string Usage = """
    Використання:
      SA.Cli [--config team_config.json] [--model perceptron.json] pack    <каталог_виходу> <пароль> <файл>...
      SA.Cli [--config team_config.json] unpack  <каталог_виходу> <пароль> <том>...
      SA.Cli [--config team_config.json] measure  <каталог_корпусу> <пароль> [dp|greedy|sequential]
      SA.Cli [--config team_config.json] ablation <каталог_корпусу> <пароль>
      SA.Cli [--config team_config.json] train    <каталог_train> <каталог_valid> <ваги.json>
      SA.Cli [--config team_config.json] evolve   <каталог_корпусу> <пароль> [популяція] [поколінь] [журнал.csv] [повторів]
      SA.Cli [--config team_config.json] attack-integrity <вибірка[:номер_тому]> <пароль> <том>...

    --model (необов'язково): файл ваг перцептрона. --genes (необов'язково): гени з evolve (best_genes.json).
    Без --config береться team_config.json з поточного каталогу, а якщо його немає, значення за замовчуванням.
    """;

var positional = new List<string>();
string? configPath = null;
string? modelPath = null;
string? genesPath = null;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--config" && i + 1 < args.Length) configPath = args[++i];
    else if (args[i] == "--model" && i + 1 < args.Length) modelPath = args[++i];
    else if (args[i] == "--genes" && i + 1 < args.Length) genesPath = args[++i];
    else positional.Add(args[i]);
}

if (positional.Count < 3)
{
    Console.Error.WriteLine(Usage);
    return 2;
}

try
{
    TeamConfig config = configPath != null ? TeamConfig.Load(configPath)
        : File.Exists("team_config.json") ? TeamConfig.Load("team_config.json")
        : TeamConfig.Default;

    // Кодер П1 (LZ77 + Гаффман) і класифікатор П3. Якщо передано --model, беруться навчені ваги перцептрона,
    // інакше класифікатор працює на евристиці (див. PerceptronClassifier.Classify).
    IBlockCompressor compressor = genesPath != null
        ? new ChromosomeCompressor(System.Text.Json.JsonSerializer.Deserialize<ArchiverChromosome>(File.ReadAllText(genesPath))
                                   ?? throw new InvalidDataException("Не вдалося прочитати гени."))
        : new BlockCodecCompressor();
    IFileClassifier classifier = modelPath != null ? PerceptronClassifier.Load(modelPath) : new PerceptronClassifier();

    string command = positional[0];
    string target = positional[1];
    string password = positional[2];
    var rest = positional.Skip(3).ToList();

    switch (command)
    {
        case "pack":
        {
            if (rest.Count == 0) { Console.Error.WriteLine(Usage); return 2; }
            var inputs = rest.Select(p => new InputFile(Path.GetFileName(p), File.ReadAllBytes(p))).ToList();
            var volumes = ArchiveBuilder.Build(inputs, password, config, compressor, classifier);
            Directory.CreateDirectory(target);
            for (int i = 0; i < volumes.Count; i++)
                File.WriteAllBytes(Path.Combine(target, $"archive.{i + 1:D3}.arc"), volumes[i]);
            Console.WriteLine($"Створено томів: {volumes.Count}, файлів: {inputs.Count}.");
            return 0;
        }

        case "unpack":
        {
            if (rest.Count == 0) { Console.Error.WriteLine(Usage); return 2; }
            var volumes = rest.Select(File.ReadAllBytes).ToList();
            // Усе розбирається в пам'яті і лише потім пишеться на диск: при помилці нічого не створюється.
            var files = ArchiveReader.ExtractAll(volumes, password, compressor);

            string root = Path.GetFullPath(target);
            Directory.CreateDirectory(root);
            foreach (var f in files)
            {
                string full = Path.GetFullPath(Path.Combine(root, f.Name));
                if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    throw new ArchiveException(ArchiveErrorCode.E_PAYLOAD, "ім'я файла виходить за межі каталогу.");
                File.WriteAllBytes(full, f.Data);
            }
            Console.WriteLine($"Розпаковано файлів: {files.Count}.");
            return 0;
        }

        case "measure":
        {
            var method = rest.Count > 0 && rest[0] == "greedy" ? PackingMethod.Greedy
                : rest.Count > 0 && rest[0] == "sequential" ? PackingMethod.Sequential
                : PackingMethod.Dp;
            var inputs = CorpusRunner.LoadDirectory(target);
            var r = CorpusRunner.Run(inputs, password, config, compressor, classifier, method);

            Console.WriteLine($"Файлів:           {r.FileCount}");
            Console.WriteLine($"B_orig:           {r.OriginalBytes}");
            Console.WriteLine($"B_arc:            {r.ArchiveBytes}  (томів: {r.Volumes})");
            Console.WriteLine($"t, с:             {r.Seconds:F3}");
            Console.WriteLine($"Відновлено точно: {(r.AllRestored ? "так" : "НІ")}{(r.Error is null ? "" : " (" + r.Error + ")")}");
            Console.WriteLine($"S = {r.S:F4}   T = {r.T:F4}   Q = {r.Q:F4}");
            double ratio = r.ArchiveBytes == 0 ? 0 : (double)r.OriginalBytes / r.ArchiveBytes;
            Console.WriteLine(
                $"Коефіцієнт стиснення: {ratio:F3} (опорний пакувальник: {config.Reference.Ratio:F3}, " +
                $"архів {config.Reference.ReferenceArchiveBytes} байтів із {config.Reference.RawBytes})");
            return r.AllRestored ? 0 : 1;
        }

        case "ablation":
        {
            var inputs = CorpusRunner.LoadDirectory(target);
            var rows = Ablation.Run(inputs, password, config, compressor, classifier);
            Console.Write(Ablation.ToMarkdown(rows));
            return rows.All(r => r.Result.AllRestored) ? 0 : 1;
        }

        case "train":
        {
            // train <каталог_train> <каталог_valid> <вихідний_файл_ваг.json>
            string validDir = password;
            if (rest.Count == 0) { Console.Error.WriteLine(Usage); return 2; }
            string outModel = rest[0];
            var (trX, trY, _) = LoadLabeled(target);
            var (vaX, vaY, vaFiles) = LoadLabeled(validDir);
            var (best, f1) = PerceptronClassifier.TrainWithProtocol(trX, trY, vaX, vaY, config);
            best.Save(outModel);
            Console.WriteLine($"train: {trX.Count} файлів, valid: {vaX.Count} файлів, ініціалізацій: {config.Classifier.Training.Initializations}");
            Console.WriteLine($"macro-F1 на valid: {f1:F4}. Ваги збережено: {outModel}");
            var cm = new int[TeamConfig.TypeCount, TeamConfig.TypeCount];
            for (int i = 0; i < vaFiles.Count; i++) cm[vaY[i], best.Classify(vaFiles[i])]++;
            Console.WriteLine("Матриця помилок (рядки: істинний клас, стовпці: передбачений):");
            for (int a = 0; a < TeamConfig.TypeCount; a++)
                Console.WriteLine($"  {a}: " + string.Join(" ", Enumerable.Range(0, TeamConfig.TypeCount).Select(b => cm[a, b].ToString().PadLeft(4))));
            return 0;
        }

        case "evolve":
        {
            // evolve <каталог_корпусу> <пароль> [популяція] [поколінь] [журнал.csv] [повторів]
            int pop = rest.Count > 0 ? int.Parse(rest[0]) : 12;
            int gens = rest.Count > 1 ? int.Parse(rest[1]) : 10;
            string logPath = rest.Count > 2 ? rest[2] : "evolution_log.csv";
            int repeats = rest.Count > 3 ? int.Parse(rest[3]) : 1;
            var inputs = CorpusRunner.LoadDirectory(target);
            int[] wins = [1024, 2048, 4096, 8192, 16384, 32768];
            string Key(ArchiverChromosome g) => FormattableString.Invariant($"{g.WindowSize}|{g.MinMatchLen}|{g.RejectEntropy:F3}");
            FitnessEvaluator<ArchiverChromosome> NewEval() => new(
                inputs, password, g => new EvaluationSetup(config, new ChromosomeCompressor(g), classifier), Key);

            var gaBests = new List<double>(); var rndBests = new List<double>();
            ArchiverChromosome? overall = null; double overallQ = -1;
            for (int r = 0; r < repeats; r++)
            {
                int seed = config.Seed + r * 101;
                string log = repeats == 1 ? logPath : Path.ChangeExtension(logPath, null) + $"_run{r}.csv";
                var ev = NewEval();
                var bestGa = new GeneticOptimizer(seed).Optimize(ev.Evaluate, pop, gens, log);
                double qGa = ev.Evaluate(bestGa);
                int budget = ev.Evaluations;

                var rnd = new Random(seed + 1);
                var evR = NewEval();
                int guard = 0;
                while (evR.Evaluations < budget && guard++ < budget * 20)
                    evR.Evaluate(new ArchiverChromosome
                    {
                        WindowSize = wins[rnd.Next(wins.Length)],
                        MinMatchLen = rnd.Next(3, 9),
                        RejectEntropy = Math.Round(0.85 + rnd.NextDouble() * 0.14, 3)
                    });
                File.WriteAllLines(Path.ChangeExtension(log, null) + "_convergence.csv",
                    new[] { "evaluation,ga_best,random_best" }.Concat(
                        Enumerable.Range(0, Math.Min(ev.History.Count, evR.History.Count))
                            .Select(i => FormattableString.Invariant($"{i + 1},{ev.History[i]:F6},{evR.History[i]:F6}"))));

                gaBests.Add(qGa); rndBests.Add(evR.BestSoFar);
                Console.WriteLine(FormattableString.Invariant(
                    $"запуск {r} (seed {seed}): ГА Q = {qGa:F4} (вікно {bestGa.WindowSize}, збіг {bestGa.MinMatchLen}, поріг {bestGa.RejectEntropy:F3}); випадковий Q = {evR.BestSoFar:F4}; обчислень {budget}, кеш {ev.CacheHits}"));
                if (qGa > overallQ) { overallQ = qGa; overall = bestGa; }
            }

            static double Sd(List<double> v) => v.Count < 2 ? 0 : Math.Sqrt(v.Sum(x => (x - v.Average()) * (x - v.Average())) / (v.Count - 1));
            Console.WriteLine(FormattableString.Invariant(
                $"ГА:        середнє Q = {gaBests.Average():F4}, σ = {Sd(gaBests):F4}, мін = {gaBests.Min():F4}, макс = {gaBests.Max():F4}"));
            Console.WriteLine(FormattableString.Invariant(
                $"Випадковий: середнє Q = {rndBests.Average():F4}, σ = {Sd(rndBests):F4}, мін = {rndBests.Min():F4}, макс = {rndBests.Max():F4}"));
            File.WriteAllText("best_genes.json", System.Text.Json.JsonSerializer.Serialize(overall));
            Console.WriteLine("Найкращі гени збережено: best_genes.json (застосування: --genes best_genes.json)");
            return 0;
        }

        case "attack-integrity":
        {
            // target = «вибірка» або «вибірка:номер_тому»; атакується один том із переданих
            var parts = target.Split(':');
            if (rest.Count == 0 || !int.TryParse(parts[0], out int samples)) { Console.Error.WriteLine(Usage); return 2; }
            int volumeIndex = parts.Length > 1 && int.TryParse(parts[1], out int vi) ? vi : 0;

            var volumes = rest.Select(File.ReadAllBytes).ToList();
            var result = IntegrityAttack.Run(volumes, password, compressor, volumeIndex, samples);

            Console.WriteLine($"Змін: {result.Mutations}; відхилено: {result.Rejected}; ПРИЙНЯТО: {result.Accepted}; необроблених винятків: {result.Unhandled}");
            foreach (var kv in result.RejectedByCode.OrderBy(k => k.Key))
                Console.WriteLine($"  {kv.Key}: {kv.Value}");
            foreach (var d in result.AcceptedMutations.Take(10))
                Console.WriteLine($"  прийнята зміна: {d}");
            Console.WriteLine(result.Survived ? "Результат: розархіватор витримав атаку." : "Результат: ЗНАЙДЕНО ПОМИЛКУ ЗАХИСТУ.");
            return result.Survived ? 0 : 1;
        }

        default:
            Console.Error.WriteLine(Usage);
            return 2;
    }
}
catch (ArchiveException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                               or PackingCapacityException or ArgumentException)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static (List<double[]> X, List<byte> Y, List<byte[]> Files) LoadLabeled(string dir)
{
    var x = new List<double[]>(); var y = new List<byte>(); var files = new List<byte[]>();
    foreach (var line in File.ReadLines(Path.Combine(dir, "labels.csv")).Skip(1))
    {
        var c = line.Split(',');
        if (c.Length < 2) continue;
        byte[] data = File.ReadAllBytes(Path.Combine(dir, c[0]));
        x.Add(FileFeatures.Extract(data)); y.Add(byte.Parse(c[1])); files.Add(data);
    }
    return (x, y, files);
}

/// <summary>Компресор з параметрами хромосоми ГА: вікно → глибина ланцюжка, мін. збіг, поріг ентропії для NO_COMPRESSION.</summary>
sealed class ChromosomeCompressor(ArchiverChromosome g) : IBlockCompressor
{
    private readonly BlockCodecCompressor _packed = new(new CodecOptions { MinMatch = g.MinMatchLen, MaxChain = Math.Max(4, g.WindowSize / 256) });
    private readonly BlockCodecCompressor _stored = new(new CodecOptions { ForceStore = true });

    public SA.Core.CompressedBlock Compress(ReadOnlySpan<byte> raw)
        => FileFeatures.ShouldSkipCompression(raw, g.RejectEntropy) ? _stored.Compress(raw) : _packed.Compress(raw);

    public byte[] Decompress(ReadOnlySpan<byte> coded, int rawLength, bool noCompression)
        => _packed.Decompress(coded, rawLength, noCompression);
}
