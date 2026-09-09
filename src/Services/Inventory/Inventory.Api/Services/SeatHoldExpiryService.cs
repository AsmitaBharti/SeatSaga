using Inventory.Api.Data;
using Inventory.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

/// <summary>
/// Periodically sweeps Held seats whose HoldExpiresAt has passed and returns them
/// to Available. Runs as a background hosted service rather than relying on a client
/// (or the Booking Service) to remember to release an abandoned hold.
/// </summary>
public class SeatHoldExpiryService : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SeatHoldExpiryService> _logger;

    public SeatHoldExpiryService(IServiceScopeFactory scopeFactory, ILogger<SeatHoldExpiryService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReleaseExpiredHoldsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while sweeping expired seat holds");
            }

            await Task.Delay(SweepInterval, stoppingToken);
        }
    }

    private async Task ReleaseExpiredHoldsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var now = DateTime.UtcNow;
        var expired = await db.Seats
            .Where(s => s.Status == SeatStatus.Held && s.HoldExpiresAt != null && s.HoldExpiresAt < now)
            .ToListAsync(ct);

        if (expired.Count == 0)
            return;

        foreach (var seat in expired)
        {
            seat.Status = SeatStatus.Available;
            seat.HeldByBookingId = null;
            seat.HoldExpiresAt = null;
        }

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Released {Count} expired seat hold(s)", expired.Count);
    }
}
