using Xunit;
using SA.Attack;
using SA.Compression;
using SA.Core;
using SA.Packing;

namespace SA.Tests;


public class AttackTests
{
    private static IReadOnlyList<byte[]> SmallArchive()
    {
        var inputs = new List<InputFile>
        {
            new("a.txt", Enumerable.Repeat((byte)'a', 120).ToArray()),
            new("b.bin", Enumerable.Range(0, 60).Select(i => (byte)(i * 7)).ToArray()),
        };
        return ArchiveBuilder.Build(inputs, "pw", TeamConfig.Default, new PassThroughCompressor(), new ConstantClassifier(0));
    }

    [Fact]
    public void IntegrityAttack_OnOurArchive_AcceptsNothing_AndLeaksNoUnhandledExceptions()
    {
        var volumes = SmallArchive();

        var result = IntegrityAttack.Run(volumes, "pw", new PassThroughCompressor(), ciphertextSamples: 20);

        Assert.True(result.Mutations >= 73 + 20, "усі байти заголовка і тега плюс вибірка шифротексту");
        Assert.True(result.Accepted == 0, string.Join("; ", result.AcceptedMutations));
        Assert.Equal(0, result.Unhandled);
        Assert.Equal(result.Mutations, result.Rejected);
        Assert.True(result.Survived);

        // зміни різних полів дають різні коди
        Assert.True(result.RejectedByCode.ContainsKey(ArchiveErrorCode.E_MAGIC));
        Assert.True(result.RejectedByCode.ContainsKey(ArchiveErrorCode.E_LENGTH));
        Assert.True(result.RejectedByCode.ContainsKey(ArchiveErrorCode.E_HMAC));
    }

    [Fact]
    public void IntegrityAttack_RejectsBadVolumeIndex()
    {
        var volumes = SmallArchive();
        Assert.Throws<ArgumentOutOfRangeException>(
            () => IntegrityAttack.Run(volumes, "pw", new PassThroughCompressor(), volumeIndex: 5));
    }

    [Fact]
    public void IntegrityAttackResult_Survived_IsFalse_WhenAnythingIsAccepted()
    {
        var bad = new IntegrityAttackResult(
            10, 9, 1, 0, new Dictionary<ArchiveErrorCode, int>(), new[] { "байт 3 ^ 0x08" });
        Assert.False(bad.Survived);

        var unhandled = new IntegrityAttackResult(
            10, 9, 0, 1, new Dictionary<ArchiveErrorCode, int>(), Array.Empty<string>());
        Assert.False(unhandled.Survived);
    }
}
