using SA.Core;

namespace SA.Compression;

/// <summary>
/// Тимчасовий компресор: розпаковує будь-які блоки (Гаффман + LZ77 через BlockDecoder),
/// а стискає лише в режимі NO_COMPRESSION. Дозволяє розпаковувати еталонні архіви викладача
/// й архіви інших команд до появи справжнього кодера. Коли П1 закінчить кодер,
/// замініть цим кодером в SA.Cli.
/// </summary>
public sealed class DecodeOnlyCompressor : IBlockCompressor
{
    public CompressedBlock Compress(ReadOnlySpan<byte> raw)
        => new(raw.ToArray(), NoCompression: true);

    public byte[] Decompress(ReadOnlySpan<byte> coded, int rawLength, bool noCompression)
    {
        if (!noCompression)
            return BlockDecoder.Decode(coded, rawLength);

        if (coded.Length != rawLength)
            throw new ArchiveException(ArchiveErrorCode.E_PAYLOAD, "NO_COMPRESSION: coded_len має дорівнювати raw_len.");
        return coded.ToArray();
    }
}
