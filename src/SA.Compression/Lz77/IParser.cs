namespace SA.Compression.Lz77;

/// <summary>
/// Загальний інтерфейс стратегії синтаксичного розбору (парсингу).
/// Приймає сирі байти та готову таблицю знайдених збігів,
/// а на виході повертає послідовність токенів (літералів або збігів LZ77).
/// </summary>
public interface IParser
{
    /// <summary>
    /// Розбиває вхідний масив байтів на токени відповідно до вибраної евристики розбору.
    /// </summary>
    /// <param name="data">Вхідні нестиснені сирі байти поточного блоку.</param>
    /// <param name="matches">Попередньо порахована таблиця кандидатів на збіги для кожної позиції.</param>
    /// <param name="options">Налаштування кодека (наприклад, поріг LazyMargin або глибина пошуку).</param>
    List<Token> Parse(ReadOnlySpan<byte> data, MatchTable matches, CodecOptions options);
}