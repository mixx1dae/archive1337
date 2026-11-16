using System.Buffers.Binary;
using SA.Compression.BitIO;
using SA.Compression.Huffman;
using SA.Core;

namespace SA.Compression;

/// <summary>
/// Декодер стисненого блока (FORMAT_SPEC_P2.md, розділ 3.1):
/// таблиці LIT, LEN, DIST, token_count (uint32), stream_len (uint32), потік токенів.
/// Токен: літерал (символ LIT &lt; 256) або посилання LZ77 (символ 256, потім довжина − 3 за таблицею LEN,
/// код відстані за таблицею DIST і extra-біти). Усі порушення дають ArchiveException:
/// E_PAYLOAD для неузгоджених довжин, E_CODE для зіпсованих кодів, посилань і доповнення.
/// Написано на примітивах П1 (CodeTable, CanonicalCode, BitReader, DistanceCoder).
/// </summary>
public static class BlockDecoder
{
    public static byte[] Decode(ReadOnlySpan<byte> coded, int rawLength)
    {
        if (rawLength < 0) throw new ArgumentOutOfRangeException(nameof(rawLength));

        int pos = 0;
        int[] litLengths = CodeTable.Read(coded, ref pos, CodecConstants.MatchMarker, "LIT");
        int[] lenLengths = CodeTable.Read(coded, ref pos, CodecConstants.LenAlphabetSize - 1, "LEN");
        int[] distLengths = CodeTable.Read(coded, ref pos, DistanceCoder.MaxCode, "DIST");

        if (coded.Length - pos < 8)
            throw Payload("відсутні token_count і stream_len");

        uint tokenCount = BinaryPrimitives.ReadUInt32BigEndian(coded.Slice(pos, 4));
        uint streamLength = BinaryPrimitives.ReadUInt32BigEndian(coded.Slice(pos + 4, 4));
        pos += 8;

        if ((long)(coded.Length - pos) != (long)streamLength)
            throw Payload("stream_len не відповідає залишку блока");

        var lit = new CanonicalCode(litLengths);
        var len = new CanonicalCode(lenLengths);
        var dist = new CanonicalCode(distLengths);
        var reader = new BitReader(coded.Slice(pos).ToArray());

        var output = new byte[rawLength];
        int o = 0;

        for (uint t = 0; t < tokenCount; t++)
        {
            int symbol = lit.ReadSymbol(reader);
            if (symbol < CodecConstants.MatchMarker)
            {
                if (o >= rawLength) throw Payload("потік дає більше байтів, ніж raw_len");
                output[o++] = (byte)symbol;
                continue;
            }

            int matchLength = len.ReadSymbol(reader) + CodecConstants.MinMatch;
            int distCode = dist.ReadSymbol(reader);
            int extraCount = DistanceCoder.ExtraBitCount(distCode);
            uint extra = extraCount > 0 ? reader.ReadBits(extraCount) : 0u;
            int distance = DistanceCoder.Decode(distCode, extra);

            if (distance > o)
                throw new ArchiveException(ArchiveErrorCode.E_CODE, "посилання виходить за початок буфера");
            if (o + matchLength > rawLength)
                throw Payload("потік дає більше байтів, ніж raw_len");

            for (int i = 0; i < matchLength; i++)
            {
                output[o] = output[o - distance]; // перекриття дозволене: копіюємо побайтово
                o++;
            }
        }

        reader.VerifyPadding();
        if (o != rawLength)
            throw Payload("розпакована довжина не дорівнює raw_len");
        return output;
    }

    private static ArchiveException Payload(string message) => new(ArchiveErrorCode.E_PAYLOAD, message);
}
