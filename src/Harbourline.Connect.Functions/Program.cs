using Harbourline.Connect.Core;
using Harbourline.Connect.Functions;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Hosting;

// <listing:program>
var builder = FunctionsApplication.CreateBuilder(args);     // <1>

// ASP.NET Core integration: HttpRequest and IActionResult.
builder.ConfigureFunctionsWebApplication();                 // <2>

// Options, Dataverse client and shared services.
builder.Services.AddHarbourlineCore(builder.Configuration); // <3>

// Integrations, messaging and telemetry (Chapters 8 to 24).
builder.AddHarbourlineFunctions();

builder.Build().Run();                                      // <4>
// </listing:program>
