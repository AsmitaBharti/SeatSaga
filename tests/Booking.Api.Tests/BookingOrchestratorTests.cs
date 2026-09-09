using Booking.Api.Clients;
using Booking.Api.Data;
using Booking.Api.Dtos;
using Booking.Api.Models;
using Booking.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Booking.Api.Tests;

public class BookingOrchestratorTests
{
    private static BookingDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<BookingDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new BookingDbContext(options);
    }

    private static PlaceBookingRequest SampleRequest(string idempotencyKey = "key-1") =>
        new(EventId: 1, SeatId: 42, UserId: Guid.NewGuid(), Amount: 49.99m, IdempotencyKey: idempotencyKey);

    [Fact]
    public async Task PlaceBookingAsync_HappyPath_EndsConfirmedWithTransactionId()
    {
        await using var db = CreateContext(Guid.NewGuid().ToString());

        var inventory = new Mock<IInventoryClient>();
        inventory.Setup(x => x.HoldSeatAsync(42, It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(InventoryHoldOutcome.Success);

        var payment = new Mock<IPaymentClient>();
        payment.Setup(x => x.ChargeAsync(It.IsAny<Guid>(), 49.99m, "key-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChargeResult(true, "txn-123", null));

        var orchestrator = new BookingOrchestrator(db, inventory.Object, payment.Object, NullLogger<BookingOrchestrator>.Instance);

        var result = await orchestrator.PlaceBookingAsync(SampleRequest(), CancellationToken.None);

        Assert.Equal(BookingStatus.Confirmed, result.Status);
        inventory.Verify(x => x.ConfirmSeatAsync(42, result.BookingId, It.IsAny<CancellationToken>()), Times.Once);
        inventory.Verify(x => x.ReleaseSeatAsync(It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PlaceBookingAsync_SeatUnavailable_FailsWithoutCallingPayment()
    {
        await using var db = CreateContext(Guid.NewGuid().ToString());

        var inventory = new Mock<IInventoryClient>();
        inventory.Setup(x => x.HoldSeatAsync(42, It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(InventoryHoldOutcome.LostRace);

        var payment = new Mock<IPaymentClient>();

        var orchestrator = new BookingOrchestrator(db, inventory.Object, payment.Object, NullLogger<BookingOrchestrator>.Instance);

        var result = await orchestrator.PlaceBookingAsync(SampleRequest(), CancellationToken.None);

        Assert.Equal(BookingStatus.Failed, result.Status);
        Assert.Contains("LostRace", result.FailureReason);

        // The saga must never attempt to charge a customer for a seat it doesn't hold.
        payment.Verify(x => x.ChargeAsync(It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PlaceBookingAsync_PaymentFails_CompensatesByReleasingSeat()
    {
        await using var db = CreateContext(Guid.NewGuid().ToString());

        var inventory = new Mock<IInventoryClient>();
        inventory.Setup(x => x.HoldSeatAsync(42, It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(InventoryHoldOutcome.Success);

        var payment = new Mock<IPaymentClient>();
        payment.Setup(x => x.ChargeAsync(It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChargeResult(false, null, "Payment service unreachable"));

        var orchestrator = new BookingOrchestrator(db, inventory.Object, payment.Object, NullLogger<BookingOrchestrator>.Instance);

        var result = await orchestrator.PlaceBookingAsync(SampleRequest(), CancellationToken.None);

        Assert.Equal(BookingStatus.Cancelled, result.Status);

        // This is the compensation guarantee: a failed charge must always result in
        // exactly one release call for the seat that was held.
        inventory.Verify(x => x.ReleaseSeatAsync(42, result.BookingId, It.IsAny<CancellationToken>()), Times.Once);
        inventory.Verify(x => x.ConfirmSeatAsync(It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PlaceBookingAsync_ConfirmFailsAfterPayment_RefundsAndCompensates()
    {
        await using var db = CreateContext(Guid.NewGuid().ToString());

        var inventory = new Mock<IInventoryClient>();
        inventory.Setup(x => x.HoldSeatAsync(42, It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(InventoryHoldOutcome.Success);
        inventory.Setup(x => x.ConfirmSeatAsync(42, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("simulated failure"));

        var payment = new Mock<IPaymentClient>();
        payment.Setup(x => x.ChargeAsync(It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChargeResult(true, "txn-999", null));

        var orchestrator = new BookingOrchestrator(db, inventory.Object, payment.Object, NullLogger<BookingOrchestrator>.Instance);

        var result = await orchestrator.PlaceBookingAsync(SampleRequest(), CancellationToken.None);

        // A charged customer must never end up without a seat AND without a refund.
        payment.Verify(x => x.RefundAsync("txn-999", It.IsAny<CancellationToken>()), Times.Once);
        inventory.Verify(x => x.ReleaseSeatAsync(42, result.BookingId, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(BookingStatus.Cancelled, result.Status);
    }

    [Fact]
    public async Task PlaceBookingAsync_IdempotentReplay_ReturnsExistingBookingWithoutRerunningSaga()
    {
        await using var db = CreateContext(Guid.NewGuid().ToString());

        var existing = new BookingEntity
        {
            EventId = 1,
            SeatId = 42,
            UserId = Guid.NewGuid(),
            Amount = 49.99m,
            IdempotencyKey = "key-1",
            Status = BookingStatus.Confirmed,
            PaymentTransactionId = "txn-original"
        };
        db.Bookings.Add(existing);
        await db.SaveChangesAsync();

        var inventory = new Mock<IInventoryClient>();
        var payment = new Mock<IPaymentClient>();

        var orchestrator = new BookingOrchestrator(db, inventory.Object, payment.Object, NullLogger<BookingOrchestrator>.Instance);

        var result = await orchestrator.PlaceBookingAsync(SampleRequest(), CancellationToken.None);

        Assert.Equal(existing.Id, result.BookingId);
        Assert.Equal(BookingStatus.Confirmed, result.Status);

        // The whole point of idempotency: a replayed request must never re-run the saga.
        inventory.Verify(x => x.HoldSeatAsync(It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        payment.Verify(x => x.ChargeAsync(It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetBookingAsync_ReturnsNull_WhenBookingDoesNotExist()
    {
        await using var db = CreateContext(Guid.NewGuid().ToString());
        var orchestrator = new BookingOrchestrator(db, Mock.Of<IInventoryClient>(), Mock.Of<IPaymentClient>(), NullLogger<BookingOrchestrator>.Instance);

        var result = await orchestrator.GetBookingAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
    }
}
