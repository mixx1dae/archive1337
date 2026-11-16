using System.Security.Cryptography;
using SA.Core;
using SA.Crypto;
using SA.Format;

namespace SA.Packing;

public sealed record InputFile(string Name, byte[] Data);

/// <summary>
/// Повний конвеєр створення архіву: класифікація, розбиття на блоки, стиснення блоків,
/// рюкзак по томах, шифрування і HMAC. PBKDF2 виконується один раз на архів.
/// </summary>
public static class ArchiveBuilder
{
    private const byte Strategy = 1;

    public static IReadOnlyList<byte[]> Build(
        IReadOnlyList<InputFile> files,
        string password,
        TeamConfig config,
        IBlockCompressor compressor,
        IFileClassifier classifier,
        PackingMethod method = PackingMethod.Dp)
    {
        config.Validate();

        var names = new HashSet<string>(StringComparer.Ordinal);
        var packingFiles = new List<PackingFile>(files.Count);

        foreach (var file in files)
        {
            SafeName.Validate(file.Name);
            if (!names.Add(file.Name))
                throw new ArgumentException($"Дубльоване ім'я файла: {file.Name}", nameof(files));

            byte type = classifier.Classify(file.Data);
            var blocks = new List<PackingBlock>();
            uint seq = 0;

            for (int offset = 0; offset < file.Data.Length; offset += config.BlockSize)
            {
                int length = Math.Min(config.BlockSize, file.Data.Length - offset);
                CompressedBlock coded = compressor.Compress(file.Data.AsSpan(offset, length));
                blocks.Add(new PackingBlock(seq++, (uint)length, coded.Data, coded.NoCompression));
            }

            // Як в еталонних архівах (ref_02): порожній файл подається одним порожнім блоком NO_COMPRESSION.
            if (file.Data.Length == 0)
                blocks.Add(new PackingBlock(0, 0, Array.Empty<byte>(), true));

            // В усіх еталонних архівах strategy = 1, зокрема й там, де блок збережено без стиснення (ref_05).
            packingFiles.Add(new PackingFile(
                file.Name, (uint)file.Data.Length, Crc32.Compute(file.Data), type, Strategy, blocks));
        }

        var payloads = VolumePacker.Pack(packingFiles, config, method);

        byte[] salt = KeyDerivation.GenerateSalt();
        DerivedKeys keys = KeyDerivation.Derive(password, salt);
        byte[] noncePrefix = RandomNumberGenerator.GetBytes(8);

        var volumes = new List<byte[]>(payloads.Count);
        for (int i = 0; i < payloads.Count; i++)
        {
            ushort index = (ushort)i;
            byte[] nonce = VolumeWriter.MakeNonce(noncePrefix, index);
            volumes.Add(VolumeWriter.Build(keys, salt, nonce, index, (ushort)payloads.Count, payloads[i]));
        }
        return volumes;
    }
}
