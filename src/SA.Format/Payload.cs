using System.Buffers.Binary;
using System.Text;
using SA.Core;

namespace SA.Format;

/// <summary>Запис таблиці файлів (FORMAT_SPEC §2.2). Size і Crc стосуються цілого файла.</summary>
public sealed record FileEntry(string Name, uint OrigSize, uint Crc, byte FileType, byte Strategy);

/// <summary>Запис таблиці блоків, 15 байтів (FORMAT_SPEC §2.3).</summary>
public sealed record BlockEntry(ushort FileIndex, uint BlockSeq, uint RawLen, uint CodedLen, byte BlockFlags)
{
    public const int Size = 15;
    public const byte NoCompressionFlag = 0x01;
    public bool NoCompression => (BlockFlags & NoCompressionFlag) != 0;
}

/// <summary>Безпечні імена файлів (FORMAT_SPEC §2.2): лише прості імена без шляху.</summary>
public static class SafeName
{
    public static void Validate(string name)
    {
        bool bad = name.Length == 0 || name == "." || name == "..";
        if (!bad)
        {
            foreach (char ch in name)
            {
                if (ch == '/' || ch == '\\' || ch == ':' || ch == '\0') { bad = true; break; }
            }
        }
        if (bad)
            throw new ArchiveException(ArchiveErrorCode.E_PAYLOAD, "небезпечне або порожнє ім'я файла.");
    }
}

/// <summary>Розшифроване навантаження тому (FORMAT_SPEC §2).</summary>
public sealed class VolumePayload
{
    public const byte FlagPrediction = 0x01;

    public byte Flags { get; }
    public IReadOnlyList<FileEntry> Files { get; }
    public IReadOnlyList<BlockEntry> Blocks { get; }
    public IReadOnlyList<byte[]> BlockData { get; }

    public VolumePayload(
        byte flags, IReadOnlyList<FileEntry> files, IReadOnlyList<BlockEntry> blocks, IReadOnlyList<byte[]> blockData)
    {
        if (blocks.Count != blockData.Count)
            throw new ArgumentException("Кількість блоків і даних блоків має збігатися.");
        Flags = flags;
        Files = files;
        Blocks = blocks;
        BlockData = blockData;
    }

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public byte[] Serialize()
    {
        if (Files.Count > ushort.MaxValue || Blocks.Count > ushort.MaxValue)
            throw new ArgumentException("Забагато файлів або блоків для одного тому.");

        var w = new ByteWriter();
        w.U16((ushort)Files.Count);
        w.U8(Flags);
        w.U8(0); // reserved
        foreach (var f in Files)
        {
            byte[] name = StrictUtf8.GetBytes(f.Name);
            if (name.Length > ushort.MaxValue) throw new ArgumentException("Задовге ім'я файла.");
            w.U16((ushort)name.Length);
            w.Bytes(name);
            w.U32(f.OrigSize);
            w.U32(f.Crc);
            w.U8(f.FileType);
            w.U8(f.Strategy);
        }
        w.U16((ushort)Blocks.Count);
        foreach (var b in Blocks)
        {
            w.U16(b.FileIndex);
            w.U32(b.BlockSeq);
            w.U32(b.RawLen);
            w.U32(b.CodedLen);
            w.U8(b.BlockFlags);
        }
        for (int i = 0; i < Blocks.Count; i++)
        {
            if (BlockData[i].Length != Blocks[i].CodedLen)
                throw new ArgumentException($"Дані блока {i} не збігаються з coded_len.");
            w.Bytes(BlockData[i]);
        }
        return w.ToArray();
    }

