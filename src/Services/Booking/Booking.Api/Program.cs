using Booking.Api.Clients;
using Booking.Api.Data;
using Booking.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using System.Net;
using OpenTelemetry.Trace;
using OpenTelemetry.Resources;

var builder = WebApplication.CreateBuilder(args);

// ---------- Configuration ----------
var connectionString = builder.Configuration.GetConnectionString("BookingDb");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:BookingDb is missing or empty. " +
        "Check ASPNETCORE_ENVIRONMENT=Development is set, or ConnectionStrings__BookingDb is set (Docker).");
}

var inventoryBaseUrl = builder.Configuration["Services:InventoryBaseUrl"]
    ?? throw new InvalidOperationException("Missing Services:InventoryBaseUrl");
var paymentBaseUrl = builder.Configuration["Services:PaymentBaseUrl"]
    ?? throw new InvalidOperationException("Missing Services:PaymentBaseUrl");

// ---------- EF Core ----------
builder.Services.AddDbContext<BookingDbContext>(options =>
    options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(3)));

// ---------- Inventory client ----------
// Read-ish/state-check calls: tolerate more retries, seat holds are naturally
// idempotent from the caller's point of view (LostRace is a valid, safe outcome).
builder.Services.AddHttpClient<IInventoryClient, InventoryClient>(client =>
{
    client.BaseAddress = new Uri(inventoryBaseUrl);
    client.Timeout = Timeout.InfiniteTimeSpan; // let the resilience pipeline own timeouts
})
.AddResilienceHandler("inventory-pipeline", pipeline =>
{
    pipeline
        .AddTimeout(TimeSpan.FromSeconds(2))
        .AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            ShouldHandle = args => ValueTask.FromResult(
                args.Outcome.Result?.StatusCode is HttpStatusCode.RequestTimeout
                    or HttpStatusCode.TooManyRequests
                    or >= HttpStatusCode.InternalServerError
                || args.Outcome.Exception is HttpRequestException)
        })
        .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            SamplingDuration = TimeSpan.FromSeconds(30),
            MinimumThroughput = 10,
            BreakDuration = TimeSpan.FromSeconds(15)
        })
        .AddTimeout(TimeSpan.FromSeconds(8));
});

// ---------- Payment client ----------
// Money-moving call: fewer retries (never blindly retry a charge past what the
// idempotency key protects against), and the circuit breaker trips faster since
// a flaky/absent payment gateway should stop being hammered quickly.
builder.Services.AddHttpClient<IPaymentClient, PaymentClient>(client =>
{
    client.BaseAddress = new Uri(paymentBaseUrl);
    client.Timeout = Timeout.InfiniteTimeSpan;
})
.AddResilienceHandler("payment-pipeline", pipeline =>
{
    pipeline
        .AddTimeout(TimeSpan.FromSeconds(2))
        .AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 2,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            ShouldHandle = args => ValueTask.FromResult(
                args.Outcome.Result?.StatusCode is HttpStatusCode.RequestTimeout
                    or HttpStatusCode.TooManyRequests
                    or >= HttpStatusCode.InternalServerError
                || args.Outcome.Exception is HttpRequestException)
        })
        .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            FailureRatio = 0.3,
            SamplingDuration = TimeSpan.FromSeconds(30),
            MinimumThroughput = 5,
            BreakDuration = TimeSpan.FromSeconds(20)
        })
        .AddTimeout(TimeSpan.FromSeconds(6));
});

// ---------- Business logic ----------
builder.Services.AddScoped<IBookingOrchestrator, BookingOrchestrator>();

// ---------- Controllers / Swagger ----------
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c => c.SwaggerDoc("v1", new() { Title = "Booking Service", Version = "v1" }));

// ---------- Health checks ----------
builder.Services.AddHealthChecks()
    .AddSqlServer(connectionString, name: "booking-db");

// ---------- OpenTelemetry ----------
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(serviceName: "booking-service"))
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation(o => o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health"))
            .AddHttpClientInstrumentation() // this is what makes retry attempts visible as spans
            .AddSqlClientInstrumentation();

        var otlpEndpoint = builder.Configuration["Otel:Endpoint"];
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
        }
    });

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.MapControllers();
app.MapHealthChecks("/health");

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
    await db.Database.MigrateAsync();
}

app.Run();
