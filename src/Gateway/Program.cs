using Fiap.Proxy.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Prometheus;
using Serilog;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

#if DEBUG
builder.Configuration.AddUserSecrets<Program>(optional: true, reloadOnChange: true);
#endif

builder.Services.AddProxyObservability(builder.Configuration, builder.Environment);

builder.Host.UseSerilog();

builder.Configuration.AddSystemsManager(source =>
{
    source.Path = "/myapp/prod/";
    source.Optional = true;
    source.ReloadAfter = TimeSpan.FromMinutes(5);
});

builder.Services.AddHealthChecks();
builder.Services.AddControllers();
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
    options.AddPolicy("AllowAllOrigins", p =>
        p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

var jwtSettings = builder.Configuration["JwtSettings:SecretKey"];
builder.Services.AddApplicationInsightsTelemetry(builder.Configuration["ApplicationInsights:InstrumentationKey"]);

if (!string.IsNullOrEmpty(jwtSettings))
{
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings))
        };
    });
}
else
{
    builder.Services.AddAuthentication();
}

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminPolicy", policy => policy
        .RequireRole("Admin")
        .RequireAuthenticatedUser());

    options.AddPolicy("UserOrAdminPolicy", p =>
        p.RequireAuthenticatedUser().RequireRole("User", "Admin"));

    options.AddPolicy("UserPolicy", policy => policy
        .RequireRole("User")
        .RequireAuthenticatedUser());
});

builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("customPolicy", opt =>
    {
        opt.PermitLimit = 5;
        opt.Window = TimeSpan.FromSeconds(12);
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = 2;
    });
});

var app = builder.Build();

app.UseRouting();

app.UseCors("AllowAllOrigins");

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Gateway API v1");
});

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks("/health");

app.MapGet("/health", () => "Gateway is running");

app.UseHttpMetrics();
app.MapMetrics();

app.MapReverseProxy();

app.Run();
