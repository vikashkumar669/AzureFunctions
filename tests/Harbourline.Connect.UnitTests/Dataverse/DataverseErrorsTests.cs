using Harbourline.Connect.Core.Dataverse;
using Harbourline.Connect.UnitTests.Support;

namespace Harbourline.Connect.UnitTests.Dataverse;

public sealed class DataverseErrorsTests
{
    [Theory]
    [InlineData(DataverseErrors.ObjectDoesNotExist, DataverseErrorKind.NotFound)]
    [InlineData(DataverseErrors.DuplicateRecordEntityKey, DataverseErrorKind.Duplicate)]
    [InlineData(DataverseErrors.ConcurrencyVersionMismatch, DataverseErrorKind.ConcurrencyConflict)]
    [InlineData(DataverseErrors.PrivilegeDenied, DataverseErrorKind.PermissionDenied)]
    [InlineData(DataverseErrors.ThrottlingRequests, DataverseErrorKind.Throttled)]
    [InlineData(DataverseErrors.ThrottlingTime, DataverseErrorKind.Throttled)]
    [InlineData(DataverseErrors.ThrottlingConcurrency, DataverseErrorKind.Throttled)]
    [InlineData(12345, DataverseErrorKind.Unknown)]
    public void Codes_AreClassified(int code, DataverseErrorKind kind) =>
        Assert.Equal(kind, DataverseErrors.Classify(Faults.Of(code))!.Kind);

    [Fact]
    public void WrappedFault_IsFound()
    {
        var wrapped = new InvalidOperationException("outer",
            Faults.Of(DataverseErrors.ObjectDoesNotExist));
        Assert.Equal(DataverseErrorKind.NotFound,
            DataverseErrors.Classify(wrapped)!.Kind);
    }

    [Fact]
    public void NonDataverseException_IsNull() =>
        Assert.Null(DataverseErrors.Classify(new TimeoutException()));

    [Fact]
    public void ThrottlingFault_CarriesRetryAfter()
    {
        var fault = Faults.Of(DataverseErrors.ThrottlingRequests);
        fault.Detail.ErrorDetails["Retry-After"] = TimeSpan.FromSeconds(30);

        Assert.Equal(TimeSpan.FromSeconds(30),
            DataverseErrors.Classify(fault)!.RetryAfter);
    }

    [Fact]
    public void CodesMatchMicrosoftLearn()
    {
        // Decimal values as published in "Web service error codes".
        Assert.Equal(-2147088254, DataverseErrors.ConcurrencyVersionMismatch);
        Assert.Equal(-2147220969, DataverseErrors.ObjectDoesNotExist);
        Assert.Equal(-2147015902, DataverseErrors.ThrottlingRequests);
        Assert.Equal(-2147015903, DataverseErrors.ThrottlingTime);
        Assert.Equal(-2147015898, DataverseErrors.ThrottlingConcurrency);
    }
}
