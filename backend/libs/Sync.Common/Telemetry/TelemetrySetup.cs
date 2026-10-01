using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Sync.Common.Telemetry;

/// <summary>
/// OpenTelemetry for every service (architecture §13, plan phase 11): traces and metrics over OTLP to the collector
/// (infra/otel-collector), which sends traces to Jaeger and metrics to Prometheus → Grafana.
///
/// Exporting is on only when OTEL_EXPORTER_OTLP_ENDPOINT is set (docker-compose sets it), so a plain `dotnet run`
/// without the tools profile exports nothing. Web services add ASP.NET Core instrumentation on the returned builder.
/// </summary>
public static class TelemetrySetup
{
    public static OpenTelemetryBuilder AddSyncTelemetry(this IHostApplicationBuilder builder, string serviceName)
    {
        var export = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        return builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName, serviceNamespace: "melanin-wound-cdss"))
            .WithTracing(t =>
            {
                t.AddSource(SyncTelemetry.Gateway.Name, SyncTelemetry.Persister.Name, SyncTelemetry.Relay.Name,
                        SyncTelemetry.Orchestrator.Name, "Npgsql")
                    .AddHttpClientInstrumentation();
                if (export) t.AddOtlpExporter();
            })
            .WithMetrics(m =>
            {
                m.AddMeter(SyncMetrics.MeterName, "Npgsql")
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
                // Every 5 s instead of the default 60 s, so the dashboard follows an experiment as it runs.
                if (export)
                    m.AddOtlpExporter((_, reader) => reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 5000);
            });
    }
}