    /// <summary>
    /// Розбір навантаження (крок 7 розділу 4.3). Усі порушення дають E_PAYLOAD:
    /// ненульове reserved, невідомі біти flags, file_type &gt; 6, небезпечні імена,
    /// неузгоджені довжини секцій, зайві байти.
    /// </summary>
    public static VolumePayload Parse(ReadOnlySpan<byte> plaintext)
    {
        var c = new ByteCursor(plaintext.ToArray());

        ushort fileCount = c.U16();
        byte flags = c.U8();
        byte reserved = c.U8();
        if (reserved != 0) throw Bad("reserved не дорівнює 0.");
        if ((flags & 0xFE) != 0) throw Bad("невідомі біти flags.");

        var files = new List<FileEntry>(fileCount);
        for (int i = 0; i < fileCount; i++)
        {
            ushort nameLen = c.U16();
            byte[] nameBytes = c.Take(nameLen);
            string name;
            try { name = StrictUtf8.GetString(nameBytes); }
            catch (DecoderFallbackException) { throw Bad("ім'я файла не є коректним UTF-8."); }
            SafeName.Validate(name);

            uint origSize = c.U32();
            uint crc = c.U32();
            byte fileType = c.U8();
            if (fileType > 6) throw Bad("file_type більший за 6.");
            byte strategy = c.U8();
            files.Add(new FileEntry(name, origSize, crc, fileType, strategy));
        }

        ushort blockCount = c.U16();
        var blocks = new List<BlockEntry>(blockCount);
        for (int j = 0; j < blockCount; j++)
        {
            ushort fileIndex = c.U16();
            uint seq = c.U32();
            uint rawLen = c.U32();
            uint codedLen = c.U32();
            byte bf = c.U8();
            if ((bf & 0xFE) != 0) throw Bad("невідомі біти block_flags.");
            if (fileIndex >= fileCount) throw Bad("file_index поза таблицею файлів.");
            if ((bf & BlockEntry.NoCompressionFlag) != 0 && codedLen != rawLen)
                throw Bad("NO_COMPRESSION вимагає coded_len = raw_len.");
            blocks.Add(new BlockEntry(fileIndex, seq, rawLen, codedLen, bf));
        }

        var data = new List<byte[]>(blockCount);
        foreach (var b in blocks)
            data.Add(c.Take(b.CodedLen));

        if (c.Remaining != 0) throw Bad("зайві байти після даних блоків.");
        return new VolumePayload(flags, files, blocks, data);
    }

    private static ArchiveException Bad(string message) => new(ArchiveErrorCode.E_PAYLOAD, message);
}

internal sealed class ByteWriter
{
    private readonly MemoryStream _ms = new();

    public void U8(byte v) => _ms.WriteByte(v);

    public void U16(ushort v)
    {
        Span<byte> t = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(t, v);
        _ms.Write(t);
    }

    public void U32(uint v)
    {
        Span<byte> t = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(t, v);
        _ms.Write(t);
    }

    public void Bytes(ReadOnlySpan<byte> b) => _ms.Write(b);

    public byte[] ToArray() => _ms.ToArray();
}

/// <summary>Читач big-endian з перевіркою меж: обрізані дані дають E_PAYLOAD, а не виняток .NET.</summary>
internal sealed class ByteCursor
{
    private readonly byte[] _data;
    private int _pos;

    public ByteCursor(byte[] data) => _data = data;

    public int Remaining => _data.Length - _pos;

    public byte U8()
    {
        Need(1);
        return _data[_pos++];
    }

    public ushort U16()
    {
        Need(2);
        ushort v = BinaryPrimitives.ReadUInt16BigEndian(_data.AsSpan(_pos));
        _pos += 2;
        return v;
    }

    public uint U32()
    {
        Need(4);
        uint v = BinaryPrimitives.ReadUInt32BigEndian(_data.AsSpan(_pos));
        _pos += 4;
        return v;
    }

    public byte[] Take(uint n)
    {
        if (n > (uint)Remaining) throw Short();
        var r = _data.AsSpan(_pos, (int)n).ToArray();
        _pos += (int)n;
        return r;
    }

    private void Need(int n)
    {
        if (n > Remaining) throw Short();
    }

    private static ArchiveException Short() =>
        new(ArchiveErrorCode.E_PAYLOAD, "навантаження обрізане: довжини секцій не узгоджені.");
}
