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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgenticPayments.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class ProviderWebhookTests(ApiFactory factory) : IAsyncLifetime
{
    private const string WebhookPath = "/webhooks/provider";
    private const string SignatureHeader = "X-Provider-Signature";
    private const string JsonMediaType = "application/json";
    private const string OtherSecret = "another-webhook-secret";

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
    [InlineData("succeeded", PaymentStatus.Succeeded)]
    [InlineData("FAILED", PaymentStatus.Failed)]
    public async Task Webhook_ValidSignedEvent_Returns200AndUpdatesStatus(string status, PaymentStatus expectedStatus)
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
            () => Assert.Equal(paymentId, storedEvent.PaymentId));
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
    public async Task TryRecordAsync_EventIdAlreadyStored_ReturnsFalseAndSavesNothing()
    {
        var paymentId = await SeedPaymentAsync();
        var eventId = NewEventId();
        await using (var seedScope = factory.Services.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ProcessedWebhookEvents.Add(new ProcessedWebhookEvent(eventId, paymentId));
            await db.SaveChangesAsync();
        }

        bool recorded;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var payments = scope.ServiceProvider.GetRequiredService<IPaymentRepository>();
            var webhookEvents = scope.ServiceProvider.GetRequiredService<IWebhookEventRepository>();
            var payment = await payments.FindAsync(paymentId, CancellationToken.None);
            Assert.NotNull(payment);
            payment.ChangeStatus(PaymentStatus.Failed);

            recorded = await webhookEvents.TryRecordAsync(
                new ProcessedWebhookEvent(eventId, paymentId),
                payment,
                CancellationToken.None);
        }

        var paymentStatus = await ReadPaymentStatusAsync(paymentId);
        var eventCount = await CountEventsAsync(eventId);
        Assert.Multiple(
            () => Assert.False(recorded),
            () => Assert.Equal(PaymentStatus.Pending, paymentStatus),
            () => Assert.Equal(1, eventCount));
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

    private async Task<Guid> SeedPaymentAsync()
    {
        var payment = new Payment(_fixture.Create<int>() + 0.50m, _fixture.Create<Currency>(), _fixture.Create<PaymentMethod>());
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

    private async Task<ProcessedWebhookEvent> FindSingleEventAsync(string eventId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ProcessedWebhookEvents.AsNoTracking().SingleAsync(e => e.EventId == eventId);
    }

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
