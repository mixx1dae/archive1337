namespace SA.Compression.Lz77;

/// <summary>
/// Лінивий парсер (Lazy Evaluation): покращена евристика.
/// Якщо в поточній позиції i є збіг довжини L, але в сусідній позиції (i + 1)
/// існує збіг, довший як мінімум на LazyMargin байтів,
/// вигідніше пожертвувати поточним байтом (випустити літерал), щоб взяти довший збіг.
/// </summary>
public sealed class LazyParser : IParser
{
    public List<Token> Parse(ReadOnlySpan<byte> data, MatchTable matches, CodecOptions options)
    {
        var tokens = new List<Token>();
        int i = 0, n = data.Length;

        while (i < n)
        {
            var candidates = matches.At(i);

            // Якщо в позиції i повторів немає — пишемо літерал і йдемо далі
            if (candidates.Length == 0)
            {
                tokens.Add(Token.Literal(data[i]));
                i++;
                continue;
            }

            // Найкращий (найдовший) збіг у поточній позиції
            var best = candidates[^1];

            // Заглядаємо на один крок уперед: чи є кращий варіант у позиції (i + 1)?
            if (i + 1 < n)
            {
                var next = matches.At(i + 1);

                // Якщо в наступній позиції є збіг, і він помітно довший (на LazyMargin або більше)
                if (next.Length > 0 && next[^1].MaxLength >= best.MaxLength + options.LazyMargin)
                {
                    // Відкладаємо збіг: у позиції i записуємо один літерал
                    tokens.Add(Token.Literal(data[i]));
                    i++;
                    continue; // На наступній ітерації цикл почнеться з i + 1 і візьме довший збіг
                }
            }

            // Якщо наступний збіг не кращий — застосовуємо поточний збіг
            tokens.Add(Token.Match(best.Distance, best.MaxLength));
            i += best.MaxLength;
        }

        return tokens;
    }
}