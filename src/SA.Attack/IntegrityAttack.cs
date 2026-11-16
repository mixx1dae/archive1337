using SA.Core;
using SA.Format;

namespace SA.Attack;

/// <summary>Підсумок базової перевірки цілісності (методичка, розділ 3.7, третій пункт).</summary>
public sealed record IntegrityAttackResult(
    int Mutations,
    int Rejected,
    int Accepted,
    int Unhandled,
    IReadOnlyDictionary<ArchiveErrorCode, int> RejectedByCode,
    IReadOnlyList<string> AcceptedMutations)
{
    /// <summary>Система витримала атаку: нічого не прийнято й жодного необробленого винятку.</summary>
    public bool Survived => Accepted == 0 && Unhandled == 0;
}

/// <summary>
/// Базова атака на цілісність: змінює один або кілька байтів тому (а також обрізає і подовжує його)
/// і перевіряє реакцію розархіватора. Коректна система відхиляє кожну зміну кодом <see cref="ArchiveException"/>.
/// Прийнята зміна означає успішну атаку (помилка захисту), а необроблений виняток означає порушення C2/C5.
/// Кожна спроба виконує PBKDF2, тому кількість змін обмежена: усі байти заголовка і тега перевіряються
/// завжди, а з шифротексту береться вибірка.
/// </summary>
public static class IntegrityAttack
{
    public static IntegrityAttackResult Run(
        IReadOnlyList<byte[]> volumes,
        string password,
        IBlockCompressor compressor,
        int volumeIndex = 0,
        int ciphertextSamples = 300,
        int multiByteMutations = 20,
        int seed = 2001)
    {
        if (volumeIndex < 0 || volumeIndex >= volumes.Count)
            throw new ArgumentOutOfRangeException(nameof(volumeIndex));

        byte[] original = volumes[volumeIndex];
        var rng = new Random(seed);
        var positions = ChoosePositions(original.Length, ciphertextSamples, rng);

        int total = 0, rejected = 0, accepted = 0, unhandled = 0;
        var byCode = new Dictionary<ArchiveErrorCode, int>();
        var acceptedList = new List<string>();

        foreach (var (description, data) in Mutations(original, positions, multiByteMutations, rng))
        {
            total++;
            var attacked = volumes.ToList();
            attacked[volumeIndex] = data;

            try
            {
                ArchiveReader.ExtractAll(attacked, password, compressor);
                accepted++;
                acceptedList.Add(description);
            }
            catch (ArchiveException ex)
            {
                rejected++;
                byCode[ex.Code] = byCode.GetValueOrDefault(ex.Code) + 1;
            }
            catch (Exception)
            {
                unhandled++;
            }
        }

        return new IntegrityAttackResult(total, rejected, accepted, unhandled, byCode, acceptedList);
    }

    private static SortedSet<int> ChoosePositions(int length, int ciphertextSamples, Random rng)
    {
        var positions = new SortedSet<int>();

        for (int i = 0; i < Math.Min(VolumeHeader.Size, length); i++) positions.Add(i);
        for (int i = Math.Max(0, length - VolumeHeader.TagSize); i < length; i++) positions.Add(i);

        int start = VolumeHeader.Size;
        int end = length - VolumeHeader.TagSize; // шифротекст: [start, end)
        if (end > start)
        {
            int span = end - start;
            if (span <= ciphertextSamples)
            {
                for (int i = start; i < end; i++) positions.Add(i);
            }
            else
            {
                int half = Math.Max(1, ciphertextSamples / 2);
                for (int k = 0; k < half; k++) positions.Add(start + (int)((long)k * span / half));
                for (int k = 0; k < ciphertextSamples - half; k++) positions.Add(start + rng.Next(span));
            }
        }
        return positions;
    }

    private static IEnumerable<(string Description, byte[] Data)> Mutations(
        byte[] original, SortedSet<int> positions, int multiByteMutations, Random rng)
    {
        // один байт: змінюється один біт, номер якого залежить від позиції
        foreach (int pos in positions)
        {
            var copy = (byte[])original.Clone();
            byte mask = (byte)(1 << (pos % 8));
            copy[pos] ^= mask;
            yield return ($"байт {pos} ^ 0x{mask:X2}", copy);
        }

        // кілька байтів одразу
        for (int m = 0; m < multiByteMutations && original.Length > 0; m++)
        {
            var copy = (byte[])original.Clone();
            var used = new HashSet<int>();
            int count = rng.Next(2, 5);
            for (int j = 0; j < count; j++)
            {
                int p = rng.Next(original.Length);
                if (used.Add(p)) copy[p] ^= (byte)rng.Next(1, 256);
            }
            yield return ($"{used.Count} байтів: {string.Join(",", used.OrderBy(x => x))}", copy);
        }

        // довжина
        if (original.Length > 0)
            yield return ("обрізано останній байт", original[..^1]);
        yield return ("обрізано до половини", original[..(original.Length / 2)]);
        yield return ("додано байт 0x00", original.Append((byte)0).ToArray());
    }
}
