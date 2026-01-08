using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Sinks.Grafana.Loki;

namespace Fiap.Proxy.Extensions;

public static class ObservabilityExtensions
{
    public static IServiceCollection AddProxyObservability(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        ConfigureProxySerilog(services, configuration, environment);
        ConfigureProxyOpenTelemetry(services, configuration);

        var appInsightsKey = configuration["ApplicationInsights:InstrumentationKey"];
        if (!string.IsNullOrEmpty(appInsightsKey))
        {
            services.AddApplicationInsightsTelemetry(options =>
            {
                options.ConnectionString = $"InstrumentationKey={appInsightsKey}";
            });
        }

        return services;
    }

    private static void ConfigureProxySerilog(IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        var lokiUri = configuration["ProxyObservability:Serilog:WriteTo:1:Args:Uri"];

        var loggerConfig = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration.GetSection("ProxyObservability:Serilog"))
            .Enrich.FromLogContext()
            .Enrich.WithEnvironmentName()
            .Enrich.WithMachineName()
            .Enrich.WithProcessId()
            .Enrich.WithProperty("Application", "Proxy")
            .WriteTo.Console();

        if (!string.IsNullOrEmpty(lokiUri))
        {
            loggerConfig.WriteTo.GrafanaLoki(
                uri: lokiUri,
                labels:
                [
                    new() { Key = "app-proxy", Value = "dotnet-api" },
                    new() { Key = "env", Value = environment.EnvironmentName }
                ]);
        }

        Log.Logger = loggerConfig.CreateLogger();
    }

    private static void ConfigureProxyOpenTelemetry(IServiceCollection services, IConfiguration configuration)
    {
        var serviceName = configuration["ProxyObservability:DistributedTracing:Jaeger:ServiceName"];
        var endpoint = configuration["ProxyObservability:DistributedTracing:Jaeger:Endpoint"];
        var host = configuration["ProxyObservability:DistributedTracing:Jaeger:Host"];
        var portStr = configuration["ProxyObservability:DistributedTracing:Jaeger:Port"];
        var port = int.TryParse(portStr, out var p) ? p : 6831;

        services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing
                    .SetResourceBuilder(ResourceBuilder.CreateDefault()
                        .AddService(serviceName: serviceName ?? "Proxy", serviceVersion: "1.0.0"))
                    .AddAspNetCoreInstrumentation();

                // Só adiciona Jaeger se tiver endpoint ou host configurado
                if (!string.IsNullOrEmpty(endpoint) || !string.IsNullOrEmpty(host))
                {
                    tracing.AddJaegerExporter(options =>
                    {
                        if (!string.IsNullOrEmpty(endpoint))
                        {
                            options.Endpoint = new Uri(endpoint);
                            options.Protocol = JaegerExportProtocol.HttpBinaryThrift;
                        }
                        else
                        {
                            options.AgentHost = host;
                            options.AgentPort = port;
                        }
                    });
                }
            });
    }
}
