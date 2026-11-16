using System.Numerics;

namespace SA.Compression;

/// <summary>
/// Кодування та декодування відстаней LZ77 (розділ 3.2 специфікації).
/// Відстань dist (1..32768) розкладається на символ code (0..15) і незакодовані біти extra.
/// </summary>
public static class DistanceCoding
{
    /// <summary>
    /// Повертає кількість бітів, необхідних для двійкового запису числа x.
    /// </summary>
    public static int BitLength(int x) => x == 0 ? 0 : 32 - BitOperations.LeadingZeroCount((uint)x);

    /// <summary>
    /// Перетворює зміщення distance (1..32768) на символ префіксного дерева DIST (0..15).
    /// Береться бітова довжина величини (distance - 1).
    /// </summary>
    public static int Code(int distance) => BitLength(distance - 1);

    /// <summary>
    /// Кількість додаткових бітів, які записуються одразу після коду в бітовий потік.
    /// Для кодів 0 і 1 це 0 бітів, для решти — (code - 1).
    /// </summary>
    public static int ExtraBits(int code) => code > 1 ? code - 1 : 0;

    /// <summary>
    /// Виділяє значення додаткових бітів із числа (distance - 1) за допомогою бітової маски.
    /// </summary>
    public static uint ExtraValue(int distance, int extraBits) =>
        extraBits == 0 ? 0u : (uint)(distance - 1) & ((1u << extraBits) - 1u);

    /// <summary>
    /// Відновлює точну початкову відстань distance за символом code та значенням extra-бітів
    /// </summary>
    public static int Decode(int code, uint extra) =>
        code == 0 ? 1 :
        code == 1 ? 2 :
        (int)((1u << (code - 1)) | extra) + 1;
}