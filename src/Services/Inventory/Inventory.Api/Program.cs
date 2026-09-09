using Inventory.Api.Data;
using Inventory.Api.Llm;
using Inventory.Api.Services;
using Microsoft.EntityFrameworkCore;
using Polly;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// ---------- Configuration ----------
var connectionString = builder.Configuration.GetConnectionString("InventoryDb");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:InventoryDb is missing or empty. " +
        "Check that ASPNETCORE_ENVIRONMENT=Development is set so appsettings.Development.json is loaded, " +
        "or that the ConnectionStrings__InventoryDb environment variable is set (Docker).");
}

// ---------- EF Core ----------
builder.Services.AddDbContext<InventoryDbContext>(options =>
    options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(3)));

// ---------- Business logic services ----------
builder.Services.AddScoped<ISeatService, SeatService>();

// ---------- LLM-backed seat concierge ----------
// Deliberately short timeout + few retries + fast-tripping circuit breaker:
// this is a "nice to have" UX feature, not a critical path, so failures here
// should degrade gracefully (see SeatCriteriaParser's fallback) rather than
// ever blocking a booking-critical call.
builder.Services.AddHttpClient<ILlmClient, AnthropicLlmClient>(client =>
{
    var baseUrl = builder.Configuration["Llm:BaseUrl"] ?? "https://api.anthropic.com/";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = Timeout.InfiniteTimeSpan;

    var apiKey = builder.Configuration["Llm:ApiKey"];
    if (!string.IsNullOrWhiteSpace(apiKey))
    {
        client.DefaultRequestHeaders.Add("x-api-key", apiKey);
        client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
    }
})
.AddResilienceHandler("llm-pipeline", pipeline =>
{
    pipeline
        .AddTimeout(TimeSpan.FromSeconds(5))
        .AddRetry(new Microsoft.Extensions.Http.Resilience.HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 1,
            BackoffType = Polly.DelayBackoffType.Constant
        })
        .AddCircuitBreaker(new Microsoft.Extensions.Http.Resilience.HttpCircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            SamplingDuration = TimeSpan.FromSeconds(30),
            MinimumThroughput = 5,
            BreakDuration = TimeSpan.FromSeconds(10)
        })
        .AddTimeout(TimeSpan.FromSeconds(10));
});

builder.Services.AddScoped<ISeatCriteriaParser, SeatCriteriaParser>();
builder.Services.AddScoped<ISeatRecommendationService, SeatRecommendationService>();

// ---------- Background services ----------
builder.Services.AddHostedService<SeatHoldExpiryService>();

// ---------- Controllers / Swagger ----------
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Inventory Service", Version = "v1" });
});

// ---------- Health checks ----------
builder.Services.AddHealthChecks()
    .AddSqlServer(connectionString, name: "inventory-db");

// ---------- OpenTelemetry ----------
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(serviceName: "inventory-service"))
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation(o => o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health"))
            .AddSqlClientInstrumentation();

        var otlpEndpoint = builder.Configuration["Otel:Endpoint"];
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
        }
    });

var app = builder.Build();

// ---------- Middleware pipeline ----------
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.MapControllers();
app.MapHealthChecks("/health");

// ---------- Apply migrations on startup (dev convenience; a real pipeline would
// run migrations as a separate release step) ----------
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    await db.Database.MigrateAsync();
}

app.Run();
