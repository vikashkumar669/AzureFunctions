using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Harbourline.Connect.Core.Dataverse;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

// Runs one question three ways against a developer environment:
// "available properties in Leeds with two or more bedrooms".

var url = args.FirstOrDefault()
    ?? Environment.GetEnvironmentVariable("Dataverse__Url")
    ?? throw new InvalidOperationException("Pass the environment URL.");
var credential = new DefaultAzureCredential();
using var client = DataverseClientFactory.Create(
    new DataverseOptions { Url = url }, credential, NullLogger.Instance);

Print("QueryExpression", await ByQueryExpression(client));
Print("FetchXML", await ByFetchXml(client));
Print("Web API (OData)", await ByWebApi(url, credential));

// <listing:probe-qe>
static async Task<List<string>> ByQueryExpression(
    IOrganizationServiceAsync2 service)
{
    var query = new QueryExpression("hl_property")
    {
        ColumnSet = new ColumnSet("hl_name", "hl_bedrooms"),   // <1>
        TopCount = 10,
    };
    query.Criteria.AddCondition("hl_city",
        ConditionOperator.Equal, "Leeds");
    query.Criteria.AddCondition("hl_bedrooms",
        ConditionOperator.GreaterEqual, 2);
    query.Criteria.AddCondition("hl_status",
        ConditionOperator.Equal, 100_000_000);             // <2>
    query.AddOrder("hl_name", OrderType.Ascending);

    var rows = await service.RetrieveMultipleAsync(query);
    return rows.Entities.Select(Describe).ToList();
}
// </listing:probe-qe>

// <listing:probe-fetch>
static async Task<List<string>> ByFetchXml(
    IOrganizationServiceAsync2 service)
{
    const string fetch = """
        <fetch top="10">
          <entity name="hl_property">
            <attribute name="hl_name" />
            <attribute name="hl_bedrooms" />
            <filter>
              <condition attribute="hl_city" operator="eq"
                         value="Leeds" />
              <condition attribute="hl_bedrooms" operator="ge"
                         value="2" />
              <condition attribute="hl_status" operator="eq"
                         value="100000000" />
            </filter>
            <order attribute="hl_name" />
          </entity>
        </fetch>
        """;                                               // <1>

    var rows = await service.RetrieveMultipleAsync(
        new FetchExpression(fetch));                       // <2>
    return rows.Entities.Select(Describe).ToList();
}
// </listing:probe-fetch>

// <listing:probe-webapi>
static async Task<List<string>> ByWebApi(
    string url, TokenCredential credential)
{
    var org = new Uri(url);
    var token = await credential.GetTokenAsync(
        new TokenRequestContext([org.GetLeftPart(
            UriPartial.Authority) + "/.default"]),
        CancellationToken.None);

    using var http = new HttpClient
    {
        BaseAddress = new Uri(org, "/api/data/v9.2/"),     // <1>
    };
    http.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", token.Token);
    http.DefaultRequestHeaders.Add("Prefer",
        "odata.include-annotations=\"*\"");                // <2>

    var json = await http.GetStringAsync(new Uri(
        "hl_properties?$select=hl_name,hl_bedrooms" +
        "&$filter=hl_city eq 'Leeds' and hl_bedrooms ge 2" +
        " and hl_status eq 100000000" +
        "&$orderby=hl_name&$top=10", UriKind.Relative));  // <3>

    using var doc = JsonDocument.Parse(json);
    return doc.RootElement.GetProperty("value").EnumerateArray()
        .Select(p => $"{p.GetProperty("hl_name").GetString()} " +
            $"({p.GetProperty("hl_bedrooms").GetInt32()} bed)")
        .ToList();
}
// </listing:probe-webapi>

static string Describe(Entity e) =>
    $"{e.GetAttributeValue<string>("hl_name")} " +
    $"({e.GetAttributeValue<int>("hl_bedrooms")} bed)";

static void Print(string title, List<string> rows)
{
    Console.WriteLine($"{title}: {rows.Count} rows");
    rows.ForEach(r => Console.WriteLine("  " + r));
}
