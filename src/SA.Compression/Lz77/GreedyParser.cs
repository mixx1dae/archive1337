namespace SA.Compression.Lz77;

/// <summary>
/// Жадібний парсер (Greedy): найшвидший і найпростіший метод розбору LZ77.
/// У кожній позиції обирає найдовший доступний збіг без заглядання вперед.
/// </summary>
public sealed class GreedyParser : IParser
{
    public List<Token> Parse(ReadOnlySpan<byte> data, MatchTable matches, CodecOptions options)
    {
        var tokens = new List<Token>();
        int i = 0, n = data.Length;

        while (i < n)
        {
            var candidates = matches.At(i);

            // Якщо збігів немає взагалі — видаємо звичайний байтовий літерал і зсуваємося на 1 байт
            if (candidates.Length == 0)
            {
                tokens.Add(Token.Literal(data[i]));
                i++;
            }
            else
            {
                // Завдяки фільтрації в MatchFinder останній кандидат завжди має найбільшу довжину MaxLength
                var best = candidates[^1];

                // Створюємо LZ77-посилання і «перестрибуємо» через усю закодовану довжину
                tokens.Add(Token.Match(best.Distance, best.MaxLength));
                i += best.MaxLength;
            }
        }

        return tokens;
    }
}