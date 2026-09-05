using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace SaucyBot.Diagnostics;

public static class TelemetryServiceRegistration
{
    public static IServiceCollection AddSaucyBotTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("OpenTelemetry").Get<TelemetryOptions>() ?? new();
        services.AddSingleton<ISaucyBotMetrics, SaucyBotMetrics>();

        if (!options.Enabled)
        {
            return services;
        }

        var builder = services
            .AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(options.ServiceName))
            .WithMetrics(metrics => metrics
                .AddMeter(SaucyBotMetrics.MeterName)
                .AddRuntimeInstrumentation()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter((exporter, reader) => ConfigureExporter(exporter, reader, options)));

        if (options.Tracing.Enabled)
        {
            builder.WithTracing(tracing => tracing
                .AddHttpClientInstrumentation()
                .SetSampler(new TraceIdRatioBasedSampler(Math.Clamp(options.Tracing.SamplingRatio, 0, 1)))
                .AddOtlpExporter(exporter => ConfigureExporter(exporter, options)));
        }

        return services;
    }

    private static void ConfigureExporter(OtlpExporterOptions exporter, MetricReaderOptions reader, TelemetryOptions options)
    {
        ConfigureExporter(exporter, options);
        reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = options.ExportIntervalMilliseconds;
    }

    private static void ConfigureExporter(OtlpExporterOptions exporter, TelemetryOptions options)
    {
        exporter.Endpoint = new Uri(options.OtlpEndpoint);
        exporter.Protocol = Enum.Parse<OtlpExportProtocol>(options.OtlpProtocol, ignoreCase: true);
        exporter.Headers = options.OtlpHeaders;
    }
}
