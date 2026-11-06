using SA.Core;
using SA.Crypto;

namespace SA.Format;

public sealed record ExtractedFile(string Name, byte[] Data);

/// <summary>Розпакування набору томів у файли (кроки 1–10 розділу 4.3).</summary>
public static class ArchiveReader
{
    private sealed class FileAcc
    {
        public uint Size;
        public uint Crc;
        public readonly SortedDictionary<uint, byte[]> Blocks = new();
    }

    public static IReadOnlyList<ExtractedFile> ExtractAll(
        IReadOnlyList<byte[]> volumes, string password, IBlockCompressor compressor, bool allowPrediction = false)
    {
        if (volumes.Count == 0)
            throw new ArchiveException(ArchiveErrorCode.E_VOLUMES, "немає жодного тому.");

        // Кроки 1–8 для кожного тому. PBKDF2 виконується один раз на архів.
        DerivedKeys? keys = null;
        var opened = new List<(OpenedVolume Volume, byte[][] Decoded)>();
        foreach (byte[] bytes in volumes)
        {
            var header = VolumeHeader.Parse(bytes);
            keys ??= KeyDerivation.Derive(password, header.Salt);

            var volume = VolumeReader.Open(bytes, keys.Value, allowPrediction);
            var decoded = new byte[volume.Payload.Blocks.Count][];
            for (int j = 0; j < decoded.Length; j++)
                decoded[j] = DecodeBlock(compressor, volume.Payload.BlockData[j], volume.Payload.Blocks[j]);
            opened.Add((volume, decoded));
        }

        // Крок 9: повнота набору томів.
        int count = opened[0].Volume.Header.VolumeCount;
        foreach (var (v, _) in opened)
            if (v.Header.VolumeCount != count)
                throw new ArchiveException(ArchiveErrorCode.E_VOLUMES, "томи оголошують різне volume_count.");
        if (opened.Count != count)
            throw new ArchiveException(ArchiveErrorCode.E_VOLUMES,
                $"наявних томів {opened.Count}, а оголошено {count}.");
        var seen = new bool[count];
        foreach (var (v, _) in opened)
        {
            int idx = v.Header.VolumeIndex;
            if (idx >= count)
                throw new ArchiveException(ArchiveErrorCode.E_VOLUMES, "volume_index не менший за volume_count.");
            if (seen[idx])
                throw new ArchiveException(ArchiveErrorCode.E_VOLUMES, "volume_index трапляється двічі.");
            seen[idx] = true;
        }

        // Крок 10: склеювання за парою (ім'я, block_seq) і перевірка CRC32.
        var accs = new Dictionary<string, FileAcc>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var (v, decoded) in opened.OrderBy(t => t.Volume.Header.VolumeIndex))
        {
            var files = v.Payload.Files;
            foreach (var f in files)
            {
                if (!accs.TryGetValue(f.Name, out var acc))
                {
                    acc = new FileAcc { Size = f.OrigSize, Crc = f.Crc };
                    accs[f.Name] = acc;
                    order.Add(f.Name);
                }
                else if (acc.Size != f.OrigSize || acc.Crc != f.Crc)
                {
                    throw new ArchiveException(ArchiveErrorCode.E_PAYLOAD,
                        "томи описують один файл з різними розміром або CRC32.");
                }
            }
            for (int j = 0; j < v.Payload.Blocks.Count; j++)
            {
                var b = v.Payload.Blocks[j];
                var acc = accs[files[b.FileIndex].Name];
                if (!acc.Blocks.TryAdd(b.BlockSeq, decoded[j]))
                    throw new ArchiveException(ArchiveErrorCode.E_PAYLOAD, "block_seq повторюється.");
            }
        }

        var result = new List<ExtractedFile>(order.Count);
        foreach (string name in order)
        {
            var acc = accs[name];
            long total = 0;
            uint expectedSeq = 0;
            foreach (var kv in acc.Blocks)
            {
                if (kv.Key != expectedSeq)
                    throw new ArchiveException(ArchiveErrorCode.E_PAYLOAD,
                        "block_seq не утворює суцільний ряд від 0.");
                expectedSeq++;
                total += kv.Value.Length;
                if (total > int.MaxValue)
                    throw new ArchiveException(ArchiveErrorCode.E_PAYLOAD, "файл завеликий.");
            }
            if (total != acc.Size)
                throw new ArchiveException(ArchiveErrorCode.E_PAYLOAD, "сума блоків не дорівнює orig_size.");

            var data = new byte[total];
            int pos = 0;
            foreach (var kv in acc.Blocks)
            {
                kv.Value.CopyTo(data, pos);
                pos += kv.Value.Length;
            }
            if (Crc32.Compute(data) != acc.Crc)
                throw new ArchiveException(ArchiveErrorCode.E_CRC, "CRC32 склеєного файла не збігається.");
            result.Add(new ExtractedFile(name, data));
        }
        return result;
    }

    /// <summary>Усі «чужі» винятки декодера перетворюються на E_CODE, щоб не було необроблених винятків.</summary>
    private static byte[] DecodeBlock(IBlockCompressor compressor, byte[] coded, BlockEntry block)
    {
        if (block.RawLen > int.MaxValue)
            throw new ArchiveException(ArchiveErrorCode.E_PAYLOAD, "raw_len завеликий.");

        byte[] raw;
        try
        {
            raw = compressor.Decompress(coded, (int)block.RawLen, block.NoCompression);
        }
        catch (ArchiveException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new ArchiveException(ArchiveErrorCode.E_CODE, "помилка декодування блока.", ex);
        }

        if (raw.Length != block.RawLen)
            throw new ArchiveException(ArchiveErrorCode.E_PAYLOAD, "розмір розпакованого блока не дорівнює raw_len.");
        return raw;
    }
}
