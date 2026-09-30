using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using Sync.Common.Kafka;

namespace Sync.Common.Telemetry;

/// <summary>
/// Tracing architecture (§13, plan phase 11). One W3C trace follows an assessment from the device through
/// the gateway, into Kafka headers, through the persister and orchestrator, and back out on pull:
///
///   device (traceparent header) → gateway span → Kafka header "traceparent" → persister span
///   → outbox headers → wound-events.persisted → orchestrator span → Recommendation Service HTTP span
///
/// Each service creates spans from its own <see cref="ActivitySource"/> below. Exporting (OpenTelemetry
/// SDK → OTLP → otel-collector → Prometheus/Grafana) is wired in plan phase 11; until then the sources
/// cost nothing because no listener is attached.
/// </summary>
public static class SyncTelemetry
{
    public static readonly ActivitySource Gateway = new("MelaninWoundCdss.SyncGateway");
    public static readonly ActivitySource Persister = new("MelaninWoundCdss.IngestPersister");
    public static readonly ActivitySource Relay = new("MelaninWoundCdss.OutboxRelay");
    public static readonly ActivitySource Orchestrator = new("MelaninWoundCdss.Orchestrator");

    /// <summary>Copies the current trace context into outgoing Kafka headers.</summary>
    public static void Inject(Headers headers)
    {
        if (Activity.Current?.Id is { } id)
            headers.Add(HeaderNames.TraceParent, Encoding.UTF8.GetBytes(id));
    }

    /// <summary>Starts a consumer span that continues the trace carried in the message headers.</summary>
    public static Activity? StartConsume(ActivitySource source, string name, Headers? headers)
    {
        var parent = headers.GetHeader(HeaderNames.TraceParent);
        return parent is not null && ActivityContext.TryParse(parent, null, out var context)
            ? source.StartActivity(name, ActivityKind.Consumer, context)
            : source.StartActivity(name, ActivityKind.Consumer);
    }
}
