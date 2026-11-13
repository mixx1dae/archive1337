using System.Text;
using SA.Core;
using SA.Format;

namespace SA.Packing;

public enum PackingMethod { Dp, Greedy }

/// <summary>Помилка конфігурації пакування: таблиця файлів або блок не вміщуються в том.</summary>
public sealed class PackingCapacityException : Exception
{
    public PackingCapacityException(string message) : base(message) { }
}

public sealed record PackingBlock(uint Seq, uint RawLen, byte[] Coded, bool NoCompression);

public sealed record PackingFile(
    string Name, uint OrigSize, uint Crc, byte FileType, byte Strategy, IReadOnlyList<PackingBlock> Blocks);

/// <summary>
/// Розкладання закодованих блоків по томах (FORMAT_SPEC_P2.md, розділ 7).
///   вага(блок) = coded_len + 15,
///   W = max_volume_bytes − 41 − 32 − 6 − (розмір таблиці файлів тому),
///   цінність = пріоритет типу файла.
/// Таблиця файлів тому визначається ДО рюкзака: це всі файли, що ще мають блоки-кандидати.
/// Файли без блоків (нульового розміру) потрапляють у перший том.
/// </summary>
public static class VolumePacker
{
    /// <summary>41 + 32 + 6 = 79 байтів накладних витрат тому без таблиці файлів.</summary>
    public const int VolumeOverhead = VolumeHeader.Size + VolumeHeader.TagSize + 6;

    public const int BlockRecordSize = BlockEntry.Size;

    /// <summary>name_len(2) + ім'я + orig_size(4) + crc32(4) + file_type(1) + strategy(1).</summary>
    public static int TableEntrySize(string name) => 12 + Encoding.UTF8.GetByteCount(name);

    public static IReadOnlyList<VolumePayload> Pack(
        IReadOnlyList<PackingFile> files, TeamConfig config, PackingMethod method = PackingMethod.Dp)
    {
        if (files.Count > ushort.MaxValue)
            throw new PackingCapacityException("Забагато файлів: таблиця файлів має до 65535 записів.");

        var remaining = new List<(int File, int Block)>();
        for (int f = 0; f < files.Count; f++)
            for (int b = 0; b < files[f].Blocks.Count; b++)
                remaining.Add((f, b));

        var emptyFiles = Enumerable.Range(0, files.Count).Where(i => files[i].Blocks.Count == 0).ToList();
        var volumes = new List<VolumePayload>();
        bool first = true;

        do
        {
            // 1. Склад таблиці файлів фіксується до рюкзака.
            var fileSet = remaining.Select(r => r.File).Distinct().ToList();
            if (first) fileSet = fileSet.Union(emptyFiles).ToList();
            fileSet.Sort();

            long tableSize = fileSet.Sum(i => (long)TableEntrySize(files[i].Name));
            long capacity = config.MaxVolumeBytes - VolumeOverhead - tableSize;
            if (capacity < 0)
                throw new PackingCapacityException(
                    $"Таблиця файлів ({tableSize} байтів) не вміщується в том max_volume_bytes = {config.MaxVolumeBytes}.");

            // 2. Рюкзак по блоках-кандидатах.
            int[] chosen = Array.Empty<int>();
            if (remaining.Count > 0)
            {
                var items = remaining
                    .Select(r => new KnapsackItem(
                        files[r.File].Blocks[r.Block].Coded.Length + BlockRecordSize,
                        config.PriorityOf(files[r.File].FileType)))
                    .ToList();

                chosen = method == PackingMethod.Dp
                    ? Knapsack.SolveDp(items, (int)capacity)
                    : Knapsack.SolveGreedy(items, (int)capacity);

                if (chosen.Length == 0)
                    throw new PackingCapacityException(
                        $"Жоден блок не вміщується в доступну місткість {capacity} байтів " +
                        "(збільште max_volume_bytes або зменште block_size).");
            }

            // 3. Збирання тому: блоки за (файл, block_seq) для відтворюваності.
            var chosenBlocks = chosen
                .Select(i => remaining[i])
                .OrderBy(r => r.File)
                .ThenBy(r => files[r.File].Blocks[r.Block].Seq)
                .ToList();

            var localIndex = new Dictionary<int, int>();
            for (int k = 0; k < fileSet.Count; k++) localIndex[fileSet[k]] = k;

            var entries = fileSet
                .Select(i => new FileEntry(files[i].Name, files[i].OrigSize, files[i].Crc, files[i].FileType, files[i].Strategy))
                .ToList();
            var blockEntries = new List<BlockEntry>(chosenBlocks.Count);
            var blockData = new List<byte[]>(chosenBlocks.Count);
            foreach (var (f, b) in chosenBlocks)
            {
                var pb = files[f].Blocks[b];
                byte flags = pb.NoCompression ? BlockEntry.NoCompressionFlag : (byte)0;
                blockEntries.Add(new BlockEntry((ushort)localIndex[f], pb.Seq, pb.RawLen, (uint)pb.Coded.Length, flags));
                blockData.Add(pb.Coded);
            }

            volumes.Add(new VolumePayload(0, entries, blockEntries, blockData));

            var chosenSet = new HashSet<int>(chosen);
            remaining = remaining.Where((_, i) => !chosenSet.Contains(i)).ToList();
            first = false;

            if (volumes.Count > ushort.MaxValue)
                throw new PackingCapacityException("Забагато томів: volume_count — це uint16.");
        }
        while (remaining.Count > 0);

        return volumes;
    }
}
