using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Harbourline.Connect.Core.Common;
using Harbourline.Connect.Core.Exports;
using Harbourline.Connect.Functions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace Harbourline.Connect.Functions.Exports;

/// <summary>
/// GET /api/v1/leases/{number}/pack — the secure Excel export of
/// Chapter 22. Small packs are returned directly; larger ones are
/// uploaded and a 15-minute, read-only link is returned instead.
/// </summary>
public sealed class LeasePackFunction(
    LeasePackService packs,
    BlobServiceClient blobs,
    TimeProvider time)
{
    public const int DirectLimitBytes = 2 * 1024 * 1024;
    public const string Container = "lease-packs";

    // <listing:pack-function>
    [Function("GetLeasePack")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "v1/leases/{number}/pack")] HttpRequest request,
        string number,
        CancellationToken cancellationToken)
    {
        var caller = ClientPrincipal.ObjectIdOf(request);  // <1>
        if (caller is null)
        {
            return new UnauthorizedResult();
        }

        var outcome = await packs.BuildAsync(
            caller.Value, number, cancellationToken);      // <2>
        if (outcome.Status != OutcomeStatus.Ok)
        {
            return ApiResults.From(outcome, request);
        }

        var pack = outcome.Value!;
        if (pack.Content.Length <= DirectLimitBytes)
        {
            return new FileContentResult(pack.Content,
                "application/vnd.openxmlformats-officedocument"
                + ".spreadsheetml.sheet")
            {
                FileDownloadName = pack.FileName,          // <3>
            };
        }

        var url = await UploadAsync(pack, cancellationToken);
        return new OkObjectResult(new { downloadUrl = url }); // <4>
    }
    // </listing:pack-function>

    // <listing:user-delegation-sas>
    private async Task<Uri> UploadAsync(
        LeasePack pack, CancellationToken ct)
    {
        var blob = blobs.GetBlobContainerClient(Container)
            .GetBlobClient($"{Guid.NewGuid():N}/{pack.FileName}");
        await blob.UploadAsync(
            new BinaryData(pack.Content), ct);

        var now = time.GetUtcNow();
        var key = await blobs.GetUserDelegationKeyAsync(  // <1>
            now.AddMinutes(-5), now.AddMinutes(15), ct);

        var sas = new BlobSasBuilder
        {
            BlobContainerName = Container,
            BlobName = blob.Name,
            Resource = "b",                                // <2>
            ExpiresOn = now.AddMinutes(15),
        };
        sas.SetPermissions(BlobSasPermissions.Read);       // <3>

        return new BlobUriBuilder(blob.Uri)
        {
            Sas = sas.ToSasQueryParameters(
                key.Value, blobs.AccountName),
        }.ToUri();
    }
    // </listing:user-delegation-sas>
}
