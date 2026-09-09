using Payment.Api.Data;
using Payment.Api.Dtos;
using Payment.Api.Models;
using Payment.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Payment.Api.Tests;

public class PaymentProcessingServiceTests
{
    private static PaymentDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new PaymentDbContext(options);
    }

    [Fact]
    public async Task ChargeAsync_CreatesTransaction_OnFirstCall()
    {
        await using var db = CreateContext(Guid.NewGuid().ToString());
        var service = new PaymentProcessingService(db, new ChaosSettings(), NullLogger<PaymentProcessingService>.Instance);

        var result = await service.ChargeAsync(new ChargeRequest(Guid.NewGuid(), 49.99m, "key-1"), CancellationToken.None);

        Assert.Equal("Charged", result.Status);
        Assert.NotEqual(Guid.Empty, result.TransactionId);
    }

    [Fact]
    public async Task ChargeAsync_ReturnsOriginalTransaction_OnIdempotentReplay()
    {
        await using var db = CreateContext(Guid.NewGuid().ToString());
        var service = new PaymentProcessingService(db, new ChaosSettings(), NullLogger<PaymentProcessingService>.Instance);

        var first = await service.ChargeAsync(new ChargeRequest(Guid.NewGuid(), 49.99m, "key-1"), CancellationToken.None);
        var second = await service.ChargeAsync(new ChargeRequest(Guid.NewGuid(), 999m, "key-1"), CancellationToken.None); // different amount/booking, same key

        Assert.Equal(first.TransactionId, second.TransactionId);
        Assert.Single(db.Payments); // never created a second row
    }

    [Fact]
    public async Task ChargeAsync_Throws_WhenChaosFailureRateIs100Percent()
    {
        await using var db = CreateContext(Guid.NewGuid().ToString());
        var chaos = new ChaosSettings { FailureRatePct = 100 };
        var service = new PaymentProcessingService(db, chaos, NullLogger<PaymentProcessingService>.Instance);

        await Assert.ThrowsAsync<PaymentDeclinedException>(() =>
            service.ChargeAsync(new ChargeRequest(Guid.NewGuid(), 49.99m, "key-chaos"), CancellationToken.None));

        Assert.Empty(db.Payments); // a declined charge must not persist a "Charged" row
    }

    [Fact]
    public async Task ChargeAsync_NeverFails_WhenChaosFailureRateIsZero()
    {
        await using var db = CreateContext(Guid.NewGuid().ToString());
        var chaos = new ChaosSettings { FailureRatePct = 0 };
        var service = new PaymentProcessingService(db, chaos, NullLogger<PaymentProcessingService>.Instance);

        var result = await service.ChargeAsync(new ChargeRequest(Guid.NewGuid(), 49.99m, "key-safe"), CancellationToken.None);

        Assert.Equal("Charged", result.Status);
    }

    [Fact]
    public async Task RefundAsync_MarksPaymentRefunded_WhenTransactionExists()
    {
        await using var db = CreateContext(Guid.NewGuid().ToString());
        var service = new PaymentProcessingService(db, new ChaosSettings(), NullLogger<PaymentProcessingService>.Instance);

        var charge = await service.ChargeAsync(new ChargeRequest(Guid.NewGuid(), 49.99m, "key-refund"), CancellationToken.None);
        var outcome = await service.RefundAsync(charge.TransactionId, "Booking compensation", CancellationToken.None);

        Assert.Equal(RefundOutcome.Success, outcome);
        var payment = await db.Payments.FirstAsync(p => p.TransactionId == charge.TransactionId);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
    }

    [Fact]
    public async Task RefundAsync_ReturnsNotFound_WhenTransactionDoesNotExist()
    {
        await using var db = CreateContext(Guid.NewGuid().ToString());
        var service = new PaymentProcessingService(db, new ChaosSettings(), NullLogger<PaymentProcessingService>.Instance);

        var outcome = await service.RefundAsync(Guid.NewGuid(), "reason", CancellationToken.None);

        Assert.Equal(RefundOutcome.NotFound, outcome);
    }
}
