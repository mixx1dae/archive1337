using SA.Core;

namespace SA.Compression;

/// <summary>Заглушка: режим NO_COMPRESSION (coded_len = raw_len).</summary>
public sealed class PassThroughCompressor : IBlockCompressor
{
    public CompressedBlock Compress(ReadOnlySpan<byte> raw)
        => new(raw.ToArray(), NoCompression: true);

    public byte[] Decompress(ReadOnlySpan<byte> coded, int rawLength, bool noCompression)
    {
        if (!noCompression)
            throw new InvalidDataException("PassThroughCompressor підтримує лише NO_COMPRESSION.");
        if (coded.Length != rawLength)
            throw new InvalidDataException("coded_len має дорівнювати raw_len.");
        return coded.ToArray();
    }
}