using Harbourline.Connect.Contracts.Properties;
using Harbourline.Connect.Core.Common;
using Harbourline.Connect.Core.Dataverse;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace Harbourline.Connect.Core.Properties;

/// <summary>
/// Reads and writes hl_property rows. Depends only on
/// IOrganizationServiceAsync2, so tests substitute it (Chapter 19).
/// </summary>
public sealed partial class PropertyService(
    IOrganizationServiceAsync2 dataverse,
    ILogger<PropertyService> logger)
{
    public const int MaxPageSize = 200;

    private static readonly string[] Columns =
    [
        PropertyTable.Code, PropertyTable.Name, PropertyTable.City,
        PropertyTable.Postcode, PropertyTable.Bedrooms,
        PropertyTable.MonthlyRent, PropertyTable.Status,
        PropertyTable.AvailableFrom,
    ];

    // <listing:property-list>
    public async Task<PropertyPage> ListAsync(
        PropertyQuery query,
        string? continuation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var size = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var filter = ContinuationToken.FilterOf(query);
        var position = ContinuationToken.Decode(
            continuation, filter);                         // <1>

        var qe = new QueryExpression(PropertyTable.LogicalName)
        {
            ColumnSet = new ColumnSet(Columns),            // <2>
            PageInfo = new PagingInfo
            {
                Count = size,
                PageNumber = position.Page,
                PagingCookie = position.Cookie,            // <3>
            },
        };
        qe.AddOrder(PropertyTable.Name, OrderType.Ascending);
        qe.AddOrder(PropertyTable.Id, OrderType.Ascending); // <4>
        AddFilters(qe.Criteria, query);

        var result = await dataverse
            .RetrieveMultipleAsync(qe, cancellationToken)
            .ConfigureAwait(false);

        var next = result.MoreRecords
            ? ContinuationToken.Encode(new PagePosition(
                position.Page + 1, result.PagingCookie, filter))
            : null;                                        // <5>

        return new PropertyPage(
            result.Entities.Select(PropertyMapper.ToDto).ToList(),
            next);
    }
    // </listing:property-list>

    // <listing:property-filters>
    private static void AddFilters(
        FilterExpression criteria, PropertyQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.City))
        {
            criteria.AddCondition(PropertyTable.City,
                ConditionOperator.Equal, query.City.Trim());
        }

        if (query.Status is not null
            && PropertyValidator.TryParseStatus(
                query.Status, out var status))
        {
            criteria.AddCondition(PropertyTable.Status,
                ConditionOperator.Equal, (int)status);
        }

        if (query.MinBedrooms is { } min)
        {
            criteria.AddCondition(PropertyTable.Bedrooms,
                ConditionOperator.GreaterEqual, min);
        }
    }
    // </listing:property-filters>

    // <listing:property-get>
    public async Task<PropertyDto?> GetByCodeAsync(
        string code, CancellationToken cancellationToken)
    {
        var request = new RetrieveRequest
        {
            Target = new EntityReference(
                PropertyTable.LogicalName,
                PropertyTable.Code, code),                 // <1>
            ColumnSet = new ColumnSet(Columns),
        };

        try
        {
            var response = (RetrieveResponse)await dataverse
                .ExecuteAsync(request, cancellationToken)
                .ConfigureAwait(false);
            return PropertyMapper.ToDto(response.Entity);
        }
        catch (Exception ex) when (
            DataverseErrors.Classify(ex)?.Kind
                is DataverseErrorKind.NotFound)            // <2>
        {
            return null;
        }
    }
    // </listing:property-get>

    public async Task<Outcome<PropertyDto>> CreateAsync(
        CreatePropertyRequest request,
        CancellationToken cancellationToken)
    {
        var errors = PropertyValidator.Validate(request);
        if (!errors.IsValid)
        {
            return Outcome<PropertyDto>.Invalid(errors.ToDictionary());
        }

        try
        {
            await dataverse
                .CreateAsync(PropertyMapper.ToEntity(request),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (
            DataverseErrors.Classify(ex)?.Kind
                is DataverseErrorKind.Duplicate)
        {
            return Outcome<PropertyDto>.Conflict(
                $"Property {request.Code} already exists.");
        }

        LogCreated(logger, request.Code!);
        var created = await GetByCodeAsync(
            request.Code!, cancellationToken).ConfigureAwait(false);
        return Outcome<PropertyDto>.Created(created!);
    }

    // <listing:property-update>
    public async Task<Outcome<PropertyDto>> UpdateAsync(
        string code,
        UpdatePropertyRequest request,
        string? ifMatch,
        CancellationToken cancellationToken)
    {
        var errors = PropertyValidator.Validate(request);
        if (!errors.IsValid)
        {
            return Outcome<PropertyDto>.Invalid(errors.ToDictionary());
        }

        var update = new UpdateRequest
        {
            Target = PropertyMapper.ToPatch(code, request, ifMatch),
            ConcurrencyBehavior = ifMatch is null
                ? ConcurrencyBehavior.Default
                : ConcurrencyBehavior.IfRowVersionMatches, // <1>
        };

        try
        {
            await dataverse.ExecuteAsync(update, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (
            DataverseErrors.Classify(ex) is { } error)
        {
            switch (error.Kind)                            // <2>
            {
                case DataverseErrorKind.NotFound:
                    return Outcome<PropertyDto>.NotFound(
                        $"Property {code} was not found.");
                case DataverseErrorKind.ConcurrencyConflict:
                    return Outcome<PropertyDto>.PreconditionFailed(
                        "The property was changed by someone else.");
                default:
                    throw;                                 // <3>
            }
        }

        var updated = await GetByCodeAsync(code, cancellationToken)
            .ConfigureAwait(false);
        return Outcome<PropertyDto>.Ok(updated!);
    }
    // </listing:property-update>

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Created property {Code}")]
    private static partial void LogCreated(ILogger logger, string code);
}
