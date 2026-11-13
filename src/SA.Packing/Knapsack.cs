namespace SA.Packing;

/// <summary>Елемент рюкзака: вага (coded_len + 15) і цінність блока.</summary>
public readonly record struct KnapsackItem(int Weight, long Value);

/// <summary>Рюкзак 0/1 для пакування блоків у том (методичка, п. 3.4).</summary>
public static class Knapsack
{
    /// <summary>
    /// Точний розв'язок ДП, O(n·W) за часом. Пам'ять: масив цінностей на W+1 значень
    /// плюс n·(W+1) бітів для відновлення відповіді.
    /// Повертає індекси обраних елементів за зростанням. При рівній цінності
    /// елемент не береться, тому результат детермінований.
    /// </summary>
    public static int[] SolveDp(IReadOnlyList<KnapsackItem> items, int capacity)
    {
        Validate(items, capacity);
        int n = items.Count;
        if (n == 0 || capacity == 0) return Array.Empty<int>();

        int words = (capacity + 1 + 63) / 64; // слів по 64 біти на один елемент
        long totalWords = (long)n * words;
        if (totalWords > Array.MaxLength)
            throw new ArgumentException(
                "Задача завелика для ДП: зменште місткість тому або кількість блоків.");

        var take = new ulong[totalWords];
        var best = new long[capacity + 1];

        for (int i = 0; i < n; i++)
        {
            int w = items[i].Weight;
            long v = items[i].Value;
            if (w > capacity) continue;

            long rowBase = (long)i * words;
            for (int c = capacity; c >= w; c--)
            {
                long candidate = best[c - w] + v;
                if (candidate > best[c])
                {
                    best[c] = candidate;
                    take[rowBase + (c >> 6)] |= 1UL << (c & 63);
                }
            }
        }

        var chosen = new List<int>();
        int cap = capacity;
        for (int i = n - 1; i >= 0; i--)
        {
            ulong word = take[(long)i * words + (cap >> 6)];
            if (((word >> (cap & 63)) & 1UL) != 0)
            {
                chosen.Add(i);
                cap -= items[i].Weight;
            }
        }
        chosen.Reverse();
        return chosen.ToArray();
    }

    /// <summary>
    /// Жадібний за відношенням цінність/вага (для порівняння з ДП). Елемент, що не влазить,
    /// пропускається, перебір іде далі. Повертає індекси за зростанням.
    /// </summary>
    public static int[] SolveGreedy(IReadOnlyList<KnapsackItem> items, int capacity)
    {
        Validate(items, capacity);
        int n = items.Count;
        var order = new int[n];
        for (int i = 0; i < n; i++) order[i] = i;

        Array.Sort(order, (a, b) =>
        {
            var ia = items[a];
            var ib = items[b];
            // спадання v/w: порівнюємо перехресним множенням без ділення
            int cmp = ((Int128)ib.Value * ia.Weight).CompareTo((Int128)ia.Value * ib.Weight);
            return cmp != 0 ? cmp : a.CompareTo(b);
        });

        var chosen = new List<int>();
        long remaining = capacity;
        foreach (int idx in order)
        {
            if (items[idx].Weight <= remaining)
            {
                chosen.Add(idx);
                remaining -= items[idx].Weight;
            }
        }
        chosen.Sort();
        return chosen.ToArray();
    }

    public static long TotalValue(IReadOnlyList<KnapsackItem> items, IEnumerable<int> chosen)
    {
        long sum = 0;
        foreach (int i in chosen) sum += items[i].Value;
        return sum;
    }

    public static long TotalWeight(IReadOnlyList<KnapsackItem> items, IEnumerable<int> chosen)
    {
        long sum = 0;
        foreach (int i in chosen) sum += items[i].Weight;
        return sum;
    }

    private static void Validate(IReadOnlyList<KnapsackItem> items, int capacity)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        foreach (var it in items)
        {
            if (it.Weight <= 0) throw new ArgumentException("Вага елемента має бути додатною.", nameof(items));
            if (it.Value < 0) throw new ArgumentException("Цінність елемента не може бути від'ємною.", nameof(items));
        }
    }
}
