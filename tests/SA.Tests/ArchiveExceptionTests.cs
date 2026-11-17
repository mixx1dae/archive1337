using Xunit;
using SA.Core;

namespace SA.Tests;


public class ArchiveExceptionTests
{
    [Fact]
    public void CarriesCodeAndInnerException()
    {
        var inner = new EndOfStreamException("кінець");
        var ex = new ArchiveException(ArchiveErrorCode.E_CODE, "обрізаний потік", inner);

        Assert.Equal(ArchiveErrorCode.E_CODE, ex.Code);
        Assert.Same(inner, ex.InnerException);
        Assert.Contains("E_CODE", ex.Message);
    }
}
