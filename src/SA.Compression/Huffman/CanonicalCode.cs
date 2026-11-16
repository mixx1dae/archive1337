//CanonicalCode.cs: за довжинами видає самі коди за алгоритмом 3.4 для кодування, а також структуру для швидкого декодування.
using SA.Compression.BitIO;
using SA.Core;

namespace SA.Compression.Huffman;

/// <summary>
/// Канонічні коди Гаффмана (розділ 3.4 специфікації).
/// Головна перевага канонічного підходу: знаючи лише довжини бітових кодів для кожного символу,
/// самі коди відновлюються детерміновано й однозначно без передачі структури дерева.
/// Клас виконує дві функції: кодування (запис через Write) та декодування (читання через ReadSymbol).
/// </summary>
public sealed class CanonicalCode
{
    // Масив довжин кодів: Lengths[символ] = довжина коду в бітах (0, якщо символ відсутній)
    public int[] Lengths { get; }

    // Масив згенерованих префіксних кодів: Codes[символ] = числовий бінарний код
    public uint[] Codes { get; }

    // Ознака того, що в таблиці немає жодного символу (порожній алфавіт)
    public bool IsEmpty { get; }

    // _countPerLength[L] — кількість символів, що мають бітову довжину коду рівно L біт (від 1 до 15)
    private readonly int[] _countPerLength = new int[CodecConstants.MaxCodeLength + 1];

    // Символи, відсортовані за пріоритетом: спочатку за зростанням довжини коду, а всередині довжини — за числовим значенням
    private readonly int[] _symbolsByCode;

    public CanonicalCode(int[] lengths)
    {
        Lengths = lengths;
        Codes = new uint[lengths.Length];

        // Підраховуємо, скільки разів зустрічається кожна бітова довжина (1..15)
        foreach (int l in lengths)
            if (l > 0) _countPerLength[l]++;

        // --- Кроки 1–2 алгоритму побудови канонічних кодів (розділ 3.4) ---
        // nextCode[L] обчислює початковий базовий числовий код для кожної довжини L біт
        var nextCode = new uint[CodecConstants.MaxCodeLength + 2];
        uint code = 0;
        for (int l = 1; l <= CodecConstants.MaxCodeLength; l++)
        {
            // Формула специфікації: додаємо кількість символів попередньої довжини
            // та робимо зсув уліво на 1 біт (домножуємо на 2)
            code = (code + (uint)_countPerLength[l - 1]) << 1;
            nextCode[l] = code;
        }

        // --- Крок 3: Призначення кодів усім символам ---
        // Символи обробляються строго за зростанням їхнього числового значення (0, 1, 2, ...)
        for (int s = 0; s < lengths.Length; s++)
        {
            if (lengths[s] > 0)
            {
                // Призначаємо символу поточний доступний числовий код для його довжини
                // та одразу інкрементуємо (++) для наступного символу такої самої довжини
                Codes[s] = nextCode[lengths[s]]++;
            }
        }

        // Формуємо таблицю швидкого пошуку символів для швидкого декодування потоку бітів:
        // збираємо символи групами від найкоротших довжин до найдовших
        var symbols = new List<int>();
        for (int l = 1; l <= CodecConstants.MaxCodeLength; l++)
            for (int s = 0; s < lengths.Length; s++)
                if (lengths[s] == l) 
                    symbols.Add(s);

        _symbolsByCode = symbols.ToArray();
        IsEmpty = symbols.Count == 0;
    }

    /// <summary>
    /// Записує бітове представлення вказаного символу у потік.
    /// </summary>
    public void Write(BitWriter writer, int symbol)
    {
        int length = Lengths[symbol];
        if (length == 0)
            throw new InvalidOperationException($"символ {symbol} відсутній у таблиці");

        // Пишемо рівно length бітів з Codes[symbol] у бітовий потік
        writer.Write(Codes[symbol], length);
    }

    /// <summary>
    /// Потокове декодування без явної структури бінарного дерева.
    /// Читає по одному біту за раз, поки поточний префікс не збіжиться із валідним кодом.
    /// Якщо досягнуто максимуму довжини без збігу — генерує помилку E_CODE.
    /// </summary>
    public int ReadSymbol(BitReader reader)
    {
        int code = 0;   // Поточне числове значення накопиченого префікса
        int first = 0;  // Перший (найменший) канонічний код поточної бітової довжини
        int index = 0;  // Зсув у масиві _symbolsByCode для початку списку символів поточної довжини

        for (int l = 1; l <= CodecConstants.MaxCodeLength; l++)
        {
            // Додаємо черговий прочитаний біт праворуч (MSB-first порядок)
            code |= reader.ReadBit();
            int count = _countPerLength[l];

            // Математична властивість канонічного коду: якщо зчитаний числовий код 
            // потрапляє в діапазон [first; first + count - 1], отже, символ знайдено!
            if (code - count < first)
            {
                // (code - first) визначає порядковий номер символу серед символів цієї ж довжини
                return _symbolsByCode[index + (code - first)];
            }

            // Переходимо до наступної глибини/довжини бітів (L + 1):
            index += count;                  // Пропускаємо count символів у таблиці розпізнавання
            first = (first + count) << 1;    // Базовий код для наступної довжини множиться на 2
            code <<= 1;                      // Зсуваємо накопичений код уліво для наступного біта
        }

        // Якщо за 15 біт код так і не розпізнано — потік даних пошкоджений
<<<<<<< HEAD
        throw new ArchiveException(ArchiveErrorCode.E_CODE, "код не відповідає жодному символу таблиці");
=======
        throw new ArcException(ErrorCodes.Code, "код не відповідає жодному символу таблиці");
>>>>>>> 0e5ee03d2c2be78bee0b57f623a074bcbc82ba4a
    }
}