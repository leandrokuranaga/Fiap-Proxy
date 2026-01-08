using Amazon;
using Amazon.CloudWatch;
using Amazon.CloudWatch.Model;
using Amazon.CloudWatchLogs;
using Amazon.XRay.Recorder.Core;
using Amazon.XRay.Recorder.Handlers.AwsSdk;
using Fiap.Proxy.Utils;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Sinks.AwsCloudWatch;
using Serilog.Sinks.Grafana.Loki;
using System.Diagnostics;
using StandardUnit = Amazon.CloudWatch.StandardUnit;

namespace Fiap.Proxy.Extensions;

public static class ObservabilityExtensions
{
    public static IServiceCollection AddProxyObservability(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        ConfigureProxySerilog(services, configuration, environment);
        ConfigureProxyOpenTelemetry(services, configuration);
        return services;
    }

    private static void ConfigureProxySerilog(IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        services.AddOptions<Utils.Serilog>()
            .Bind(configuration.GetSection(nameof(Utils.Serilog)))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var lokiUri = configuration["ProxyObservability:Serilog:WriteTo:1:Args:Uri"];

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration.GetSection("ProxyObservability:Serilog"))
            .Enrich.FromLogContext()
            .Enrich.WithEnvironmentName()
            .Enrich.WithMachineName()
            .Enrich.WithProcessId()
            .Enrich.WithProperty("Application", "Proxy")
            .WriteTo.Console()
            .WriteTo.GrafanaLoki(
                uri: lokiUri,
                labels:
                [
                    new() { Key = "app-proxy", Value = "dotnet-api" },
                    new() { Key = "env", Value = environment.EnvironmentName }
                ])
            .CreateLogger();
    }

    private static void ConfigureProxyOpenTelemetry(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DistributedTracing>()
            .Bind(configuration.GetSection(nameof(DistributedTracing)))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var serviceName = configuration["DistributedTracing:Jaeger:ServiceName"];
        var endpoint = configuration["DistributedTracing:Jaeger:Endpoint"];
        var host = configuration["DistributedTracing:Jaeger:Host"];
        var port = int.Parse(configuration["DistributedTracing:Jaeger:Port"]);

        services.AddOpenTelemetry()
            .WithTracing(tracing => tracing
                .SetResourceBuilder(ResourceBuilder.CreateDefault()
                    .AddService(serviceName: serviceName, serviceVersion: "1.0.0"))
                .AddAspNetCoreInstrumentation()
                .AddEntityFrameworkCoreInstrumentation(options =>
                {
                    options.SetDbStatementForText = true;
                    options.SetDbStatementForStoredProcedure = true;
                })
                .AddJaegerExporter(options =>
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
                }));
    }

    /// <summary>
    /// Configura observabilidade usando AWS CloudWatch (Logs + Metrics) e X-Ray (Tracing)
    /// - Logs: SDK direto via API (sem collector)
    /// - Metrics: SDK direto via API (sem collector)
    /// - X-Ray: Requer X-Ray Daemon rodando no cluster (DaemonSet) - env: AWS_XRAY_DAEMON_ADDRESS
    /// </summary>
    public static IServiceCollection AddAwsObservability(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        var awsConfig = configuration.GetSection("AWS:Observability");
        var serviceName = awsConfig["ServiceName"] ?? "FiapProxy";
        var region = awsConfig["Region"] ?? "sa-east-1";

        ConfigureAwsCloudWatchLogs(configuration, environment, serviceName, region);
        ConfigureAwsXRayTracing(services, configuration, serviceName, region);
        ConfigureAwsCloudWatchMetrics(services, configuration, serviceName, region);

        return services;
    }

    /// <summary>
    /// Configura middleware do X-Ray no pipeline (chamar no Program.cs após builder.Build())
    /// </summary>
    public static IApplicationBuilder UseAwsXRay(this IApplicationBuilder app, string serviceName)
    {
        app.UseXRay(serviceName);
        return app;
    }

    private static void ConfigureAwsCloudWatchLogs(IConfiguration configuration, IWebHostEnvironment environment, string serviceName, string region)
    {
        var logsEnabled = configuration.GetValue<bool>("AWS:Observability:CloudWatch:Logs:Enabled");
        if (!logsEnabled) return;

        var logGroup = configuration["AWS:Observability:CloudWatch:Logs:LogGroup"] ?? "/fiap/proxy";
        var logStreamPrefix = configuration["AWS:Observability:CloudWatch:Logs:LogStreamPrefix"] ?? "proxy-";
        var streamName = $"{logStreamPrefix}{environment.EnvironmentName}-{DateTime.UtcNow:yyyyMMdd}";

        var accessKey = configuration["AWS:Credentials:AccessKey"]
            ?? Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID")
            ?? Environment.GetEnvironmentVariable("AWS_SQS_ACCESS_KEY");
        var secretKey = configuration["AWS:Credentials:SecretKey"]
            ?? Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY")
            ?? Environment.GetEnvironmentVariable("AWS_SQS_SECRET_KEY");

        try
        {
            AmazonCloudWatchLogsClient client;

            var localStackEnabled = configuration.GetValue<bool>("AWS:Observability:LocalStack:Enabled");
            var localStackUrl = configuration["AWS:Observability:LocalStack:ServiceUrl"] ?? "http://localhost:4566";

            if (!string.IsNullOrEmpty(accessKey) && !string.IsNullOrEmpty(secretKey))
            {
                var credentials = new Amazon.Runtime.BasicAWSCredentials(accessKey, secretKey);

                if (localStackEnabled)
                {
                    var config = new AmazonCloudWatchLogsConfig
                    {
                        ServiceURL = localStackUrl,
                        UseHttp = true
                    };
                    client = new AmazonCloudWatchLogsClient(credentials, config);
                }
                else
                {
                    client = new AmazonCloudWatchLogsClient(credentials, RegionEndpoint.GetBySystemName(region));
                }
            }
            else
            {
                client = new AmazonCloudWatchLogsClient(RegionEndpoint.GetBySystemName(region));
            }

            // Tenta criar o log group (ignora se já existe)
            try
            {
                client.CreateLogGroupAsync(new Amazon.CloudWatchLogs.Model.CreateLogGroupRequest
                {
                    LogGroupName = logGroup
                }).GetAwaiter().GetResult();
            }
            catch (Amazon.CloudWatchLogs.Model.ResourceAlreadyExistsException) { }

            var options = new CloudWatchSinkOptions
            {
                LogGroupName = logGroup,
                LogStreamNameProvider = new ConstantLogStreamNameProvider(streamName),
                TextFormatter = new Serilog.Formatting.Json.JsonFormatter(),
                MinimumLogEventLevel = Serilog.Events.LogEventLevel.Information,
                BatchSizeLimit = 100,
                QueueSizeLimit = 10000,
                Period = TimeSpan.FromSeconds(10),
                CreateLogGroup = true,
                LogGroupRetentionPolicy = LogGroupRetentionPolicy.OneMonth
            };

            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration.GetSection("ProxyObservability:Serilog"))
                .Enrich.FromLogContext()
                .Enrich.WithEnvironmentName()
                .Enrich.WithMachineName()
                .Enrich.WithProcessId()
                .Enrich.WithProperty("Application", serviceName)
                .WriteTo.Console()
                .WriteTo.AmazonCloudWatch(options, client)
                .CreateLogger();
        }
        catch (Exception)
        {
            // Fallback: só console
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration.GetSection("ProxyObservability:Serilog"))
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .CreateLogger();
        }
    }

    private static void ConfigureAwsXRayTracing(IServiceCollection services, IConfiguration configuration, string serviceName, string region)
    {
        var xrayEnabled = configuration.GetValue<bool>("AWS:Observability:XRay:Enabled");
        if (!xrayEnabled) return;

        var daemonAddress = configuration["AWS:Observability:XRay:DaemonAddress"]
            ?? Environment.GetEnvironmentVariable("AWS_XRAY_DAEMON_ADDRESS")
            ?? "xray-daemon.fiap:2000";

        Environment.SetEnvironmentVariable("AWS_XRAY_DAEMON_ADDRESS", daemonAddress);

        AWSXRayRecorder.InitializeInstance(configuration);
        AWSSDKHandler.RegisterXRayForAllServices();
    }

    private static void ConfigureAwsCloudWatchMetrics(IServiceCollection services, IConfiguration configuration, string serviceName, string region)
    {
        var metricsEnabled = configuration.GetValue<bool>("AWS:Observability:CloudWatch:Metrics:Enabled");
        if (!metricsEnabled) return;

        var metricsNamespace = configuration["AWS:Observability:CloudWatch:Metrics:Namespace"] ?? "Fiap/Proxy";

        services.AddSingleton<IAmazonCloudWatch>(sp =>
            CreateCloudWatchClient(configuration, region));

        services.AddSingleton<ICloudWatchMetricsService>(sp =>
            new CloudWatchMetricsService(
                sp.GetRequiredService<IAmazonCloudWatch>(),
                metricsNamespace,
                serviceName));

        services.AddHostedService<CloudWatchMetricsCollector>();
    }

    private static AmazonCloudWatchClient CreateCloudWatchClient(IConfiguration configuration, string region)
    {
        var accessKey = configuration["AWS:Credentials:AccessKey"]
            ?? Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID")
            ?? Environment.GetEnvironmentVariable("AWS_SQS_ACCESS_KEY");
        var secretKey = configuration["AWS:Credentials:SecretKey"]
            ?? Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY")
            ?? Environment.GetEnvironmentVariable("AWS_SQS_SECRET_KEY");

        var localStackEnabled = configuration.GetValue<bool>("AWS:Observability:LocalStack:Enabled");
        var localStackUrl = configuration["AWS:Observability:LocalStack:ServiceUrl"] ?? "http://localhost:4566";

        if (!string.IsNullOrEmpty(accessKey) && !string.IsNullOrEmpty(secretKey))
        {
            var credentials = new Amazon.Runtime.BasicAWSCredentials(accessKey, secretKey);

            if (localStackEnabled)
            {
                var config = new AmazonCloudWatchConfig
                {
                    ServiceURL = localStackUrl,
                    UseHttp = true
                };
                return new AmazonCloudWatchClient(credentials, config);
            }

            return new AmazonCloudWatchClient(credentials, RegionEndpoint.GetBySystemName(region));
        }

        return new AmazonCloudWatchClient(RegionEndpoint.GetBySystemName(region));
    }
}

