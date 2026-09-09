using Payment.Api.Dtos;
using Payment.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Payment.Api.Controllers;

[ApiController]
[Route("api/payments")]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentProcessingService _paymentService;

    public PaymentsController(IPaymentProcessingService paymentService)
    {
        _paymentService = paymentService;
    }

    [HttpPost("charge")]
    [ProducesResponseType(typeof(ChargeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ChargeResponse>> Charge([FromBody] ChargeRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _paymentService.ChargeAsync(request, ct);
            return Ok(result);
        }
        catch (PaymentDeclinedException ex)
        {
            // Surfaces as a 5xx so the caller's resilience pipeline (retry/circuit
            // breaker) treats this exactly like a real transient gateway failure.
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = ex.Message });
        }
    }

    [HttpPost("{transactionId:guid}/refund")]
    public async Task<IActionResult> Refund(Guid transactionId, [FromBody] RefundRequest request, CancellationToken ct)
    {
        var result = await _paymentService.RefundAsync(transactionId, request.Reason, ct);
        return result switch
        {
            RefundOutcome.Success => Ok(new RefundResponse("Refunded")),
            RefundOutcome.NotFound => NotFound(),
            _ => Problem()
        };
    }
}
