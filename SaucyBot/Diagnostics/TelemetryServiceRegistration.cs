using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace SaucyBot.Diagnostics;

public static class TelemetryServiceRegistration
{
    public static IServiceCollection AddSaucyBotTelemetry(this IServiceCollection services, IOptions<TelemetryOptions> options)
    {
        var telemetry = options.Value;

        services.AddSingleton<ISaucyBotMetrics, SaucyBotMetrics>();

        if (!telemetry.Enabled)
        {
            return services;
        }

        var builder = services
            .AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(telemetry.ServiceName))
            .WithMetrics(metrics => metrics
                .AddMeter(SaucyBotMetrics.MeterName)
                .AddRuntimeInstrumentation()
                .AddProcessInstrumentation()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter((exporter, reader) => ConfigureExporter(exporter, reader, telemetry)));

        if (telemetry.Tracing.Enabled)
        {
            builder.WithTracing(tracing => tracing
                .AddSource(QueueTelemetry.ActivitySourceName)
                .AddHttpClientInstrumentation()
                .SetSampler(new TraceIdRatioBasedSampler(Math.Clamp(telemetry.Tracing.SamplingRatio, 0, 1)))
                .AddOtlpExporter(exporter => ConfigureExporter(exporter, telemetry)));
        }

        return services;
    }

    private static void ConfigureExporter(OtlpExporterOptions exporter, MetricReaderOptions reader, TelemetryOptions telemetry)
    {
        ConfigureExporter(exporter, telemetry);
        reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = telemetry.ExportIntervalMilliseconds;
    }

    private static void ConfigureExporter(OtlpExporterOptions exporter, TelemetryOptions telemetry)
    {
        exporter.Endpoint = new Uri(telemetry.OtlpEndpoint);
        exporter.Protocol = Enum.Parse<OtlpExportProtocol>(telemetry.OtlpProtocol, ignoreCase: true);
        exporter.Headers = telemetry.OtlpHeaders;
    }
}
