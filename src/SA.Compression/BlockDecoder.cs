using SA.Compression.BitIO;
using SA.Compression.Huffman;
using SA.Core;

namespace SA.Compression;

/// <summary>
/// Декодер закодованого блоку: перетворює бітовий потік назад у вихідні байти.
/// Будь-яка невідповідність у даних викликає захисний доменний виняток ArcException.
/// </summary>
public static class BlockDecoder
{
    public static byte[] Decode(ReadOnlySpan<byte> block, int rawLength)
    {
        int pos = 0;

        // 1. Зчитуємо та перевіряємо таблиці канонічних кодів Гаффмана
        var litLengths = CodeTable.Read(block, ref pos, CodecConstants.LitAlphabetSize - 1, "LIT");
        var lenLengths = CodeTable.Read(block, ref pos, CodecConstants.LenAlphabetSize - 1, "LEN");
        var distLengths = CodeTable.Read(block, ref pos, CodecConstants.DistAlphabetSize - 1, "DIST");

        // 2. Зчитуємо метадані блоку (token_count та stream_len)
        if (pos + 8 > block.Length)
            throw new ArcException(ErrorCodes.Payload, "немає token_count / stream_len");

        long tokenCount = ReadUInt32(block, pos);
        long streamLength = ReadUInt32(block, pos + 4);
        pos += 8;

        // Перевіряємо, чи збігається розмір потоку з фактичним залишком байтів у блоці
        if (pos + streamLength != block.Length)
            throw new ArcException(ErrorCodes.Payload, "stream_len не збігається з розміром блоку");

        // Токенів не може бути більше за початкову довжину сирих даних (кожен токен дає >= 1 байта)
        if (tokenCount > rawLength)
            throw new ArcException(ErrorCodes.Payload, "token_count більший за raw_len");

        // 3. Створюємо канонічні таблиці та побітовий рідер
        var litCode = new CanonicalCode(litLengths);
        var lenCode = new CanonicalCode(lenLengths);
        var distCode = new CanonicalCode(distLengths);
        var reader = new BitReader(block.Slice(pos).ToArray());

        var output = new byte[rawLength];
        int written = 0;

        // 4. Послідовно розпаковуємо всі токени
        for (long k = 0; k < tokenCount; k++)
        {
            int symbol = litCode.ReadSymbol(reader);

            // Якщо зчитано значення 0..255 — це звичайний байт
            if (symbol < CodecConstants.MatchMarker)
            {
                if (written >= rawLength)
                    throw new ArcException(ErrorCodes.Payload, "вивід перевищує raw_len");

                output[written++] = (byte)symbol;
                continue;
            }

            // Якщо зчитано маркер 256 — розпаковуємо посилання LZ77
            int length = lenCode.ReadSymbol(reader) + CodecConstants.MinMatch;
            int code = distCode.ReadSymbol(reader);
            int extraBits = DistanceCoding.ExtraBits(code);
            uint extra = extraBits > 0 ? reader.ReadBits(extraBits) : 0;
            int distance = DistanceCoding.Decode(code, extra);

            // Захист від зловмисних або пошкоджених даних
            if (distance > written)
                throw new ArcException(ErrorCodes.Code, "посилання за межі буфера");
            if (written + length > rawLength)
                throw new ArcException(ErrorCodes.Payload, "вивід перевищує raw_len");

            // Копіюємо побайтово: якщо distance < length, фрагмент багаторазово самовідтворюється
            for (int c = 0; c < length; c++, written++)
                output[written] = output[written - distance];
        }

        // Перевіряємо точність обсягу розпакованих даних
        if (written != rawLength)
            throw new ArcException(ErrorCodes.Payload, "розмір розпакованого блоку не дорівнює raw_len");

        // Перевіряємо нульовий паддінг залишкових бітів
        reader.VerifyPadding();

        return output;
    }

    /// <summary>
    /// Зчитує 4 байти в форматі Big-Endian
    /// </summary>
    private static uint ReadUInt32(ReadOnlySpan<byte> s, int p) =>
        (uint)(s[p] << 24 | s[p + 1] << 16 | s[p + 2] << 8 | s[p + 3]);
}