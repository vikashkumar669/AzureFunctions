using System.ServiceModel;
using Microsoft.Xrm.Sdk;

namespace Harbourline.Connect.Core.Dataverse;

/// <summary>What a Dataverse failure means to a caller.</summary>
public enum DataverseErrorKind
{
    Unknown,
    NotFound,
    Duplicate,
    ConcurrencyConflict,
    PermissionDenied,
    Throttled,
    InvalidRequest,
}

/// <summary>A classified Dataverse error.</summary>
public sealed record DataverseError(
    DataverseErrorKind Kind,
    int Code,
    string Message,
    TimeSpan? RetryAfter);

/// <summary>
/// Maps Dataverse error codes to a small set of outcomes. The codes are
/// from "Web service error codes" on Microsoft Learn.
/// </summary>
public static class DataverseErrors
{
    // <listing:error-codes>
    public const int ObjectDoesNotExist = unchecked((int)0x80040217);
    public const int RecordNotFoundByEntityKey =
        unchecked((int)0x80060891);
    public const int DuplicateRecord = unchecked((int)0x80040237);
    public const int DuplicateRecordEntityKey =
        unchecked((int)0x80060892);
    public const int ConcurrencyVersionMismatch =
        unchecked((int)0x80060882);
    public const int PrivilegeDenied = unchecked((int)0x80040220);
    public const int InvalidArgument = unchecked((int)0x80040203);
    public const int ThrottlingRequests = unchecked((int)0x80072322);
    public const int ThrottlingTime = unchecked((int)0x80072321);
    public const int ThrottlingConcurrency =
        unchecked((int)0x80072326);
    // </listing:error-codes>

    // <listing:error-classify>
    public static DataverseError? Classify(Exception exception)
    {
        for (var e = exception; e is not null; e = e.InnerException)
        {
            if (e is FaultException<OrganizationServiceFault> fault) // <1>
            {
                var detail = fault.Detail;
                return new DataverseError(
                    KindOf(detail.ErrorCode),
                    detail.ErrorCode,
                    detail.Message,
                    RetryAfterOf(detail));
            }
        }

        return null;                                       // <2>
    }

    public static DataverseErrorKind KindOf(int code) => code switch
    {
        ObjectDoesNotExist or RecordNotFoundByEntityKey
            => DataverseErrorKind.NotFound,
        DuplicateRecord or DuplicateRecordEntityKey
            => DataverseErrorKind.Duplicate,
        ConcurrencyVersionMismatch
            => DataverseErrorKind.ConcurrencyConflict,
        PrivilegeDenied => DataverseErrorKind.PermissionDenied,
        InvalidArgument => DataverseErrorKind.InvalidRequest,
        ThrottlingRequests or ThrottlingTime or ThrottlingConcurrency
            => DataverseErrorKind.Throttled,
        _ => DataverseErrorKind.Unknown,
    };

    private static TimeSpan? RetryAfterOf(OrganizationServiceFault f)
    {
        // Throttling faults carry the wait time in ErrorDetails. // <3>
        return f.ErrorDetails is not null
            && f.ErrorDetails.TryGetValue("Retry-After", out var v)
            && v is TimeSpan wait
                ? wait
                : null;
    }
    // </listing:error-classify>
}
