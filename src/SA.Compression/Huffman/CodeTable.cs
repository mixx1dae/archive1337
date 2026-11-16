using SA.Core;

namespace SA.Compression.Huffman;

/// <summary>
/// Серіалізація та десеріалізація таблиць кодів Гаффмана (розділ 3.3 специфікації).
/// Формат збереження у бінарному вигляді:
/// [nsym (2 байти uint16 BE)] + nsym × [symbol (2 байти uint16 BE), length (1 байт uint8)].
/// </summary>
public static class CodeTable
{
    /// <summary>
    /// Серіалізує масив довжин кодів у бінарний потік output.
    /// </summary>
    public static void Write(List<byte> output, int[] lengths)
    {
        // 1. Рахуємо кількість дійсно використовуваних символів (тих, у кого довжина > 0)
        int nsym = 0;
        foreach (int l in lengths) 
            if (l > 0) nsym++;

        // 2. Записуємо nsym як 16-бітне ціле (2 байти, порядок Big-Endian: старший байт першим)
        output.Add((byte)(nsym >> 8));
        output.Add((byte)nsym);

        // 3. Записуємо кожен активний символ: 2 байти на код символу + 1 байт на довжину коду
        for (int s = 0; s < lengths.Length; s++)
        {
            if (lengths[s] == 0) continue; // Символи з довжиною 0 у потік не пишуться

            output.Add((byte)(s >> 8));    // Старший байт номера символу
            output.Add((byte)s);           // Молодший байт номера символу
            output.Add((byte)lengths[s]);  // Довжина коду Гаффмана (1..15)
        }
    }

    /// <summary>
    /// Зчитує бінарні дані та виконує сувору валідацію таблиці відповідно до специфікації.
    /// Перевірки валідності (захист від сфальсифікованих чи пошкоджених архівів):
    /// - Достатність байтів у буфері.
    /// - Символи йдуть суворо за зростанням (без дублікатів і невідсортованості).
    /// - Номер символу не виходить за допустимий алфавіт (<= maxSymbol).
    /// - Довжина коду лежить у межах 1..15.
    /// - Для nsym == 1 довжина коду має бути строго 1.
    /// - Для nsym > 1 має точно виконуватися нерівність/рівність Крафта-Макміллана.
    /// </summary>
    public static int[] Read(ReadOnlySpan<byte> data, ref int pos, int maxSymbol, string name)
    {
        // Перевіряємо, чи є хоча б 2 байти для зчитування поля nsym
        if (pos + 2 > data.Length)
<<<<<<< HEAD
            throw new ArchiveException(ArchiveErrorCode.E_PAYLOAD, $"таблиця {name} обірвана");
=======
            throw new ArcException(ErrorCodes.Payload, $"таблиця {name} обірвана");
>>>>>>> 0e5ee03d2c2be78bee0b57f623a074bcbc82ba4a

        // Зчитуємо 16-бітне число nsym (Big-Endian)
        int nsym = (data[pos] << 8) | data[pos + 1];
        pos += 2;

        // Перевіряємо, чи в масиві вистачає байтів для записів усіх nsym елементів (по 3 байти на запис)
        if (pos + 3L * nsym > data.Length)
<<<<<<< HEAD
            throw new ArchiveException(ArchiveErrorCode.E_PAYLOAD, $"таблиця {name} обірвана");
=======
            throw new ArcException(ErrorCodes.Payload, $"таблиця {name} обірвана");
>>>>>>> 0e5ee03d2c2be78bee0b57f623a074bcbc82ba4a

        var lengths = new int[maxSymbol + 1];
        int last = -1;       // Попередній символ для перевірки строгого зростання
        long kraft = 0;      // Накопичувач для перевірки повноти дерева (рівність Крафта)
        int single = 0;      // Довжина коду у випадку, коли передано лише один символ

        for (int i = 0; i < nsym; i++)
        {
            // Зчитуємо 2 байти символу та 1 байт довжини
            int symbol = (data[pos] << 8) | data[pos + 1];
            int length = data[pos + 2];
            pos += 3;

            // Символи зобов'язані бути відсортованими за зростанням: symbol > last
            if (symbol <= last)
<<<<<<< HEAD
                throw new ArchiveException(ArchiveErrorCode.E_CODE, $"{name}: символи не за зростанням");

            // Символ не може перевищувати встановлену верхню межу розміру алфавіту
            if (symbol > maxSymbol)
                throw new ArchiveException(ArchiveErrorCode.E_CODE, $"{name}: символ {symbol} поза алфавітом");

            // Довжина префіксного коду може бути лише в межах від 1 до 15 біт
            if (length < 1 || length > CodecConstants.MaxCodeLength)
                throw new ArchiveException(ArchiveErrorCode.E_CODE, $"{name}: довжина коду {length}");
=======
                throw new ArcException(ErrorCodes.Code, $"{name}: символи не за зростанням");

            // Символ не може перевищувати встановлену верхню межу розміру алфавіту
            if (symbol > maxSymbol)
                throw new ArcException(ErrorCodes.Code, $"{name}: символ {symbol} поза алфавітом");

            // Довжина префіксного коду може бути лише в межах від 1 до 15 біт
            if (length < 1 || length > CodecConstants.MaxCodeLength)
                throw new ArcException(ErrorCodes.Code, $"{name}: довжина коду {length}");
>>>>>>> 0e5ee03d2c2be78bee0b57f623a074bcbc82ba4a

            lengths[symbol] = length;
            last = symbol;

            // Перевірка Крафта: кожен символ довжини L займає частку 2^(MaxCodeLength - L) простору кодів
            kraft += 1L << (CodecConstants.MaxCodeLength - length);
            single = length;
        }

        // Вироджений випадок (тільки 1 символ в алфавіті): повинен мати довжину рівно 1 біт
        if (nsym == 1 && single != 1)
<<<<<<< HEAD
            throw new ArchiveException(ArchiveErrorCode.E_CODE, $"{name}: вироджений код має мати довжину 1");

        // Для повноцінного префіксного дерева сума часток кодів повинна покривати рівно 2^MaxCodeLength (1 << 15)
        if (nsym > 1 && kraft != 1L << CodecConstants.MaxCodeLength)
            throw new ArchiveException(ArchiveErrorCode.E_CODE, $"{name}: порушена рівність Крафта");
=======
            throw new ArcException(ErrorCodes.Code, $"{name}: вироджений код має мати довжину 1");

        // Для повноцінного префіксного дерева сума часток кодів повинна покривати рівно 2^MaxCodeLength (1 << 15)
        if (nsym > 1 && kraft != 1L << CodecConstants.MaxCodeLength)
            throw new ArcException(ErrorCodes.Code, $"{name}: порушена рівність Крафта");
>>>>>>> 0e5ee03d2c2be78bee0b57f623a074bcbc82ba4a

        return lengths;
    }
}