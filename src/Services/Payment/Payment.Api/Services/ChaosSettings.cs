namespace Payment.Api.Services;

/// <summary>
/// Registered as a singleton so /admin/chaos can flip these values at runtime and
/// every subsequent charge request immediately picks up the new behavior — this is
/// how you demo Booking Service's retry/circuit-breaker pipeline live: set a high
/// failure rate here, then place a booking and watch Booking's logs/traces.
/// </summary>
public class ChaosSettings
{
    public int FailureRatePct { get; set; } = 0;
    public int LatencyMs { get; set; } = 0;
}
