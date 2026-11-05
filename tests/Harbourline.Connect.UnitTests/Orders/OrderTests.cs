using System.Net;
using Harbourline.Connect.Contracts.Orders;
using Harbourline.Connect.Core.Dataverse;
using Harbourline.Connect.Core.Integrations;
using Harbourline.Connect.Core.Orders;
using Harbourline.Connect.UnitTests.Support;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk.Messages;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Harbourline.Connect.UnitTests.Orders;

public sealed class OrderTests
{
    private static OrderSubmission Valid() =>
        new("ORD-1001", "HBR-0042", "Plumbing", 180m, "PARTNER-7");

    // <listing:test-classifier>
    [Theory]
    [MemberData(nameof(Failures))]
    public void Failures_AreClassified(Exception ex, bool transient) =>
        Assert.Equal(transient, FailureClassifier.IsTransient(ex));

    public static TheoryData<Exception, bool> Failures() => new()
    {
        { new PermanentFailureException("bad"), false },
        { new HttpRequestException("down", null,
            HttpStatusCode.ServiceUnavailable), true },
        { new HttpRequestException("busy", null,
            HttpStatusCode.TooManyRequests), true },
        { new HttpRequestException("bad", null,
            HttpStatusCode.BadRequest), false },
        { new HttpRequestException("no network"), true },
        { Faults.Of(DataverseErrors.ThrottlingRequests), true },
        { Faults.Of(DataverseErrors.PrivilegeDenied), false },
        { new TimeoutException(), true },
    };
    // </listing:test-classifier>

    [Fact]
    public async Task InvalidOrder_IsPermanent()
    {
        var processor = new OrderProcessor(
            Substitute.For<IOrganizationServiceAsync2>(),
            Substitute.For<ILedgerOneClient>());

        await Assert.ThrowsAsync<PermanentFailureException>(() =>
            processor.ProcessAsync(Valid() with { Category = "Gardening" },
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UnknownProperty_IsPermanent()
    {
        var dataverse = Substitute.For<IOrganizationServiceAsync2>();
        dataverse.ExecuteAsync(Arg.Any<UpsertRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(Faults.Of(DataverseErrors.RecordNotFoundByEntityKey));
        var ledger = Substitute.For<ILedgerOneClient>();
        var processor = new OrderProcessor(dataverse, ledger);

        await Assert.ThrowsAsync<PermanentFailureException>(() =>
            processor.ProcessAsync(Valid(), TestContext.Current.CancellationToken));
        await ledger.DidNotReceive().SubmitOrderAsync(
            Arg.Any<LedgerOrder>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ValidOrder_IsRecordedThenSubmitted()
    {
        var dataverse = Substitute.For<IOrganizationServiceAsync2>();
        var ledger = Substitute.For<ILedgerOneClient>();
        ledger.SubmitOrderAsync(Arg.Any<LedgerOrder>(), Arg.Any<CancellationToken>())
            .Returns(new LedgerOrderReceipt("L1-123"));

        var id = await new OrderProcessor(dataverse, ledger)
            .ProcessAsync(Valid(), TestContext.Current.CancellationToken);

        Assert.Equal("L1-123", id);
        Received.InOrder(() =>
        {
            dataverse.ExecuteAsync(Arg.Any<UpsertRequest>(), Arg.Any<CancellationToken>());
            ledger.SubmitOrderAsync(Arg.Any<LedgerOrder>(), Arg.Any<CancellationToken>());
        });
    }
}