/// <summary>
/// Interface para enviar métricas customizadas ao CloudWatch
/// </summary>
public interface ICloudWatchMetricsService
{
    Task PutMetricAsync(string metricName, double value, StandardUnit unit, Dictionary<string, string>? dimensions = null);
    Task PutRequestMetricsAsync(string endpoint, double latencyMs, bool success);
}

/// <summary>
/// Implementação do serviço de métricas do CloudWatch
/// </summary>
public class CloudWatchMetricsService : ICloudWatchMetricsService
{
    private readonly IAmazonCloudWatch _cloudWatch;
    private readonly string _namespace;
    private readonly string _serviceName;

    public CloudWatchMetricsService(IAmazonCloudWatch cloudWatch, string metricsNamespace, string serviceName)
    {
        _cloudWatch = cloudWatch;
        _namespace = metricsNamespace;
        _serviceName = serviceName;
    }

    public async Task PutMetricAsync(string metricName, double value, StandardUnit unit, Dictionary<string, string>? dimensions = null)
    {
        var metricData = new MetricDatum
        {
            MetricName = metricName,
            Value = value,
            Unit = unit,
            Dimensions = new List<Dimension>
            {
                new() { Name = "ServiceName", Value = _serviceName }
            }
        };

        if (dimensions != null)
        {
            foreach (var dim in dimensions)
            {
                metricData.Dimensions.Add(new Dimension { Name = dim.Key, Value = dim.Value });
            }
        }

        await _cloudWatch.PutMetricDataAsync(new PutMetricDataRequest
        {
            Namespace = _namespace,
            MetricData = new List<MetricDatum> { metricData }
        });
    }

