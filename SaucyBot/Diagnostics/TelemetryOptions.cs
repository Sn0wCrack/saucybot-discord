namespace SaucyBot.Diagnostics;

public sealed class TelemetryOptions
{
    public bool Enabled { get; set; }

    public string ServiceName { get; set; } = "SaucyBot";

    public string OtlpEndpoint { get; set; } = "http://localhost:4318";

    public string OtlpProtocol { get; set; } = "HttpProtobuf";

    public string? OtlpHeaders { get; set; }

    public int ExportIntervalMilliseconds { get; set; } = 10000;

    public TracingOptions Tracing { get; set; } = new();

    public sealed class TracingOptions
    {
        public bool Enabled { get; set; }

        public double SamplingRatio { get; set; } = 0.1;
    }
}
