<<<<<<< HEAD
﻿namespace SA.Core;
=======
﻿/* namespace SA.Core;
>>>>>>> 0e5ee03d2c2be78bee0b57f623a074bcbc82ba4a

/// <summary>Результат стиснення одного блоку.</summary>
public readonly record struct CompressedBlock(byte[] Data, bool NoCompression);

/// <summary>Спільний інтерфейс компресора блоку (SA.Core).</summary>
public interface IBlockCompressor
{
    CompressedBlock Compress(ReadOnlySpan<byte> raw);
    byte[] Decompress(ReadOnlySpan<byte> coded, int rawLength, bool noCompression);
<<<<<<< HEAD
}
=======
} */
>>>>>>> 0e5ee03d2c2be78bee0b57f623a074bcbc82ba4a
