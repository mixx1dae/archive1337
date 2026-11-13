namespace SA.Core;

/// <summary>Результат стиснення одного блоку.</summary>
public readonly record struct CompressedBlock(byte[] Data, bool NoCompression);

/// <summary>Спільний інтерфейс компресора блоку (SA.Core).</summary>
public interface IBlockCompressor
{
    CompressedBlock Compress(ReadOnlySpan<byte> raw);
    byte[] Decompress(ReadOnlySpan<byte> coded, int rawLength, bool noCompression);
}