# OpenTelemetry Configuration

Copy the `SaucyBot/telemetry.example.json` section into the ignored runtime `appsettings.json` or provide the same values through environment variables. The application binds the `OpenTelemetry` section using these keys:

| Configuration key | Environment variable | Description |
| --- | --- | --- |
| `OpenTelemetry:Enabled` | `OpenTelemetry__Enabled` | Enables metrics and exporters. Defaults to `false`. |
| `OpenTelemetry:ServiceName` | `OpenTelemetry__ServiceName` | Resource service name. |
| `OpenTelemetry:OtlpEndpoint` | `OpenTelemetry__OtlpEndpoint` | OTLP exporter endpoint. |
| `OpenTelemetry:OtlpProtocol` | `OpenTelemetry__OtlpProtocol` | `Grpc` or `HttpProtobuf`. |
| `OpenTelemetry:OtlpHeaders` | `OpenTelemetry__OtlpHeaders` | Comma-separated OTLP headers, such as `api-key=secret`. |
| `OpenTelemetry:ExportIntervalMilliseconds` | `OpenTelemetry__ExportIntervalMilliseconds` | Periodic metrics export interval. |
| `OpenTelemetry:Tracing:Enabled` | `OpenTelemetry__Tracing__Enabled` | Enables sampled tracing. |
| `OpenTelemetry:Tracing:SamplingRatio` | `OpenTelemetry__Tracing__SamplingRatio` | Trace sampling ratio from `0` to `1`. |

Environment variables are loaded after JSON configuration by the default .NET host, so they override values from local appsettings files. Keep OTLP headers and other credentials in environment variables or user secrets, not in tracked files.

When telemetry is disabled, no OpenTelemetry providers or exporters are registered and no exporter endpoint is required.
