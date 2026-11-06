using SA.Core;
using SA.Crypto;

namespace SA.Tests;

[TestClass]
public class CryptoTests
{
    private static byte[] Hex(string s) => Convert.FromHexString(s);

    [TestMethod]
    public void AesCtr_MatchesNistVector_Sp800_38a_F55()
    {
        // NIST SP 800-38A, F.5.5: початковий лічильник f0..fb fcfdfeff = nonce(12) + блок 0xFCFDFEFF.
        byte[] key = Hex("603deb1015ca71be2b73aef0857d77811f352c073b6108d72d9810a30914dff4");
        byte[] nonce = Hex("f0f1f2f3f4f5f6f7f8f9fafb");
        byte[] plain = Hex(
            "6bc1bee22e409f96e93d7e117393172aae2d8a571e03ac9c9eb76fac45af8e51" +
            "30c81c46a35ce411e5fbc1191a0a52eff69f2445df4f9b17ad2b417be66c3710");
        byte[] expected = Hex(
            "601ec313775789a5b7a7f504bbf3d228f443e3ca4d62b59aca84e990cacaf5c5" +
            "2b0930daa23de94ce87017ba2d84988ddfc9c58db67aada613c2dd08457941a6");

        CollectionAssert.AreEqual(expected, AesCtr.Transform(key, nonce, plain, firstBlock: 0xFCFDFEFFu));
    }

    [TestMethod]
    public void AesCtr_FirstCounterBlockIsOne()
    {
        // Нульовий вхід дає чистий потік ключів: перший блок = AES(nonce || 00000001).
        byte[] key = Hex("603deb1015ca71be2b73aef0857d77811f352c073b6108d72d9810a30914dff4");
        byte[] nonce = Enumerable.Range(0, 12).Select(i => (byte)i).ToArray();

        byte[] stream = AesCtr.Transform(key, nonce, new byte[16]);

        Assert.AreEqual("f0538d11a8e001d0b533c558051e37d6", Convert.ToHexStringLower(stream));
    }

    [TestMethod]
    public void AesCtr_RoundTrip_ForAwkwardLengths()
    {
        byte[] key = new byte[32];
        byte[] nonce = AesCtr.GenerateNonce();
        var rng = new Random(1);
        foreach (int len in new[] { 0, 1, 15, 16, 17, 31, 32, 33, 1000 })
        {
            byte[] data = new byte[len];
            rng.NextBytes(data);
            byte[] enc = AesCtr.Transform(key, nonce, data);
            Assert.AreEqual(len, enc.Length);
            CollectionAssert.AreEqual(data, AesCtr.Transform(key, nonce, enc));
        }
    }

    [TestMethod]
    public void AesCtr_DifferentNonce_GivesDifferentCiphertext()
    {
        byte[] key = new byte[32];
        byte[] data = new byte[64];
        byte[] a = AesCtr.Transform(key, new byte[12], data);
        byte[] nonce2 = new byte[12]; nonce2[0] = 1;
        byte[] b = AesCtr.Transform(key, nonce2, data);
        CollectionAssert.AreNotEqual(a, b);
    }

    [TestMethod]
    public void AesCtr_RejectsBadKeyAndNonceLength()
    {
        Assert.ThrowsExactly<ArgumentException>(() => AesCtr.Transform(new byte[16], new byte[12], new byte[1]));
        Assert.ThrowsExactly<ArgumentException>(() => AesCtr.Transform(new byte[32], new byte[16], new byte[1]));
    }

    [TestMethod]
    public void AesCtr_RejectsCounterOverflow()
    {
        // З блока 0xFFFFFFFF можна зашифрувати рівно один блок.
        byte[] key = new byte[32], nonce = new byte[12];
        AesCtr.Transform(key, nonce, new byte[16], firstBlock: uint.MaxValue);
        Assert.ThrowsExactly<ArgumentException>(
            () => AesCtr.Transform(key, nonce, new byte[17], firstBlock: uint.MaxValue));
    }

    [TestMethod]
    public void Hmac_MatchesRfc4231_TestCase1()
    {
        byte[] key = Enumerable.Repeat((byte)0x0b, 20).ToArray();
        byte[] tag = ArchiveMac.Compute(key, System.Text.Encoding.ASCII.GetBytes("Hi There"));
        Assert.AreEqual("b0344c61d8db38535ca8afceaf0bf12b881dc200c9833da726e9376c2e32cff7",
            Convert.ToHexStringLower(tag));
    }

    [TestMethod]
    public void Hmac_PartsAreConcatenated()
    {
        byte[] key = new byte[32];
        byte[] whole = ArchiveMac.Compute(key, new byte[] { 1, 2, 3, 4 });
        byte[] parts = ArchiveMac.Compute(key, new byte[] { 1, 2 }, new byte[] { 3, 4 });
        CollectionAssert.AreEqual(whole, parts);
    }

    [TestMethod]
    public void Hmac_Verify_RejectsModifiedDataAndWrongKey()
    {
        byte[] key = new byte[32];
        byte[] data = { 1, 2, 3, 4, 5 };
        byte[] tag = ArchiveMac.Compute(key, data);

        ArchiveMac.Verify(key, tag, data); // не кидає

        byte[] tampered = (byte[])data.Clone();
        tampered[2] ^= 1;
        AssertHmacFails(() => ArchiveMac.Verify(key, tag, tampered));

        byte[] otherKey = new byte[32]; otherKey[0] = 1;
        AssertHmacFails(() => ArchiveMac.Verify(otherKey, tag, data));
        AssertHmacFails(() => ArchiveMac.Verify(key, tag[..31], data));
    }

    private static void AssertHmacFails(Action action)
    {
        var ex = Assert.ThrowsExactly<ArchiveException>(action);
        Assert.AreEqual(ArchiveErrorCode.E_HMAC, ex.Code);
    }

    [TestMethod]
    public void KeyDerivation_MatchesKnownVector_AndKeysDiffer()
    {
        // Еталон пораховано окремо (Python hashlib.pbkdf2_hmac, sha256, 100000, 64 байти).
        byte[] salt = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
        var keys = KeyDerivation.Derive("пароль-123", salt);

        Assert.AreEqual("03e9aac667d1efe491957954becd1528ea1d39498609c695f8fd33408d60ca46",
            Convert.ToHexStringLower(keys.EncKey));
        Assert.AreEqual("b12a8eae507a009347e52c3209a6e1967b6e88155d0ea457348221b2bc2816b3",
            Convert.ToHexStringLower(keys.MacKey));
        CollectionAssert.AreNotEqual(keys.EncKey, keys.MacKey);
    }

    [TestMethod]
    public void KeyDerivation_RejectsWrongSaltLength()
    {
        Assert.ThrowsExactly<ArgumentException>(() => KeyDerivation.Derive("x", new byte[15]));
        Assert.AreEqual(KeyDerivation.SaltLength, KeyDerivation.GenerateSalt().Length);
    }
}
