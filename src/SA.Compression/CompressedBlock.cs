namespace SA.Compression;

/// <summary>
/// Фінальний результат стиснення блоку, призначений для запису пакувальником у том
/// </summary>
/// <param name="Data">Стиснені дані (або оригінальні байти, якщо стиснення вимкнено)</param>
/// <param name="RawLength">Оригінальний розмір сирих даних до стиснення</param>
/// <param name="NoCompression">True, якщо блок записано без компресії</param>
public sealed record CompressedBlock(byte[] Data, int RawLength, bool NoCompression)
{
    // Фактична довжина закодованого блоку в байтах
    public int CodedLength => Data.Length;
}