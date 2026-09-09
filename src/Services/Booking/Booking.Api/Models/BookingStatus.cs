namespace Booking.Api.Models;

/// <summary>
/// States of the booking saga. Mirrors the flow in the LLD: Pending -> AwaitingPayment
/// -> Confirmed on the happy path, or -> Compensating -> Cancelled if payment fails.
/// </summary>
public enum BookingStatus
{
    Pending,
    AwaitingPayment,
    Confirmed,
    Compensating,
    Cancelled,
    Failed
}
