namespace SA.Compression.Lz77;

/// <summary>
/// Окремий кандидат на LZ77-збіг для певної позиції в тексті.
/// Зберігає найвигіднішу відстань для свого класу відстаней і максимальну довжину збігу.
/// </summary>
public readonly record struct MatchCandidate(int Distance, int MaxLength)
{
    /// <summary>
    /// Символ коду відстані (0..15) для алфавіту DIST відповідно до розділу 3.2 специфікації.
    /// </summary>
    public int Code => DistanceCoding.Code(Distance);
}

/// <summary>
/// Компактне сховище всіх знайдених збігів для кожної позиції вхідного блоку даних.
/// Замість створення окремого масиву або списку List для кожного байта (що навантажує GC),
/// використовується плоский єдиний масив _items та масив зміщень _offsets (формат CSR).
/// </summary>
public sealed class MatchTable
{
    // Масив початкових індексів: кандидати для позиції i починаються в _items[_offsets[i]]
    // і закінчуються в _items[_offsets[i + 1] - 1].
    private readonly int[] _offsets;

    // Єдиний нерозривний масив усіх знайдених кандидатів у всьому блоці даних.
    private readonly MatchCandidate[] _items;

    public MatchTable(int[] offsets, MatchCandidate[] items)
    {
        _offsets = offsets;
        _items = items;
    }

    /// <summary>
    /// Загальна кількість позицій байтів у вхідному блоці.
    /// </summary>
    public int PositionCount => _offsets.Length - 1;

    /// <summary>
    /// Повертає список доступних кандидатів на збіг у конкретній позиції без виділення нової пам'яті.
    /// </summary>
    public ReadOnlySpan<MatchCandidate> At(int position) =>
        _items.AsSpan(_offsets[position], _offsets[position + 1] - _offsets[position]);
}