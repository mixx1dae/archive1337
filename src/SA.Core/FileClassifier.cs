namespace SA.Core;

/// <summary>
/// Класифікатор типу файла (file_type 0…6). Реальний перцептрон (SA.Analysis, зона П3)
/// має реалізувати цей інтерфейс.
/// </summary>
public interface IFileClassifier
{
    byte Classify(ReadOnlySpan<byte> data);
}

/// <summary>Заглушка: завжди повертає один тип (до появи перцептрона).</summary>
public sealed class ConstantClassifier : IFileClassifier
{
    private readonly byte _type;

    public ConstantClassifier(byte type = 0)
    {
        if (type > 6) throw new ArgumentOutOfRangeException(nameof(type));
        _type = type;
    }

    public byte Classify(ReadOnlySpan<byte> data) => _type;
}
