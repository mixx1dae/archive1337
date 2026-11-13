using SA.Benchmark;
using SA.Compression;
using SA.Core;
using SA.Format;
using SA.Packing;

const string Usage = """
    Використання:
      SA.Cli [--config team_config.json] pack    <каталог_виходу> <пароль> <файл>...
      SA.Cli [--config team_config.json] unpack  <каталог_виходу> <пароль> <том>...
      SA.Cli [--config team_config.json] measure <каталог_корпусу> <пароль> [dp|greedy]

    Без --config береться team_config.json з поточного каталогу, а якщо його немає, значення за замовчуванням.
    """;

var positional = new List<string>();
string? configPath = null;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--config" && i + 1 < args.Length) configPath = args[++i];
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

    // DecodeOnlyCompressor розпаковує будь-що, а стискає лише без стиснення. Замініть його кодером П1,
    // а ConstantClassifier перцептроном П3, коли ці зони будуть злиті.
    IBlockCompressor compressor = new DecodeOnlyCompressor();
    IFileClassifier classifier = new ConstantClassifier(0);

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
            var method = rest.Count > 0 && rest[0] == "greedy" ? PackingMethod.Greedy : PackingMethod.Dp;
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
