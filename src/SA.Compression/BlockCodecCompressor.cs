using SA.Core;

namespace SA.Compression;

/// <summary>
/// Справжній компресор блоків (LZ77 + канонічний Гаффман) для <see cref="IBlockCompressor"/>.
/// Обгортає <see cref="BlockCodec"/>. Якщо стиснення не вигідне, повертає блок як NO_COMPRESSION.
/// </summary>
public sealed class BlockCodecCompressor : IBlockCompressor
{
    private readonly CodecOptions _options;

    public BlockCodecCompressor(CodecOptions? options = null) => _options = options ?? CodecOptions.Tuned;

    public SA.Core.CompressedBlock Compress(ReadOnlySpan<byte> raw)
    {
        var block = BlockCodec.Compress(raw, _options);
        return new SA.Core.CompressedBlock(block.Data, block.NoCompression);
    }

    public byte[] Decompress(ReadOnlySpan<byte> coded, int rawLength, bool noCompression)
        => BlockCodec.Decompress(coded, rawLength, noCompression);
}
