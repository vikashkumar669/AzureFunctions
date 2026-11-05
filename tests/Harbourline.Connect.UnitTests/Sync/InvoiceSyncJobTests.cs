using Harbourline.Connect.Core.Dataverse;
using Harbourline.Connect.Core.Integrations;
using Harbourline.Connect.Core.Sync;
using Harbourline.Connect.UnitTests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Harbourline.Connect.UnitTests.Sync;

public sealed class InvoiceSyncJobTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 27, 2, 0, 0, TimeSpan.Zero);

    private readonly ILedgerOneClient ledger = Substitute.For<ILedgerOneClient>();
    private readonly IOrganizationServiceAsync2 dataverse =
        Substitute.For<IOrganizationServiceAsync2>();
    private readonly ICheckpointStore checkpoints = Substitute.For<ICheckpointStore>();

    private InvoiceSyncJob Job()
    {
        var time = Substitute.For<TimeProvider>();
        time.GetUtcNow().Returns(Now);
        return new InvoiceSyncJob(ledger, dataverse, checkpoints, time,
            NullLogger<InvoiceSyncJob>.Instance);
    }

    private static LedgerInvoice Invoice(string number) => new(
        number, "LSE-0001", 1250m, new DateOnly(2026, 11, 1), "Open", Now);

    [Fact]
    public void Mapper_UsesAlternateKeysForRowAndLookup()
    {
        var row = InvoiceMapper.ToEntity(Invoice("INV-1"));

        Assert.Equal("INV-1", row.KeyAttributes[InvoiceTable.Number]);
        var lease = (EntityReference)row[InvoiceTable.Lease];
        Assert.Equal("LSE-0001", lease.KeyAttributes[LeaseTable.Number]);
    }

    // <listing:test-watermark>
    [Fact]
    public async Task Run_StartsFromWatermarkMinusOverlap_AndSavesNewOne()
    {
        var last = new DateTimeOffset(2026, 10, 26, 2, 0, 0, TimeSpan.Zero);
        checkpoints.GetAsync(InvoiceSyncJob.CheckpointName,
                Arg.Any<CancellationToken>())
            .Returns(new Checkpoint(InvoiceSyncJob.CheckpointName,
                last.ToString("O"), null));
        ledger.GetInvoicesAsync(Arg.Any<DateTimeOffset>(), 1,
                Arg.Any<CancellationToken>())
            .Returns(new LedgerInvoicePage(
                [Invoice("INV-1"), Invoice("INV-2")], null));

        var result = await Job().RunAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Upserted);
        await ledger.Received(1).GetInvoicesAsync(
            last - InvoiceSyncJob.Overlap, 1,
            Arg.Any<CancellationToken>());                 // <1>
        await checkpoints.Received(1).SaveAsync(
            Arg.Is<Checkpoint>(c => c.Token == Now.ToString("O")),
            Arg.Any<CancellationToken>());                 // <2>
    }
    // </listing:test-watermark>

    [Fact]
    public async Task Run_DoesNotAdvanceWatermark_WhenAnInvoiceFails()
    {
        checkpoints.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new Checkpoint(InvoiceSyncJob.CheckpointName, null, null));
        ledger.GetInvoicesAsync(Arg.Any<DateTimeOffset>(), 1,
                Arg.Any<CancellationToken>())
            .Returns(new LedgerInvoicePage([Invoice("INV-1")], null));
        dataverse.ExecuteAsync(Arg.Any<UpsertRequest>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(Faults.Of(DataverseErrors.ObjectDoesNotExist));

        var result = await Job().RunAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Failed);
        await checkpoints.DidNotReceive().SaveAsync(
            Arg.Any<Checkpoint>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void UnknownLedgerStatus_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => InvoiceMapper.StatusOf("Frozen"));
}
