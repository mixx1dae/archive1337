namespace SA.Compression.Lz77;

/// <summary>
/// Елементарна неподільна одиниця розбору LZ77.
/// Представляє один із двох станів:
/// 1. Звичайний сирий байт (літерал).
/// 2. Посилання на попередній повтор (відстань distance, довжина length).
/// Для економії пам'яті структура запакована у два поля int.
/// </summary>
public readonly struct Token
{
    // Відстань зміщення назад (1..32768). Якщо _distance == 0 — це літерал.
    private readonly int _distance;

    // Подвійне призначення: якщо літерал — це числовий код байта (0..255);
    // якщо посилання на повтор — це довжина збігу (3..258).
    private readonly int _value;

    private Token(int distance, int value)
    {
        _distance = distance;
        _value = value;
    }

    /// <summary>
    /// Фабричний метод для створення звичайного байтового літерала.
    /// </summary>
    public static Token Literal(byte b) => new(0, b);

    /// <summary>
    /// Фабричний метод для створення посилання на повтор у ковзному вікні.
    /// </summary>
    public static Token Match(int distance, int length) => new(distance, length);

    /// <summary>
    /// Чи є токен звичайним літералом (true) чи LZ77-посиланням (false).
    /// </summary>
    public bool IsLiteral => _distance == 0;

    /// <summary>
    /// Значення байта літерала (викликати лише коли IsLiteral == true).
    /// </summary>
    public byte Byte => (byte)_value;

    /// <summary>
    /// Відстань назад у вікні (на скільки байтів відступити від поточної позиції).
    /// </summary>
    public int Distance => _distance;

    /// <summary>
    /// Скільки байтів скопіювати з попередньої позиції (довжина збігу).
    /// </summary>
    public int Length => _value;

    /// <summary>
    /// Скільки байтів вихідного розпакованого тексту замінює цей один токен.
    /// Літерал покриває рівно 1 байт, а посилання — length байтів.
    /// </summary>
    public int CoveredBytes => IsLiteral ? 1 : _value;
}