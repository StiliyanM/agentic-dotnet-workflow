using AgenticPayments.Domain.Payments;
using FluentValidation;

namespace AgenticPayments.Application.Payments;

public sealed class CreatePaymentUseCase(
    IValidator<CreatePaymentRequest> validator,
    IValidator<string> idempotencyKeyValidator,
    IIdempotencyRecordRepository idempotencyRecords,
    TimeProvider timeProvider)
{
    private const string RedirectBaseUrl = "https://pay.example.com";

    public async Task<CreatePaymentResult> ExecuteAsync(
        string idempotencyKey,
        CreatePaymentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(idempotencyKey);
        ArgumentNullException.ThrowIfNull(request);

        // An invalid request reads and stores nothing, so a corrected request can still use the same key.
        var keyResult = await idempotencyKeyValidator.ValidateAsync(idempotencyKey, cancellationToken);
        var requestResult = await validator.ValidateAsync(request, cancellationToken);
        if (!keyResult.IsValid || !requestResult.IsValid)
        {
            return CreatePaymentResult.Invalid(
                new Dictionary<string, string[]>([.. keyResult.ToDictionary(), .. requestResult.ToDictionary()]));
        }

        var hash = CreatePaymentRequestHash.Compute(request.Amount, request.Currency, request.Method);
        var now = timeProvider.GetUtcNow();

        var record = await idempotencyRecords.FindAsync(idempotencyKey, cancellationToken);
        if (record is not null && !record.IsExpired(now))
        {
            // The replay is the first response, so its status is Pending even if a webhook changed the payment since.
            return record.RequestHash == hash
                ? CreatePaymentResult.Success(Response(record.PaymentId, request.Method))
                : CreatePaymentResult.IdempotencyKeyReused();
        }

        var payment = new Payment(request.Amount, request.Currency, request.Method);
        if (record is null)
        {
            record = new IdempotencyRecord(idempotencyKey, hash, payment.Id, now);
        }
        else
        {
            record.Renew(hash, payment.Id, now);
        }

        // A renewed record is saved only because FindAsync of the same repository scope tracks it.
        var saveResult = await idempotencyRecords.SaveAsync(record, payment, cancellationToken);
        return saveResult == IdempotencySaveResult.Saved
            ? CreatePaymentResult.Success(Response(payment.Id, payment.Method))
            : CreatePaymentResult.IdempotencyKeyInProgress();
    }

    private static CreatePaymentResponse Response(Guid paymentId, PaymentMethod method) =>
        new(paymentId, new Uri($"{RedirectBaseUrl}/{ToUrlSegment(method)}/{paymentId:D}"), PaymentStatus.Pending);

    private static string ToUrlSegment(PaymentMethod method) => method switch
    {
        PaymentMethod.Ideal => "ideal",
        PaymentMethod.Klarna => "klarna",
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, null),
    };
}
