using SA.Core;

namespace SA.Tests;

[TestClass]
public class ArchiveExceptionTests
{
    [TestMethod]
    public void CarriesCodeAndInnerException()
    {
        var inner = new EndOfStreamException("кінець");
        var ex = new ArchiveException(ArchiveErrorCode.E_CODE, "обрізаний потік", inner);

        Assert.AreEqual(ArchiveErrorCode.E_CODE, ex.Code);
        Assert.AreSame(inner, ex.InnerException);
        StringAssert.Contains(ex.Message, "E_CODE");
    }
}
