using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Harbourline.Connect.Core.Telemetry;

// <listing:telemetry>
/// <summary>
/// One ActivitySource and one Meter for all Harbourline code. Spans and
/// metrics flow to Application Insights through OpenTelemetry
/// (Chapter 18).
/// </summary>
public static class HarbourlineTelemetry
{
    public const string Name = "Harbourline.Connect";

    public static readonly ActivitySource Source = new(Name);  // <1>

    private static readonly Meter Meter = new(Name);

    public static readonly Counter<long> RecordsWritten =
        Meter.CreateCounter<long>("harbourline.records.written",
            unit: "{record}",
            description: "Rows written to Dataverse");     // <2>

    public static readonly Counter<long> MessagesDeadLettered =
        Meter.CreateCounter<long>("harbourline.messages.deadlettered",
            unit: "{message}",
            description: "Messages moved to a dead-letter queue");

    public static readonly Histogram<double> BatchSeconds =
        Meter.CreateHistogram<double>("harbourline.batch.duration",
            unit: "s",
            description: "Time to write one Dataverse batch");
}
// </listing:telemetry>
