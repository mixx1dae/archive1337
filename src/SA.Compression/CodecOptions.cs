namespace SA.Compression;

/// <summary>
/// Параметри налаштування кодека стиснення.
/// </summary>
public sealed record CodecOptions
{
    // За замовчуванням у базовому рівні обрано стратегію Lazy (лінива жадібність)
    public ParseStrategy Parse { get; init; } = ParseStrategy.Lazy;

    /// <summary>
    /// Глибина пошуку збігів: скільки попередніх входжень у хеш-ланцюжку ми перевіряємо
    /// </summary>
    public int MaxChain { get; init; } = 64;

    /// <summary>
    /// Мінімальна довжина збігу (за стандартом 3..258)
    /// </summary>
    public int MinMatch { get; init; } = CodecConstants.MinMatch;

    /// <summary>
    /// Запас для лінивого розбору: на скільки наступний повтор має бути довшим, щоб відкласти поточний
    /// </summary>
    public int LazyMargin { get; init; } = 1;

    /// <summary>
    /// Примусове збереження без стиснення (прапорець NO_COMPRESSION)
    /// </summary>
    public bool ForceStore { get; init; }

    /// <summary>
    /// Валідація переданих числових значень параметрів
    /// </summary>
    public void Validate()
    {
        if (MaxChain < 1) 
            throw new ArgumentOutOfRangeException(nameof(MaxChain));

        if (MinMatch < CodecConstants.MinMatch || MinMatch > CodecConstants.MaxMatch)
            throw new ArgumentOutOfRangeException(nameof(MinMatch));

        if (LazyMargin < 1) 
            throw new ArgumentOutOfRangeException(nameof(LazyMargin));
    }
}