using SA.Attack;
using SA.Compression;
using SA.Core;
using SA.Packing;

namespace SA.Tests;

[TestClass]
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

    [TestMethod]
    public void IntegrityAttack_OnOurArchive_AcceptsNothing_AndLeaksNoUnhandledExceptions()
    {
        var volumes = SmallArchive();

        var result = IntegrityAttack.Run(volumes, "pw", new PassThroughCompressor(), ciphertextSamples: 20);

        Assert.IsTrue(result.Mutations >= 73 + 20, "усі байти заголовка і тега плюс вибірка шифротексту");
        Assert.AreEqual(0, result.Accepted, string.Join("; ", result.AcceptedMutations));
        Assert.AreEqual(0, result.Unhandled);
        Assert.AreEqual(result.Mutations, result.Rejected);
        Assert.IsTrue(result.Survived);

        // зміни різних полів дають різні коди
        Assert.IsTrue(result.RejectedByCode.ContainsKey(ArchiveErrorCode.E_MAGIC));
        Assert.IsTrue(result.RejectedByCode.ContainsKey(ArchiveErrorCode.E_LENGTH));
        Assert.IsTrue(result.RejectedByCode.ContainsKey(ArchiveErrorCode.E_HMAC));
    }

    [TestMethod]
    public void IntegrityAttack_RejectsBadVolumeIndex()
    {
        var volumes = SmallArchive();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => IntegrityAttack.Run(volumes, "pw", new PassThroughCompressor(), volumeIndex: 5));
    }

    [TestMethod]
    public void IntegrityAttackResult_Survived_IsFalse_WhenAnythingIsAccepted()
    {
        var bad = new IntegrityAttackResult(
            10, 9, 1, 0, new Dictionary<ArchiveErrorCode, int>(), new[] { "байт 3 ^ 0x08" });
        Assert.IsFalse(bad.Survived);

        var unhandled = new IntegrityAttackResult(
            10, 9, 0, 1, new Dictionary<ArchiveErrorCode, int>(), Array.Empty<string>());
        Assert.IsFalse(unhandled.Survived);
    }
}
