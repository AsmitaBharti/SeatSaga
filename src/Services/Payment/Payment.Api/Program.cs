using Payment.Api.Data;
using Payment.Api.Services;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("PaymentDb");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:PaymentDb is missing or empty. " +
        "Check ASPNETCORE_ENVIRONMENT=Development is set, or ConnectionStrings__PaymentDb is set (Docker).");
}

builder.Services.AddDbContext<PaymentDbContext>(options =>
    options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(3)));

builder.Services.AddSingleton<ChaosSettings>();
builder.Services.AddScoped<IPaymentProcessingService, PaymentProcessingService>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c => c.SwaggerDoc("v1", new() { Title = "Payment Service", Version = "v1" }));

builder.Services.AddHealthChecks()
    .AddSqlServer(connectionString, name: "payment-db");

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(serviceName: "payment-service"))
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
    var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
    await db.Database.MigrateAsync();
}

app.Run();
