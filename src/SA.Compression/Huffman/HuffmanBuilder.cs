//за частотами символів повертає довжини кодів.
using SA.Core;

namespace SA.Compression.Huffman;

/// <summary>
/// Побудова оптимальних довжин кодів Гаффмана на основі частот появи символів.
/// Використовує класичний жадібний алгоритм на основі черги з пріоритетами (PriorityQueue).
/// Специфікація обмежує глибину коду 15 бітами. Якщо згенероване дерево глибше за 15,
/// застосовується згладжування частот (частоти діляться навпіл з округленням угору),
/// після чого дерево перебудовується наново.
/// </summary>
public static class HuffmanBuilder
{
    /// <summary>
    /// Вузол бінарного дерева Гаффмана.
    /// </summary>
    private sealed class Node
    {
        public readonly int Symbol;   // Значення символу (>= 0 для листка, -1 для внутрішнього вузла-предка)
        public readonly long Freq;    // Сумарна частота появи вузла та його піддерев
        public readonly Node? Left;   // Лівий нащадок (біт 0)
        public readonly Node? Right;  // Правий нащадок (біт 1)

        public Node(int symbol, long freq, Node? left, Node? right)
        {
            Symbol = symbol; 
            Freq = freq; 
            Left = left; 
            Right = right;
        }
    }

    /// <param name="frequencies">Масив частот: frequencies[s] показує, скільки разів зустрівся символ s.</param>
    /// <returns>Масив lengths: lengths[s] дорівнює довжині коду (0, якщо символ взагалі не використовувався).</returns>
    public static int[] BuildLengths(long[] frequencies)
    {
        var lengths = new int[frequencies.Length];
        var used = new List<int>();

        // Знаходимо всі унікальні символи, які з'явилися в тексті хоча б один раз
        for (int s = 0; s < frequencies.Length; s++)
            if (frequencies[s] > 0) used.Add(s);

        // Якщо вхідний потік повністю порожній — повертаємо нульові довжини
        if (used.Count == 0) return lengths;

        // Вироджений випадок: якщо символ всього один, призначаємо йому 1-бітний код "0"
        if (used.Count == 1)
        {
            lengths[used[0]] = 1;
            return lengths;
        }

        // Клонуємо частоти, щоб при необхідності коригувати їх без модифікації оригінального масиву
        var freq = (long[])frequencies.Clone();

        while (true)
        {
            // Будуємо дерево й отримуємо максимальну глибину (найдовший код)
            int maxDepth = AssignDepths(freq, used, lengths);

            // Якщо код вклався в ліміт 15 біт — результат знайдено
            if (maxDepth <= CodecConstants.MaxCodeLength) 
                return lengths;

            // Якщо дерево виявилося занадто глибоким (> 15 біт):
            // Згладжуємо піки частот, ділячи їх навпіл з округленням угору (ceil).
            // Завдяки формулі (f + 1) / 2 ненульова частота ніколи не стане 0, тобто символ не зникне.
            foreach (int s in used) 
                freq[s] = (freq[s] + 1) / 2;
        }
    }

    /// <summary>
    /// Створює дерево за алгоритмом Гаффмана через PriorityQueue й записує глибину кожного листка в lengths.
    /// </summary>
    private static int AssignDepths(long[] freq, List<int> used, int[] lengths)
    {
        // Черга з пріоритетом. Кортеж (freq, order) забезпечує стабільність сортування вузлів із однаковою частотою
        var queue = new PriorityQueue<Node, (long, int)>();
        int order = 0;

        // Створюємо листовий вузол для кожного використовуваного символу та кладемо в чергу
        foreach (int s in used)
            queue.Enqueue(new Node(s, freq[s], null, null), (freq[s], order++));

        // Цикл злиття: об'єднуємо два вузли з найменшими частотами в один спільний батьківський вузол
        while (queue.Count > 1)
        {
            var a = queue.Dequeue();
            var b = queue.Dequeue();

            // Батьківський вузол має Symbol = -1 та вагу, що дорівнює сумі частот дітей
            var parent = new Node(-1, a.Freq + b.Freq, a, b);
            queue.Enqueue(parent, (parent.Freq, order++));
        }

        int max = 0;
        // Обхід побудованого дерева в глибину (DFS) через стек для підрахунку довжин шляхів (глибини)
        var stack = new Stack<(Node node, int depth)>();
        stack.Push((queue.Dequeue(), 0)); // Корінь дерева має глибину 0

        while (stack.Count > 0)
        {
            var (node, depth) = stack.Pop();

            // Якщо натрапили на листок (node.Left is null) — глибина і є шуканою довжиною бітового коду
            if (node.Left is null)
            {
                lengths[node.Symbol] = depth;
                if (depth > max) max = depth;
            }
            else
            {
                // Для проміжних вузлів спускаємося ліворуч та праворуч, збільшуючи глибину на 1
                stack.Push((node.Left, depth + 1));
                stack.Push((node.Right!, depth + 1));
            }
        }

        return max;
    }
}