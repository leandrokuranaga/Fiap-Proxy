using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, config) =>
    config.WriteTo.Console(new JsonFormatter()));

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .SetResourceBuilder(ResourceBuilder.CreateDefault()
            .AddService("PROXY"))
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation() 
        .AddJaegerExporter(options =>
        {
            options.AgentHost = "localhost"; 
            options.AgentPort = 6831;
        }));

builder.Services.AddHealthChecks();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo 
    { 
        Title = "Gateway API", 
        Version = "v1",
        Description = "API Gateway com redirecionamento",
    });
});

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAllOrigins", builder =>
    {
        builder.AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();
app.UseCors("AllowAllOrigins");

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Gateway API v1");
});

app.MapHealthChecks("/health");
app.MapGet("/health", () => "Gateway is running");
app.MapReverseProxy();

app.MapMetrics();

app.Run();