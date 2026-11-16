namespace SA.Compression.Lz77;

/// <summary>
/// Будівник таблиці збігів за допомогою хеш-ланцюжків.
/// Для кожної позиції i шукає попередні позиції j у вікні (до 32 КБ),
/// які починаються з тих самих 3 байтів, і вимірює спільний префікс.
/// 
/// Оптимізація для точного ДП:
/// Для кожного з 16 класів відстаней залишається лише один найкращий збіг
/// (найдовший, а серед однакових за довжиною — найближчий, бо він дешевший).
/// </summary>
public static class MatchFinder
{
    // Розмір хеш-таблиці в бітах: 2^16 = 65536 кошиків для 3-байтових префіксів
    private const int HashBits = 16;

    /// <summary>
    /// Створює оптимізовану таблицю збігів для всього масиву data.
    /// </summary>
    public static MatchTable Build(ReadOnlySpan<byte> data, int maxChain, int minMatch)
    {
        int n = data.Length;
        var offsets = new int[n + 1];
        var items = new List<MatchCandidate>();

        // head[h] зберігає позицію останнього зустрінутого триграму з хешем h.
        // Значення -1 означає, що такий хеш ще не зустрічався.
        var head = new int[1 << HashBits];
        Array.Fill(head, -1);

        // prev[i] утворює зв'язний список (ланцюжок): вказує на попереднє входження такого самого хешу
        var prev = new int[n];

        // Тимчасові буфери на стеку для збереження найкращого збігу для кожного з 16 класів відстаней
        Span<int> bestLen = stackalloc int[CodecConstants.DistAlphabetSize];
        Span<int> bestDist = stackalloc int[CodecConstants.DistAlphabetSize];

        for (int i = 0; i < n; i++)
        {
            // Запам'ятовуємо, де в items починаються кандидати для поточної позиції i
            offsets[i] = items.Count;

            // Якщо до кінця блоку лишилося менше ніж minMatch (3 байти), збіг знайти неможливо
            if (i + CodecConstants.MinMatch > n) 
            { 
                prev[i] = -1; 
                continue; 
            }

            // Обчислюємо хеш від 3 послідовних байтів
            int h = Hash(data[i], data[i + 1], data[i + 2]);

            // Максимальна довжина повтору обмежена MaxMatch (258) або залишком блоку
            int cap = Math.Min(CodecConstants.MaxMatch, n - i);
            var current = data.Slice(i, cap);

            // Очищаємо результати попередньої позиції
            bestLen.Clear();

            int j = head[h]; // Беремо найновішу позицію з таким самим хешем
            int steps = 0;

            // Проходимо по ланцюжку попередників у межах ковзного вікна 32 КБ та ліміту кроків maxChain
            while (j >= 0 && i - j <= CodecConstants.WindowSize && steps < maxChain)
            {
                // Рахуємо, скільки однакових байтів поспіль збігаються в позиціях i та j
                int len = current.CommonPrefixLength(data.Slice(j, cap));

                if (len >= minMatch)
                {
                    int dist = i - j;
                    int code = DistanceCoding.Code(dist); // Визначаємо клас вартості відстані (0..15)

                    // Якщо знайдений повтор довший за попередній кандидат цього ж класу вартості
                    if (len > bestLen[code]) 
                    { 
                        bestLen[code] = len; 
                        bestDist[code] = dist; 
                    }

                    // Якщо досягли максимальної можливої довжини — шукати далі немає сенсу
                    if (len == cap) break;
                }

                j = prev[j]; // Переходимо до ще старішого входження
                steps++;
            }

            // Оновлюємо зв'язний список: тепер поточна позиція i стає найновішою для хешу h
            prev[i] = head[h];
            head[h] = i;

            // Фільтрація Парето: зберігаємо кандидатів за зростанням вартості та довжини.
            // Більш далекий клас відстані (дорожчий) береться лише тоді, коли його довжина строго більша.
            int longestSoFar = 0;
            for (int code = 0; code < CodecConstants.DistAlphabetSize; code++)
            {
                if (bestLen[code] > longestSoFar)
                {
                    items.Add(new MatchCandidate(bestDist[code], bestLen[code]));
                    longestSoFar = bestLen[code];
                }
            }
        }

        // Фіксуємо кінцеве зміщення
        offsets[n] = items.Count;
        return new MatchTable(offsets, items.ToArray());
    }

    /// <summary>
    /// Мультиплікативний хеш для трьох послідовних байтів (коефіцієнт золотого перетину).
    /// Повертає індекс кошика від 0 до 65535.
    /// </summary>
    private static int Hash(byte a, byte b, byte c) =>
        (int)((((uint)a << 16 | (uint)b << 8 | c) * 2654435761u) >> (32 - HashBits));
}