    public async Task PutRequestMetricsAsync(string endpoint, double latencyMs, bool success)
    {
        var dimensions = new Dictionary<string, string> { { "Endpoint", endpoint } };

        await PutMetricAsync("RequestLatency", latencyMs, StandardUnit.Milliseconds, dimensions);
        await PutMetricAsync("RequestCount", 1, StandardUnit.Count, dimensions);

        if (!success)
        {
            await PutMetricAsync("ErrorCount", 1, StandardUnit.Count, dimensions);
        }
    }
}

/// <summary>
/// Background service para coletar métricas do sistema periodicamente
/// </summary>
public class CloudWatchMetricsCollector : BackgroundService
{
    private readonly ICloudWatchMetricsService _metricsService;
    private readonly ILogger<CloudWatchMetricsCollector> _logger;
    private readonly Process _currentProcess;
    private bool _credentialsAvailable = true;

    public CloudWatchMetricsCollector(ICloudWatchMetricsService metricsService, ILogger<CloudWatchMetricsCollector> logger)
    {
        _metricsService = metricsService;
        _logger = logger;
        _currentProcess = Process.GetCurrentProcess();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_credentialsAvailable)
                {
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                    _credentialsAvailable = true;
                    continue;
                }

                _currentProcess.Refresh();

                await _metricsService.PutMetricAsync("MemoryUsedMB", _currentProcess.WorkingSet64 / 1024.0 / 1024.0, StandardUnit.Megabytes);
                await _metricsService.PutMetricAsync("ThreadCount", _currentProcess.Threads.Count, StandardUnit.Count);
                await _metricsService.PutMetricAsync("CpuTimeSeconds", _currentProcess.TotalProcessorTime.TotalSeconds, StandardUnit.Seconds);
                await _metricsService.PutMetricAsync("GCGen0Collections", GC.CollectionCount(0), StandardUnit.Count);
                await _metricsService.PutMetricAsync("GCGen1Collections", GC.CollectionCount(1), StandardUnit.Count);
                await _metricsService.PutMetricAsync("GCGen2Collections", GC.CollectionCount(2), StandardUnit.Count);
                await _metricsService.PutMetricAsync("GCHeapSizeMB", GC.GetTotalMemory(false) / 1024.0 / 1024.0, StandardUnit.Megabytes);

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Amazon.Runtime.AmazonClientException ex) when (ex.Message.Contains("credentials"))
            {
                _logger.LogWarning("Credenciais AWS não disponíveis para CloudWatch Metrics. Métricas desabilitadas temporariamente.");
                _credentialsAvailable = false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erro ao enviar métricas para CloudWatch");
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
