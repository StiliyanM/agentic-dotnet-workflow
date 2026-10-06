I fixed both findings from the documentation review: the required one in `payments-api.md` and the optional one in `README.md`. I changed only those two files and ran no checks, so this change makes no claim that a check passed.

**1. C:\Users\Stiliyan\source\repos\ApmPlayground\docs\user\payments-api.md, the paragraph after the error table (required finding)**

The old sentence that listed "415, a body that is not a JSON object, or a field value that cannot be read" is gone. It was incomplete. The new text says the API reads the body before it checks the key. When the body cannot be read, the API returns the body error at once and does not check the key, so there is no `Idempotency-Key` error even when the header is missing. It lists every case:
- `Content-Type` is not JSON (415).
  - Source: `JsonRequestBody.ReadAsync`, the `HasJsonContentType` check.
- The body is empty or is not valid JSON.
  - Source: `ReadAsync`, which catches the `JsonException` from `JsonDocument.ParseAsync`.
- The body is not a JSON object.
  - Source: `Read`, the `ValueKind != Object` check.
- A required field (`amount`, `currency` or `method`) is missing.
  - Source: `Read`, the `IsRequired && !HasMember` loop, then `ValidationProblem`.
- A field value cannot be read.
  - Source: `Read`, the `catch (JsonException)` block with `FindProperty`.

All five are in `src/AgenticPayments.Api/JsonRequestBody.cs`. The early return is in `src/AgenticPayments.Api/Payments/PaymentEndpoints.cs`: `if (!body.IsSuccess) return body.Error;` runs before the line that reads the `Idempotency-Key` header.

New example: `{"currency":"EUR","method":"ideal"}` with no header gives `400` with only `errors.amount`. Source: the same early return, plus the missing-member error "Amount is required." in `JsonRequestBody.Read`.

New statement: only field rule errors (for example "Amount must be greater than 0.") are combined with key errors in one validation problem. Source: `CreatePaymentUseCase.ExecuteAsync` in `src/AgenticPayments.Application/Payments/CreatePaymentUseCase.cs` merges `keyResult` and `requestResult` into one `Invalid` dictionary. It is called only after the body has been read.

**2. C:\Users\Stiliyan\source\repos\ApmPlayground\README.md, known limitation "Two requests with the same new key at the same time" (optional finding)**

The old text said "One gets `201`. The other gets `409` and must retry." The new text:
- One payment is created. One request gets `201`. The other gets `409` and must retry.
- When the first request was already saved before the other request looked up the key, the other request gets the replay (`201`, same `paymentId`) if it is the same request, or `422` if it is a different request.
- The rest of the bullet is unchanged.

Source: `CreatePaymentUseCase.ExecuteAsync`. `FindAsync` returns an unexpired record, which gives `Success(Response(record.PaymentId, ...))` when the hash matches and `IdempotencyKeyReused` (422 in `PaymentEndpoints`) when it does not. A failed `SaveAsync` gives `IdempotencyKeyInProgress` (409).

The 422 case goes beyond what the reviewer asked for. The code returns 422 here when the request differs, so leaving it out would have been inaccurate. I left the `payments-api.md` idempotency row ("The request whose save comes second gets `409`") unchanged. It is consistent with the README.
