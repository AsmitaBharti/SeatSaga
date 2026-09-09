using Inventory.Api.Data;
using Inventory.Api.Dtos;
using Inventory.Api.Models;
using Inventory.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Inventory.Api.Tests;

public class SeatServiceTests
{
    /// <summary>
    /// Creates a fresh InventoryDbContext pointed at a named in-memory database.
    /// Passing the same <paramref name="dbName"/> to multiple contexts makes them
    /// share the same underlying store — which is what lets us simulate two
    /// independent "requests" (each normally gets its own scoped DbContext)
    /// racing for the same seat.
    /// </summary>
    private static InventoryDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new InventoryDbContext(options);
    }

    private static async Task<int> SeedAvailableSeatAsync(string dbName)
    {
        await using var db = CreateContext(dbName);
        var evt = new EventEntity { Name = "Test Event", VenueName = "Test Venue", StartsAt = DateTime.UtcNow.AddDays(30), BasePrice = 50m };
        evt.Seats.Add(new Seat { Row = 0, Col = 0, Status = SeatStatus.Available });
        db.Events.Add(evt);
        await db.SaveChangesAsync();
        return evt.Seats[0].Id;
    }

    [Fact]
    public async Task HoldAsync_ReturnsSuccess_WhenSeatIsAvailable()
    {
        var dbName = Guid.NewGuid().ToString();
        var seatId = await SeedAvailableSeatAsync(dbName);

        await using var db = CreateContext(dbName);
        var service = new SeatService(db, NullLogger<SeatService>.Instance);

        var result = await service.HoldAsync(seatId, new HoldSeatRequest(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(HoldOutcome.Success, result.Result);
        Assert.NotNull(result.HoldExpiresAt);
    }

    [Fact]
    public async Task HoldAsync_ReturnsNotFound_WhenSeatDoesNotExist()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var db = CreateContext(dbName); // empty store, no seed

        var service = new SeatService(db, NullLogger<SeatService>.Instance);
        var result = await service.HoldAsync(999, new HoldSeatRequest(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(HoldOutcome.NotFound, result.Result);
    }

    [Fact]
    public async Task HoldAsync_ReturnsUnavailable_WhenSeatAlreadyHeld()
    {
        var dbName = Guid.NewGuid().ToString();
        var seatId = await SeedAvailableSeatAsync(dbName);

        await using var firstCallDb = CreateContext(dbName);
        var firstService = new SeatService(firstCallDb, NullLogger<SeatService>.Instance);
        var first = await firstService.HoldAsync(seatId, new HoldSeatRequest(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);
        Assert.Equal(HoldOutcome.Success, first.Result);

        // A fresh context/service simulates a second, independent HTTP request
        // arriving after the seat is already Held (not racing — just late).
        await using var secondCallDb = CreateContext(dbName);
        var secondService = new SeatService(secondCallDb, NullLogger<SeatService>.Instance);
        var second = await secondService.HoldAsync(seatId, new HoldSeatRequest(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(HoldOutcome.Unavailable, second.Result);
    }

    /// <summary>
    /// The centerpiece test: simulates two requests racing for the same seat.
    ///
    /// dbContextA "reads" the seat first (priming its change tracker with the
    /// original RowVersion) but is the SLOWER caller — dbContextB reads afterward
    /// but commits its hold first. When A's service call finally does its own
    /// read, EF Core's identity map hands back A's already-tracked (now stale)
    /// entity instead of re-querying the store, so A's SaveChanges is issued
    /// against an outdated RowVersion and EF Core throws
    /// DbUpdateConcurrencyException — which SeatService converts into LostRace.
    ///
    /// This proves the guarantee the whole demo is built around: two concurrent
    /// holds on one seat never both succeed, with no application-level lock.
    /// </summary>
    [Fact]
    public async Task HoldAsync_OnlyOneWinner_WhenTwoRequestsRaceForSameSeat()
    {
        var dbName = Guid.NewGuid().ToString();
        var seatId = await SeedAvailableSeatAsync(dbName);

        await using var dbContextA = CreateContext(dbName);
        await using var dbContextB = CreateContext(dbName);

        // Prime dbContextA's change tracker with the seat's original state,
        // simulating "A read the seat" before B ever acted.
        await dbContextA.Seats.FirstAsync(s => s.Id == seatId);

        var serviceA = new SeatService(dbContextA, NullLogger<SeatService>.Instance);
        var serviceB = new SeatService(dbContextB, NullLogger<SeatService>.Instance);

        // B commits its hold first.
        var resultB = await serviceB.HoldAsync(seatId, new HoldSeatRequest(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        // A now attempts its hold against its stale tracked entity.
        var resultA = await serviceA.HoldAsync(seatId, new HoldSeatRequest(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(HoldOutcome.Success, resultB.Result);
        Assert.Equal(HoldOutcome.LostRace, resultA.Result);

        // Exactly one Success across both attempts — never zero, never two.
        var outcomes = new[] { resultA.Result, resultB.Result };
        Assert.Single(outcomes, o => o == HoldOutcome.Success);
    }

    [Fact]
    public async Task ReleaseAsync_ReturnsNotHeldByThisBooking_WhenBookingIdDoesNotMatch()
    {
        var dbName = Guid.NewGuid().ToString();
        var seatId = await SeedAvailableSeatAsync(dbName);
        var holdingBookingId = Guid.NewGuid();

        await using var holdDb = CreateContext(dbName);
        await new SeatService(holdDb, NullLogger<SeatService>.Instance)
            .HoldAsync(seatId, new HoldSeatRequest(holdingBookingId, Guid.NewGuid()), CancellationToken.None);

        await using var releaseDb = CreateContext(dbName);
        var result = await new SeatService(releaseDb, NullLogger<SeatService>.Instance)
            .ReleaseAsync(seatId, Guid.NewGuid(), CancellationToken.None); // wrong bookingId

        Assert.Equal(SeatOperationResult.NotHeldByThisBooking, result);
    }

    [Fact]
    public async Task ConfirmAsync_TransitionsHeldSeatToSold()
    {
        var dbName = Guid.NewGuid().ToString();
        var seatId = await SeedAvailableSeatAsync(dbName);
        var bookingId = Guid.NewGuid();

        await using var holdDb = CreateContext(dbName);
        await new SeatService(holdDb, NullLogger<SeatService>.Instance)
            .HoldAsync(seatId, new HoldSeatRequest(bookingId, Guid.NewGuid()), CancellationToken.None);

        await using var confirmDb = CreateContext(dbName);
        var result = await new SeatService(confirmDb, NullLogger<SeatService>.Instance)
            .ConfirmAsync(seatId, bookingId, CancellationToken.None);

        Assert.Equal(SeatOperationResult.Success, result);

        await using var verifyDb = CreateContext(dbName);
        var seat = await verifyDb.Seats.FirstAsync(s => s.Id == seatId);
        Assert.Equal(SeatStatus.Sold, seat.Status);
    }
}
