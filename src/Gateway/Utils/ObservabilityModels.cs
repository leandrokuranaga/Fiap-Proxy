
namespace Fiap.Proxy.Utils;

public class Serilog
{
    public List<SerilogWriteTo> WriteTo { get; set; } = [];
}

public class SerilogWriteTo
{
    public string Name { get; set; } = string.Empty;
    public SerilogArgs Args { get; set; } = new();
}

public class SerilogArgs
{
    public string? Uri { get; set; }
    public string? OutputTemplate { get; set; }
}

public class DistributedTracing
{
    public JaegerSettings Jaeger { get; set; } = new();
}

public class JaegerSettings
{
    public string ServiceName { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
}