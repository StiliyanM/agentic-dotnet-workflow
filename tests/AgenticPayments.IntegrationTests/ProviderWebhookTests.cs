using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgenticPayments.Application.Payments;
using AgenticPayments.Application.Webhooks;
using AgenticPayments.Domain.Payments;
using AgenticPayments.Domain.Webhooks;
using AgenticPayments.Infrastructure.Persistence;
using AgenticPayments.IntegrationTests.Infrastructure;
using AutoFixture;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgenticPayments.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class ProviderWebhookTests(ApiFactory factory) : IAsyncLifetime
{
    private const string WebhookPath = "/webhooks/provider";
    private const string SignatureHeader = "X-Provider-Signature";
    private const string JsonMediaType = "application/json";
    private const string OtherSecret = "another-webhook-secret";
    private const string WebhookLogCategory = "AgenticPayments.Api.Webhooks.WebhookEndpoints";

    private readonly Fixture _fixture = new();
    private readonly List<Guid> _paymentIds = [];
    private readonly List<string> _eventIds = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (_eventIds.Count > 0)
        {
            await db.ProcessedWebhookEvents.Where(e => _eventIds.Contains(e.EventId)).ExecuteDeleteAsync();
        }

        if (_paymentIds.Count > 0)
        {
            await db.Payments.Where(p => _paymentIds.Contains(p.Id)).ExecuteDeleteAsync();
        }
    }

    [Theory]
    [InlineData("succeeded", PaymentStatus.Succeeded, ProviderPaymentStatus.Succeeded)]
    [InlineData("FAILED", PaymentStatus.Failed, ProviderPaymentStatus.Failed)]
    public async Task Webhook_ValidSignedEvent_Returns200AndUpdatesStatus(
        string status,
        PaymentStatus expectedStatus,
        ProviderPaymentStatus providerStatus)
    {
        using var client = factory.CreateClient();
        var paymentId = await SeedPaymentAsync();
        var eventId = NewEventId();
        var body = Body(eventId, paymentId, status);

        using var response = await PostWebhookAsync(client, body, [Sign(body, ApiFactory.WebhookSecret)]);

        var content = await response.Content.ReadAsStringAsync();
        var storedEvent = await FindSingleEventAsync(eventId);
        var paymentStatus = await ReadPaymentStatusAsync(paymentId);
        Assert.Multiple(
            () => Assert.Equal(HttpStatusCode.OK, response.StatusCode),
            () => Assert.Empty(content),
            () => Assert.Equal(expectedStatus, paymentStatus),
            () => Assert.Equal(paymentId, storedEvent.PaymentId),
            () => Assert.Equal(WebhookPayloadHash.Compute(paymentId, providerStatus), storedEvent.PayloadHash));
    }

    [Theory]
    [InlineData("succeeded", "failed", PaymentStatus.Succeeded)]
    [InlineData("failed", "succeeded", PaymentStatus.Failed)]
    public async Task Webhook_TerminalPaymentNewEvent_Returns200KeepsStatusAndRecordsEvent(
        string firstStatus,
        string laterStatus,
        PaymentStatus expectedStatus)
    {
        using var client = factory.CreateClient();
        var paymentId = await SeedPaymentAsync();
        var firstBody = Body(NewEventId(), paymentId, firstStatus);
        var laterEventId = NewEventId();
        var laterBody = Body(laterEventId, paymentId, laterStatus);

        using var first = await PostWebhookAsync(client, firstBody, [Sign(firstBody, ApiFactory.WebhookSecret)]);
        using var later = await PostWebhookAsync(client, laterBody, [Sign(laterBody, ApiFactory.WebhookSecret)]);

        var laterContent = await later.Content.ReadAsStringAsync();
        var paymentStatus = await ReadPaymentStatusAsync(paymentId);
        var laterEvent = await FindSingleEventAsync(laterEventId);
        Assert.Multiple(
            () => Assert.Equal(HttpStatusCode.OK, first.StatusCode),
            () => Assert.Equal(HttpStatusCode.OK, later.StatusCode),
            () => Assert.Empty(laterContent),
            () => Assert.Equal(expectedStatus, paymentStatus),
            () => Assert.Equal(paymentId, laterEvent.PaymentId));
    }

    [Fact]
    public async Task Webhook_IgnoredEventRepeated_Returns200AndChangesNothing()
    {
        using var client = factory.CreateClient();
        var paymentId = await SeedPaymentAsync();
        var appliedBody = Body(NewEventId(), paymentId, "succeeded");
        var ignoredEventId = NewEventId();
        var ignoredBody = Body(ignoredEventId, paymentId, "failed");
        var ignoredSignature = Sign(ignoredBody, ApiFactory.WebhookSecret);

        using var applied = await PostWebhookAsync(client, appliedBody, [Sign(appliedBody, ApiFactory.WebhookSecret)]);
        using var ignored = await PostWebhookAsync(client, ignoredBody, [ignoredSignature]);
        using var repeat = await PostWebhookAsync(client, ignoredBody, [ignoredSignature]);

        var repeatContent = await repeat.Content.ReadAsStringAsync();
        var paymentStatus = await ReadPaymentStatusAsync(paymentId);
        var eventCount = await CountEventsAsync(ignoredEventId);
        Assert.Multiple(
            () => Assert.Equal(HttpStatusCode.OK, applied.StatusCode),
            () => Assert.Equal(HttpStatusCode.OK, ignored.StatusCode),
            () => Assert.Equal(HttpStatusCode.OK, repeat.StatusCode),
            () => Assert.Empty(repeatContent),
            () => Assert.Equal(PaymentStatus.Succeeded, paymentStatus),
            () => Assert.Equal(1, eventCount));
    }

    [Fact]
    public async Task Webhook_SameEventSamePayload_Returns200AndLogsNoWarning()
    {
        var logs = new CapturingLoggerProvider();
        await using var loggingFactory = WithCapturedLogs(logs);
        using var client = loggingFactory.CreateClient();
        var paymentId = await SeedPaymentAsync();
        var eventId = NewEventId();
        var body = Body(eventId, paymentId, "succeeded");
        var signature = Sign(body, ApiFactory.WebhookSecret);

        using var first = await PostWebhookAsync(client, body, [signature]);
        using var repeat = await PostWebhookAsync(client, body, [signature]);

        var repeatContent = await repeat.Content.ReadAsStringAsync();
        var paymentStatus = await ReadPaymentStatusAsync(paymentId);
        var eventCount = await CountEventsAsync(eventId);
        Assert.Multiple(
            () => Assert.Equal(HttpStatusCode.OK, first.StatusCode),
            () => Assert.Equal(HttpStatusCode.OK, repeat.StatusCode),
            () => Assert.Empty(repeatContent),
            () => Assert.Equal(PaymentStatus.Succeeded, paymentStatus),
            () => Assert.Equal(1, eventCount),
            () => Assert.Empty(WebhookWarnings(logs)));
    }

    [Fact]
    public async Task Webhook_MissingSignature_Returns401AndChangesNothing()
    {
        using var client = factory.CreateClient();
        var paymentId = await SeedPaymentAsync();
        var eventId = NewEventId();

        using var response = await PostWebhookAsync(client, Body(eventId, paymentId, "succeeded"), []);

        await AssertUnauthorizedAndNothingChangedAsync(response, paymentId, eventId);
    }

    [Theory]
    [InlineData("signed with another secret")]
    [InlineData("body changed after signing")]
    [InlineData("no prefix")]
    [InlineData("not hex")]
    [InlineData("wrong length")]
    [InlineData("two header values")]
    public async Task Webhook_InvalidSignature_Returns401(string signatureCase)
    {
        using var client = factory.CreateClient();
        var paymentId = await SeedPaymentAsync();
        var eventId = NewEventId();
        var body = Body(eventId, paymentId, "succeeded");
        var validSignature = Sign(body, ApiFactory.WebhookSecret);
        string[] signatures = signatureCase switch
        {
            "signed with another secret" => [Sign(body, OtherSecret)],
            "body changed after signing" => [Sign(Body(eventId, paymentId, "failed"), ApiFactory.WebhookSecret)],
            "no prefix" => [validSignature["sha256=".Length..]],
            "not hex" => ["sha256=" + new string('z', 64)],
            "wrong length" => [validSignature[..^2]],
            "two header values" => [validSignature, validSignature],
            _ => throw new ArgumentOutOfRangeException(nameof(signatureCase), signatureCase, null),
        };

        using var response = await PostWebhookAsync(client, body, signatures);

        await AssertUnauthorizedAndNothingChangedAsync(response, paymentId, eventId);
    }

    [Fact]
    public async Task Webhook_UppercaseHexSignature_Returns200()
    {
        using var client = factory.CreateClient();
        var paymentId = await SeedPaymentAsync();
        var eventId = NewEventId();
        var body = Body(eventId, paymentId, "succeeded");
        var signature = "sha256=" + Convert.ToHexString(Hmac(body, ApiFactory.WebhookSecret));

        using var response = await PostWebhookAsync(client, body, [signature]);

        var paymentStatus = await ReadPaymentStatusAsync(paymentId);
        Assert.Multiple(
            () => Assert.Equal(HttpStatusCode.OK, response.StatusCode),
            () => Assert.Equal(PaymentStatus.Succeeded, paymentStatus));
    }

    [Fact]
    public async Task Webhook_UnsignedMalformedBody_Returns401()
    {
        using var client = factory.CreateClient();

        using var response = await PostWebhookAsync(client, """{ "eventId": """, []);

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Webhook_SignedMalformedJson_Returns400Problem()
    {
        using var client = factory.CreateClient();
        const string body = """{ "eventId": """;

        using var response = await PostWebhookAsync(client, body, [Sign(body, ApiFactory.WebhookSecret)]);

        await AssertProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            root => Assert.False(root.TryGetProperty("errors", out _)));
    }

    [Fact]
    public async Task Webhook_SignedNonJsonContentType_Returns415()
    {
        using var client = factory.CreateClient();
        var paymentId = await SeedPaymentAsync();
        var eventId = NewEventId();
        var body = Body(eventId, paymentId, "succeeded");

        using var response = await PostWebhookAsync(client, body, [Sign(body, ApiFactory.WebhookSecret)], "text/plain");

        var content = await response.Content.ReadAsStringAsync();
        var paymentStatus = await ReadPaymentStatusAsync(paymentId);
        Assert.Multiple(
            () => Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode),
            () => Assert.Empty(content),
            () => Assert.Equal(PaymentStatus.Pending, paymentStatus));
    }

    [Theory]
    [InlineData("eventId", "EventId is required.")]
    [InlineData("paymentId", "PaymentId is required.")]
    [InlineData("status", "Status is required.")]
    public async Task Webhook_MissingField_Returns400WithRequiredError(string field, string expectedMessage)
    {
        using var client = factory.CreateClient();
        var body = ToJsonObject(ValidRawFields().Where(f => f.Field != field));

        using var response = await PostWebhookAsync(client, body, [Sign(body, ApiFactory.WebhookSecret)]);

        await AssertValidationProblemAsync(response, field, expectedMessage);
    }

    [Theory]
    [InlineData("\"refunded\"")]
    [InlineData("\"pending\"")]
    [InlineData("1")]
    public async Task Webhook_InvalidStatus_Returns400WithStatusError(string statusJson)
    {
        using var client = factory.CreateClient();
        var paymentId = await SeedPaymentAsync();
        var eventId = NewEventId();
        var body = $$"""{"eventId":"{{eventId}}","paymentId":"{{paymentId}}","status":{{statusJson}}}""";

        using var response = await PostWebhookAsync(client, body, [Sign(body, ApiFactory.WebhookSecret)]);

        var paymentStatus = await ReadPaymentStatusAsync(paymentId);
        var eventCount = await CountEventsAsync(eventId);
        await AssertValidationProblemAsync(
            response,
            "status",
            "Status has an invalid value.",
            () => Assert.Equal(PaymentStatus.Pending, paymentStatus),
            () => Assert.Equal(0, eventCount));
    }

    [Theory]
    [InlineData("\"not-a-guid\"")]
    [InlineData("123")]
    public async Task Webhook_InvalidPaymentId_Returns400WithPaymentIdError(string paymentIdJson)
    {
        using var client = factory.CreateClient();
        var eventId = NewEventId();
        var body = $$"""{"eventId":"{{eventId}}","paymentId":{{paymentIdJson}},"status":"succeeded"}""";

        using var response = await PostWebhookAsync(client, body, [Sign(body, ApiFactory.WebhookSecret)]);

        var eventCount = await CountEventsAsync(eventId);
        await AssertValidationProblemAsync(
            response,
            "paymentId",
            "PaymentId has an invalid value.",
            () => Assert.Equal(0, eventCount));
    }

    [Fact]
    public async Task Webhook_EmptyEventId_Returns400WithEventIdError()
    {
        using var client = factory.CreateClient();
        var paymentId = await SeedPaymentAsync();
        var body = Body(string.Empty, paymentId, "succeeded");

        using var response = await PostWebhookAsync(client, body, [Sign(body, ApiFactory.WebhookSecret)]);

        var paymentStatus = await ReadPaymentStatusAsync(paymentId);
        await AssertValidationProblemAsync(
            response,
            "eventId",
            "EventId is required.",
            () => Assert.Equal(PaymentStatus.Pending, paymentStatus));
    }

    [Theory]
    [InlineData("wrong signature")]
    [InlineData("pending status")]
    [InlineData("empty eventId")]
    public async Task Webhook_TerminalPaymentRejectedRequest_ReturnsSpec002ResponseAndChangesNothing(string rejectedCase)
    {
        using var client = factory.CreateClient();
        var paymentId = await SeedPaymentAsync(PaymentStatus.Succeeded);
        var eventId = NewEventId();
        var failedBody = Body(eventId, paymentId, "failed");
        var (body, signatures) = rejectedCase switch
        {
            "wrong signature" => SignedBody(failedBody, OtherSecret),
            "pending status" => SignedBody(Body(eventId, paymentId, "pending")),
            "empty eventId" => SignedBody(Body(string.Empty, paymentId, "failed")),
            _ => throw new ArgumentOutOfRangeException(nameof(rejectedCase), rejectedCase, null),
        };

        using var response = await PostWebhookAsync(client, body, signatures);

        var paymentStatus = await ReadPaymentStatusAsync(paymentId);
        var paymentEventCount = await CountEventsForPaymentAsync(paymentId);
        Action[] unchanged =
        [
            () => Assert.Equal(PaymentStatus.Succeeded, paymentStatus),
            () => Assert.Equal(0, paymentEventCount),
        ];
        await (rejectedCase switch
        {
            "wrong signature" => AssertProblemAsync(
                response,
                HttpStatusCode.Unauthorized,
                [.. unchanged.Select(assertion => (Action<JsonElement>)(_ => assertion()))]),
            "pending status" => AssertValidationProblemAsync(response, "status", "Status has an invalid value.", unchanged),
            _ => AssertValidationProblemAsync(response, "eventId", "EventId is required.", unchanged),
        });
    }

    [Fact]
    public async Task Webhook_UnknownPayment_Returns404AndStoresNoEvent()
    {
        using var client = factory.CreateClient();
        var eventId = NewEventId();
        var body = Body(eventId, _fixture.Create<Guid>(), "succeeded");

        using var response = await PostWebhookAsync(client, body, [Sign(body, ApiFactory.WebhookSecret)]);

        var eventCount = await CountEventsAsync(eventId);
        await AssertProblemAsync(
            response,
            HttpStatusCode.NotFound,
            _ => Assert.Equal(0, eventCount));
    }

    [Fact]
    public async Task Webhook_DuplicateEventId_Returns200AndDoesNotChangeStatusAgain()
    {
        using var client = factory.CreateClient();
        var paymentId = await SeedPaymentAsync();
        var eventId = NewEventId();
        var firstBody = Body(eventId, paymentId, "succeeded");
        var repeatBody = Body(eventId, paymentId, "failed");

        using var first = await PostWebhookAsync(client, firstBody, [Sign(firstBody, ApiFactory.WebhookSecret)]);
        using var repeat = await PostWebhookAsync(client, repeatBody, [Sign(repeatBody, ApiFactory.WebhookSecret)]);

        var repeatContent = await repeat.Content.ReadAsStringAsync();
        var paymentStatus = await ReadPaymentStatusAsync(paymentId);
        var eventCount = await CountEventsAsync(eventId);
        Assert.Multiple(
            () => Assert.Equal(HttpStatusCode.OK, first.StatusCode),
            () => Assert.Equal(HttpStatusCode.OK, repeat.StatusCode),
            () => Assert.Empty(repeatContent),
            () => Assert.Equal(PaymentStatus.Succeeded, paymentStatus),
            () => Assert.Equal(1, eventCount));
    }

    [Theory]
    [InlineData("other status")]
    [InlineData("other paymentId")]
    public async Task Webhook_SameEventIdDifferentPayload_Returns200ChangesNothingAndLogsWarning(string difference)
    {
        var logs = new CapturingLoggerProvider();
        await using var loggingFactory = WithCapturedLogs(logs);
        using var client = loggingFactory.CreateClient();
        var paymentId = await SeedPaymentAsync();
        var otherPaymentId = await SeedPaymentAsync();
        var eventId = NewEventId();
        var firstBody = Body(eventId, paymentId, "succeeded");
        var repeatBody = difference switch
        {
            "other status" => Body(eventId, paymentId, "failed"),
            "other paymentId" => Body(eventId, otherPaymentId, "succeeded"),
            _ => throw new ArgumentOutOfRangeException(nameof(difference), difference, null),
        };

        using var first = await PostWebhookAsync(client, firstBody, [Sign(firstBody, ApiFactory.WebhookSecret)]);
        using var repeat = await PostWebhookAsync(client, repeatBody, [Sign(repeatBody, ApiFactory.WebhookSecret)]);

        var repeatContent = await repeat.Content.ReadAsStringAsync();
        var paymentStatus = await ReadPaymentStatusAsync(paymentId);
        var otherPaymentStatus = await ReadPaymentStatusAsync(otherPaymentId);
        var storedEvent = await FindSingleEventAsync(eventId);
        var warnings = WebhookWarnings(logs);
        Assert.Multiple(
            () => Assert.Equal(HttpStatusCode.OK, first.StatusCode),
            () => Assert.Equal(HttpStatusCode.OK, repeat.StatusCode),
            () => Assert.Empty(repeatContent),
            () => Assert.Equal(PaymentStatus.Succeeded, paymentStatus),
            () => Assert.Equal(PaymentStatus.Pending, otherPaymentStatus),
            () => Assert.Equal(paymentId, storedEvent.PaymentId),
            () => Assert.Equal(WebhookPayloadHash.Compute(paymentId, ProviderPaymentStatus.Succeeded), storedEvent.PayloadHash),
            () => Assert.Contains(eventId, Assert.Single(warnings), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Webhook_EventStoredWithoutPayloadHash_CountsAsDuplicate()
    {
        var logs = new CapturingLoggerProvider();
        await using var loggingFactory = WithCapturedLogs(logs);
        using var client = loggingFactory.CreateClient();
        var paymentId = await SeedPaymentAsync();
        var eventId = NewEventId();
        await using (var seedScope = factory.Services.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ProcessedWebhookEvents.Add(
                new ProcessedWebhookEvent(eventId, paymentId, WebhookPayloadHash.Compute(paymentId, ProviderPaymentStatus.Succeeded)));
            await db.SaveChangesAsync();
            // A row stored before spec 003 has no hash.
            await db.Database.ExecuteSqlAsync(
                $"""UPDATE "ProcessedWebhookEvents" SET "PayloadHash" = NULL WHERE "EventId" = {eventId}""");
        }

        var body = Body(eventId, paymentId, "failed");

        using var response = await PostWebhookAsync(client, body, [Sign(body, ApiFactory.WebhookSecret)]);

        var content = await response.Content.ReadAsStringAsync();
        var paymentStatus = await ReadPaymentStatusAsync(paymentId);
        var storedEvent = await FindSingleEventAsync(eventId);
        Assert.Multiple(
            () => Assert.Equal(HttpStatusCode.OK, response.StatusCode),
            () => Assert.Empty(content),
            () => Assert.Equal(PaymentStatus.Pending, paymentStatus),
            () => Assert.Null(storedEvent.PayloadHash),
            () => Assert.Empty(WebhookWarnings(logs)));
    }

    [Theory]
    [InlineData("succeeded", "failed", PaymentStatus.Succeeded)]
    [InlineData("failed", "succeeded", PaymentStatus.Failed)]
    public async Task Webhook_ConcurrentTerminalEvents_KeepFirstSavedStatusAndIgnoreOther(
        string firstSavedStatus,
        string otherStatus,
        PaymentStatus expectedStatus)
    {
        var paymentId = await SeedPaymentAsync();
        var firstEventId = NewEventId();
        var otherEventId = NewEventId();
        var coordinator = new WebhookRaceCoordinator(firstEventId);
        await using var raceFactory = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddScoped<IPaymentRepository>(provider => new RacingPaymentRepository(
                new PaymentRepository(provider.GetRequiredService<AppDbContext>()),
                coordinator));
            services.AddScoped<IWebhookEventRepository>(provider => new RacingWebhookEventRepository(
                new WebhookEventRepository(provider.GetRequiredService<AppDbContext>()),
                coordinator));
        }));
        using var client = raceFactory.CreateClient();
        var firstBody = Body(firstEventId, paymentId, firstSavedStatus);
        var otherBody = Body(otherEventId, paymentId, otherStatus);

        // The coordinator makes both requests load the Pending payment before either one saves.
        var responses = await Task.WhenAll(
            PostWebhookAsync(client, firstBody, [Sign(firstBody, ApiFactory.WebhookSecret)]),
            PostWebhookAsync(client, otherBody, [Sign(otherBody, ApiFactory.WebhookSecret)]));
        var statusCodes = responses.Select(r => r.StatusCode).ToList();
        foreach (var response in responses)
        {
            response.Dispose();
        }

        var paymentStatus = await ReadPaymentStatusAsync(paymentId);
        var firstEventCount = await CountEventsAsync(firstEventId);
        var otherEventCount = await CountEventsAsync(otherEventId);
        Assert.Multiple(
            () => Assert.All(statusCodes, code => Assert.Equal(HttpStatusCode.OK, code)),
            () => Assert.Equal(expectedStatus, paymentStatus),
            () => Assert.Equal(1, firstEventCount),
            () => Assert.Equal(1, otherEventCount));
    }

    [Fact]
    public async Task Webhook_ConcurrentDuplicates_ProcessOnce()
    {
        using var client = factory.CreateClient();
        var paymentId = await SeedPaymentAsync();
        var eventId = NewEventId();
        var body = Body(eventId, paymentId, "succeeded");
        var signature = Sign(body, ApiFactory.WebhookSecret);

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 5).Select(_ => PostWebhookAsync(client, body, [signature])));
        var statusCodes = responses.Select(r => r.StatusCode).ToList();
        foreach (var response in responses)
        {
            response.Dispose();
        }

        var paymentStatus = await ReadPaymentStatusAsync(paymentId);
        var eventCount = await CountEventsAsync(eventId);
        Assert.Multiple(
            () => Assert.All(statusCodes, code => Assert.Equal(HttpStatusCode.OK, code)),
            () => Assert.Equal(PaymentStatus.Succeeded, paymentStatus),
            () => Assert.Equal(1, eventCount));
    }

    [Fact]
    public async Task RecordAsync_EventIdAlreadyStored_ReturnsDuplicateEventAndSavesNothing()
    {
        var paymentId = await SeedPaymentAsync();
        var eventId = NewEventId();
        await using (var seedScope = factory.Services.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ProcessedWebhookEvents.Add(
                new ProcessedWebhookEvent(eventId, paymentId, WebhookPayloadHash.Compute(paymentId, ProviderPaymentStatus.Succeeded)));
            await db.SaveChangesAsync();
        }

        WebhookRecordResult result;
        int trackedAfterRecord;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var payments = scope.ServiceProvider.GetRequiredService<IPaymentRepository>();
            var webhookEvents = scope.ServiceProvider.GetRequiredService<IWebhookEventRepository>();
            var payment = await payments.FindAsync(paymentId, CancellationToken.None);
            Assert.NotNull(payment);
            Assert.True(payment.TryChangeStatus(PaymentStatus.Failed));

            result = await webhookEvents.RecordAsync(
                new ProcessedWebhookEvent(eventId, paymentId, WebhookPayloadHash.Compute(paymentId, ProviderPaymentStatus.Failed)),
                payment,
                CancellationToken.None);
            trackedAfterRecord = scope.ServiceProvider.GetRequiredService<AppDbContext>().ChangeTracker.Entries().Count();
        }

        var paymentStatus = await ReadPaymentStatusAsync(paymentId);
        var eventCount = await CountEventsAsync(eventId);
        Assert.Multiple(
            () => Assert.Equal(WebhookRecordResult.DuplicateEvent, result),
            () => Assert.Equal(PaymentStatus.Pending, paymentStatus),
            () => Assert.Equal(1, eventCount),
            () => Assert.Equal(0, trackedAfterRecord));
    }

    [Fact]
    public async Task RecordAsync_PaymentChangedSinceLoad_ReturnsPaymentChangedAndSavesNothing()
    {
        var paymentId = await SeedPaymentAsync();
        var firstEventId = NewEventId();
        var secondEventId = NewEventId();
        await using var firstScope = factory.Services.CreateAsyncScope();
        await using var secondScope = factory.Services.CreateAsyncScope();
        var firstPayment = await firstScope.ServiceProvider.GetRequiredService<IPaymentRepository>()
            .FindAsync(paymentId, CancellationToken.None);
        var secondPayment = await secondScope.ServiceProvider.GetRequiredService<IPaymentRepository>()
            .FindAsync(paymentId, CancellationToken.None);
        Assert.NotNull(firstPayment);
        Assert.NotNull(secondPayment);
        Assert.True(firstPayment.TryChangeStatus(PaymentStatus.Succeeded));
        Assert.True(secondPayment.TryChangeStatus(PaymentStatus.Failed));

        var firstResult = await firstScope.ServiceProvider.GetRequiredService<IWebhookEventRepository>().RecordAsync(
            new ProcessedWebhookEvent(firstEventId, paymentId, WebhookPayloadHash.Compute(paymentId, ProviderPaymentStatus.Succeeded)),
            firstPayment,
            CancellationToken.None);
        var secondResult = await secondScope.ServiceProvider.GetRequiredService<IWebhookEventRepository>().RecordAsync(
            new ProcessedWebhookEvent(secondEventId, paymentId, WebhookPayloadHash.Compute(paymentId, ProviderPaymentStatus.Failed)),
            secondPayment,
            CancellationToken.None);

        var trackedAfterConflict = secondScope.ServiceProvider.GetRequiredService<AppDbContext>().ChangeTracker.Entries().Count();
        var paymentStatus = await ReadPaymentStatusAsync(paymentId);
        var firstEventCount = await CountEventsAsync(firstEventId);
        var secondEventCount = await CountEventsAsync(secondEventId);
        Assert.Multiple(
            () => Assert.Equal(WebhookRecordResult.Recorded, firstResult),
            () => Assert.Equal(WebhookRecordResult.PaymentChanged, secondResult),
            () => Assert.Equal(PaymentStatus.Succeeded, paymentStatus),
            () => Assert.Equal(1, firstEventCount),
            () => Assert.Equal(0, secondEventCount),
            () => Assert.Equal(0, trackedAfterConflict));
    }

    [Fact]
    public async Task RecordAsync_PaymentNotTracked_ThrowsInvalidOperationException()
    {
        var paymentId = await SeedPaymentAsync();
        var eventId = NewEventId();
        Payment detachedPayment;
        await using (var loadScope = factory.Services.CreateAsyncScope())
        {
            var db = loadScope.ServiceProvider.GetRequiredService<AppDbContext>();
            detachedPayment = await db.Payments.AsNoTracking().SingleAsync(p => p.Id == paymentId);
        }

        Assert.True(detachedPayment.TryChangeStatus(PaymentStatus.Succeeded));

        Exception? exception;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var webhookEvents = scope.ServiceProvider.GetRequiredService<IWebhookEventRepository>();

            exception = await Record.ExceptionAsync(() => webhookEvents.RecordAsync(
                new ProcessedWebhookEvent(eventId, paymentId, WebhookPayloadHash.Compute(paymentId, ProviderPaymentStatus.Succeeded)),
                detachedPayment,
                CancellationToken.None));
        }

        var paymentStatus = await ReadPaymentStatusAsync(paymentId);
        var eventCount = await CountEventsAsync(eventId);
        Assert.Multiple(
            () => Assert.IsType<InvalidOperationException>(exception),
            () => Assert.Equal(PaymentStatus.Pending, paymentStatus),
            () => Assert.Equal(0, eventCount));
    }

    [Fact]
    public async Task Startup_WebhookSecretMissing_Fails()
    {
        // A separate factory, so that the failed start does not affect the shared factory of the collection.
        await using var failingFactory = factory.WithWebHostBuilder(
            builder => builder.UseSetting(ApiFactory.WebhookSecretSetting, string.Empty));

        var exception = Assert.Throws<OptionsValidationException>(() =>
        {
            using var client = failingFactory.CreateClient();
        });

        Assert.Contains("Webhooks:Provider:Secret is required.", exception.Failures);
    }

    private string NewEventId()
    {
        var eventId = $"evt_{_fixture.Create<Guid>():N}";
        _eventIds.Add(eventId);
        return eventId;
    }

    private async Task<Guid> SeedPaymentAsync(PaymentStatus status = PaymentStatus.Pending)
    {
        var payment = new Payment(_fixture.Create<int>() + 0.50m, _fixture.Create<Currency>(), _fixture.Create<PaymentMethod>());
        if (status != PaymentStatus.Pending)
        {
            Assert.True(payment.TryChangeStatus(status));
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Payments.Add(payment);
        await db.SaveChangesAsync();
        _paymentIds.Add(payment.Id);
        return payment.Id;
    }

    private async Task<PaymentStatus> ReadPaymentStatusAsync(Guid paymentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var payment = await db.Payments.AsNoTracking().SingleAsync(p => p.Id == paymentId);
        return payment.Status;
    }

    private async Task<int> CountEventsAsync(string eventId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ProcessedWebhookEvents.CountAsync(e => e.EventId == eventId);
    }

    private async Task<int> CountEventsForPaymentAsync(Guid paymentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ProcessedWebhookEvents.CountAsync(e => e.PaymentId == paymentId);
    }

    private async Task<ProcessedWebhookEvent> FindSingleEventAsync(string eventId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ProcessedWebhookEvents.AsNoTracking().SingleAsync(e => e.EventId == eventId);
    }

    // A host that shares the collection's database and also writes its log entries to the given provider.
    private WebApplicationFactory<Program> WithCapturedLogs(CapturingLoggerProvider logs) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddProvider(logs)));

    private static List<string> WebhookWarnings(CapturingLoggerProvider logs) =>
    [
        .. logs.Entries
            .Where(e => e.Category == WebhookLogCategory && e.Level == LogLevel.Warning)
            .Select(e => e.Message),
    ];

    private (string Field, string JsonValue)[] ValidRawFields() =>
    [
        ("eventId", $"\"{NewEventId()}\""),
        ("paymentId", $"\"{_fixture.Create<Guid>()}\""),
        ("status", "\"succeeded\""),
    ];

    private static string ToJsonObject(IEnumerable<(string Field, string JsonValue)> fields) =>
        "{" + string.Join(",", fields.Select(f => $"\"{f.Field}\":{f.JsonValue}")) + "}";

    private static string Body(string eventId, Guid paymentId, string status) =>
        $$"""{"eventId":"{{eventId}}","paymentId":"{{paymentId}}","status":"{{status}}"}""";

    private static (string Body, string[] Signatures) SignedBody(string body, string secret = ApiFactory.WebhookSecret) =>
        (body, [Sign(body, secret)]);

    private static byte[] Hmac(string body, string secret) =>
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body));

    private static string Sign(string body, string secret) => "sha256=" + Convert.ToHexStringLower(Hmac(body, secret));

    // Sends the exact UTF-8 bytes that were signed, so the server sees the same raw body.
    private static async Task<HttpResponseMessage> PostWebhookAsync(
        HttpClient client,
        string body,
        string[] signatures,
        string mediaType = JsonMediaType)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, WebhookPath);
        request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        if (signatures.Length > 0)
        {
            request.Headers.TryAddWithoutValidation(SignatureHeader, signatures);
        }

        return await client.SendAsync(request);
    }

    private async Task AssertUnauthorizedAndNothingChangedAsync(HttpResponseMessage response, Guid paymentId, string eventId)
    {
        var paymentStatus = await ReadPaymentStatusAsync(paymentId);
        var eventCount = await CountEventsAsync(eventId);

        await AssertProblemAsync(
            response,
            HttpStatusCode.Unauthorized,
            _ => Assert.Equal(PaymentStatus.Pending, paymentStatus),
            _ => Assert.Equal(0, eventCount));
    }

    // Checks the problem contract and the extra assertions (on the problem body or on state read before) in one Assert.Multiple.
    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        params Action<JsonElement>[] extraAssertions)
    {
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode == expectedStatus,
            $"Expected {(int)expectedStatus} but got {(int)response.StatusCode}: {content}");
        using var problem = JsonDocument.Parse(content);
        var root = problem.RootElement;

        Assert.Multiple(
        [
            () => Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType),
            () => Assert.Equal((int)expectedStatus, root.GetProperty("status").GetInt32()),
            .. extraAssertions.Select(assertion => (Action)(() => assertion(root))),
        ]);
    }

    private static Task AssertValidationProblemAsync(
        HttpResponseMessage response,
        string field,
        string message,
        params Action[] stateAssertions) =>
        AssertProblemAsync(
            response,
            HttpStatusCode.BadRequest,
        [
            root => Assert.Equal("One or more validation errors occurred.", root.GetProperty("title").GetString()),
            root => Assert.Equal(field, Assert.Single(root.GetProperty("errors").EnumerateObject()).Name),
            root => Assert.Equal(message, Assert.Single(root.GetProperty("errors").GetProperty(field).EnumerateArray()).GetString()),
            .. stateAssertions.Select(assertion => (Action<JsonElement>)(_ => assertion())),
        ]);
}
