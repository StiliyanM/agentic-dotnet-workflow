using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using AgenticPayments.Application.Payments;
using AgenticPayments.Domain.Payments;
using AgenticPayments.Infrastructure.Persistence;
using AgenticPayments.IntegrationTests.Infrastructure;
using AutoFixture;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgenticPayments.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class CreatePaymentIdempotencyTests(ApiFactory factory) : IAsyncLifetime
{
    private const string KeyHeader = "Idempotency-Key";
    private const string KeyRequiredMessage = "Idempotency-Key header is required.";
    private const string KeyReusedDetail = "The Idempotency-Key was already used with a different request.";
    private const string KeyInProgressDetail =
        "Another request with the same Idempotency-Key was processed at the same time. Retry the request.";

    private readonly Fixture _fixture = new();
    private readonly List<Guid> _paymentIds = [];
    private readonly List<decimal> _amounts = [];
    private readonly List<string> _keys = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (_keys.Count > 0)
        {
            await db.IdempotencyRecords.Where(r => _keys.Contains(r.Key)).ExecuteDeleteAsync();
        }

        if (_paymentIds.Count > 0 || _amounts.Count > 0)
        {
            await db.Payments
                .Where(p => _paymentIds.Contains(p.Id) || _amounts.Contains(p.Amount))
                .ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task CreatePayment_NewKey_Returns201AndStoresKey()
    {
        using var client = factory.CreateClient();
        var key = NewKey();
        var amount = NewAmount();

        using var response = await PostAsync(client, key, Body(amount, "EUR", "ideal"));
        var created = await ReadCreatedAsync(response);

        var paymentCount = await CountPaymentsAsync(amount);
        var record = await FindRecordAsync(key);
        Assert.NotNull(record);
        Assert.Multiple(
            () => Assert.Equal("Pending", created.Status),
            () => Assert.Equal($"https://pay.example.com/ideal/{created.PaymentId:D}", created.RedirectUrl),
            () => Assert.Equal(1, paymentCount),
            () => Assert.Equal(created.PaymentId, record.PaymentId),
            () => Assert.Equal(CreatePaymentRequestHash.Compute(amount, Currency.Eur, PaymentMethod.Ideal), record.RequestHash));
    }

    [Theory]
    [InlineData("identical")]
    [InlineData("currency and method in other case")]
    [InlineData("amount with other scale")]
    [InlineData("other field order and whitespace")]
    public async Task CreatePayment_SameKeySameRequest_Returns201WithSamePaymentIdAndRedirectUrl(string variant)
    {
        using var client = factory.CreateClient();
        var key = NewKey();
        var wholeAmount = _fixture.Create<int>();
        var amount = wholeAmount + 0.50m;
        TrackAmount(amount);
        var secondBody = variant switch
        {
            "identical" => Body(amount, "EUR", "ideal"),
            "currency and method in other case" => Body(amount, "eur", "IDEAL"),
            "amount with other scale" => $$"""{"amount":{{wholeAmount}}.5,"currency":"EUR","method":"ideal"}""",
            "other field order and whitespace" => $$"""
                {
                  "method" : "ideal",
                  "currency":"EUR" ,
                  "amount":   {{wholeAmount}}.50
                }
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, null),
        };
        using var firstResponse = await PostAsync(client, key, Body(amount, "EUR", "ideal"));
        var first = await ReadCreatedAsync(firstResponse);

        using var secondResponse = await PostAsync(client, key, secondBody);
        var second = await ReadCreatedAsync(secondResponse);

        var paymentCount = await CountPaymentsAsync(amount);
        Assert.Multiple(
            () => Assert.Equal(first.PaymentId, second.PaymentId),
            () => Assert.Equal(first.RedirectUrl, second.RedirectUrl),
            () => Assert.Equal("Pending", second.Status),
            () => Assert.Equal(1, paymentCount));
    }

    [Fact]
    public async Task CreatePayment_SameKeyDifferentRequest_Returns422AndCreatesNothing()
    {
        using var client = factory.CreateClient();
        var key = NewKey();
        var amount = NewAmount();
        var otherAmount = amount + 1;
        TrackAmount(otherAmount);
        using var firstResponse = await PostAsync(client, key, Body(amount, "EUR", "ideal"));
        var first = await ReadCreatedAsync(firstResponse);
        var recordBefore = await FindRecordAsync(key);
        Assert.NotNull(recordBefore);

        using var response = await PostAsync(client, key, Body(otherAmount, "EUR", "ideal"));

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, KeyReusedDetail);
        var paymentCount = await CountPaymentsAsync(amount) + await CountPaymentsAsync(otherAmount);
        var recordAfter = await FindRecordAsync(key);
        Assert.NotNull(recordAfter);
        Assert.Multiple(
            () => Assert.Equal(1, paymentCount),
            () => Assert.Equal(first.PaymentId, recordAfter.PaymentId),
            () => Assert.Equal(recordBefore.RequestHash, recordAfter.RequestHash),
            () => Assert.Equal(recordBefore.CreatedAt, recordAfter.CreatedAt));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreatePayment_MissingOrEmptyKey_Returns400WithKeyError(string? key)
    {
        using var client = factory.CreateClient();
        var amount = NewAmount();

        using var response = await PostAsync(client, key, Body(amount, "EUR", "ideal"));

        var errors = await ReadValidationErrorsAsync(response);
        var paymentCount = await CountPaymentsAsync(amount);
        Assert.Multiple(
            () => Assert.Equal(KeyHeader, Assert.Single(errors.EnumerateObject()).Name),
            () => Assert.Equal(KeyRequiredMessage, Assert.Single(errors.GetProperty(KeyHeader).EnumerateArray()).GetString()),
            () => Assert.Equal(0, paymentCount));
    }

    [Fact]
    public async Task CreatePayment_KeyOf100Characters_Returns201()
    {
        using var client = factory.CreateClient();
        var key = NewKey(length: 100);

        using var response = await PostAsync(client, key, Body(NewAmount(), "EUR", "ideal"));
        var created = await ReadCreatedAsync(response);

        var record = await FindRecordAsync(key);
        Assert.NotNull(record);
        Assert.Multiple(
            () => Assert.Equal(key, record.Key),
            () => Assert.Equal(created.PaymentId, record.PaymentId));
    }

    [Fact]
    public async Task CreatePayment_MissingKeyAndInvalidField_Returns400WithBothErrors()
    {
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, null, """{"amount":0,"currency":"EUR","method":"ideal"}""");

        var errors = await ReadValidationErrorsAsync(response);
        Assert.Multiple(
            () => Assert.Equal(2, errors.EnumerateObject().Count()),
            () => Assert.Equal(KeyRequiredMessage, Assert.Single(errors.GetProperty(KeyHeader).EnumerateArray()).GetString()),
            () => Assert.Equal(
                "Amount must be greater than 0.",
                Assert.Single(errors.GetProperty("amount").EnumerateArray()).GetString()));
    }

    [Fact]
    public async Task CreatePayment_InvalidRequestThenValidRequestSameKey_Returns400Then201()
    {
        using var client = factory.CreateClient();
        var key = NewKey();
        var amount = NewAmount();

        using var invalidResponse = await PostAsync(client, key, """{"amount":0,"currency":"EUR","method":"ideal"}""");
        var errors = await ReadValidationErrorsAsync(invalidResponse);
        var recordAfterInvalid = await FindRecordAsync(key);

        using var validResponse = await PostAsync(client, key, Body(amount, "EUR", "ideal"));
        var created = await ReadCreatedAsync(validResponse);

        var paymentCount = await CountPaymentsAsync(amount);
        var record = await FindRecordAsync(key);
        Assert.NotNull(record);
        Assert.Multiple(
            () => Assert.Equal("amount", Assert.Single(errors.EnumerateObject()).Name),
            () => Assert.Null(recordAfterInvalid),
            () => Assert.Equal(1, paymentCount),
            () => Assert.Equal(created.PaymentId, record.PaymentId));
    }

    [Fact]
    public async Task CreatePayment_SameKeyAfter24Hours_Returns201WithNewPayment()
    {
        using var client = factory.CreateClient();
        var key = NewKey();
        var amount = NewAmount();
        var body = Body(amount, "EUR", "ideal");
        using var firstResponse = await PostAsync(client, key, body);
        var first = await ReadCreatedAsync(firstResponse);
        var expiredCreatedAt = DateTimeOffset.UtcNow - TimeSpan.FromHours(24) - TimeSpan.FromMinutes(1);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlAsync(
                $"""UPDATE "IdempotencyRecords" SET "CreatedAt" = {expiredCreatedAt} WHERE "Key" = {key}""");
        }

        using var secondResponse = await PostAsync(client, key, body);
        var second = await ReadCreatedAsync(secondResponse);

        var paymentCount = await CountPaymentsAsync(amount);
        var oldPaymentExists = await PaymentExistsAsync(first.PaymentId);
        var record = await FindRecordAsync(key);
        Assert.NotNull(record);
        Assert.Multiple(
            () => Assert.NotEqual(first.PaymentId, second.PaymentId),
            () => Assert.Equal(2, paymentCount),
            () => Assert.True(oldPaymentExists),
            () => Assert.Equal(second.PaymentId, record.PaymentId),
            () => Assert.True(record.CreatedAt > expiredCreatedAt + TimeSpan.FromHours(23)),
            () => Assert.False(record.IsExpired(DateTimeOffset.UtcNow)));
    }

    [Fact]
    public async Task CreatePayment_KeysDifferOnlyInCase_CreatesTwoPayments()
    {
        using var client = factory.CreateClient();
        var key = $"Key-{_fixture.Create<Guid>():N}";
        var lowerKey = key.ToLowerInvariant();
        var upperKey = key.ToUpperInvariant();
        _keys.AddRange([lowerKey, upperKey]);
        var amount = NewAmount();
        var body = Body(amount, "EUR", "ideal");

        using var lowerResponse = await PostAsync(client, lowerKey, body);
        var lower = await ReadCreatedAsync(lowerResponse);
        using var upperResponse = await PostAsync(client, upperKey, body);
        var upper = await ReadCreatedAsync(upperResponse);

        var paymentCount = await CountPaymentsAsync(amount);
        var lowerRecord = await FindRecordAsync(lowerKey);
        var upperRecord = await FindRecordAsync(upperKey);
        Assert.Multiple(
            () => Assert.NotEqual(lower.PaymentId, upper.PaymentId),
            () => Assert.Equal(2, paymentCount),
            () => Assert.Equal(lower.PaymentId, lowerRecord?.PaymentId),
            () => Assert.Equal(upper.PaymentId, upperRecord?.PaymentId));
    }

    [Fact]
    public async Task CreatePayment_ReplayAfterStatusChange_ReturnsFirstResponseWithPending()
    {
        using var client = factory.CreateClient();
        var key = NewKey();
        var body = Body(NewAmount(), "EUR", "klarna");
        using var firstResponse = await PostAsync(client, key, body);
        var first = await ReadCreatedAsync(firstResponse);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var payment = await db.Payments.SingleAsync(p => p.Id == first.PaymentId);
            Assert.True(payment.TryChangeStatus(PaymentStatus.Succeeded));
            await db.SaveChangesAsync();
        }

        using var replayResponse = await PostAsync(client, key, body);
        var replay = await ReadCreatedAsync(replayResponse);

        Assert.Multiple(
            () => Assert.Equal(first.PaymentId, replay.PaymentId),
            () => Assert.Equal(first.RedirectUrl, replay.RedirectUrl),
            () => Assert.Equal("Pending", replay.Status));
    }

    [Fact]
    public async Task CreatePayment_ConcurrentSameKey_CreatesOnePayment()
    {
        var key = NewKey();
        var amount = NewAmount();
        var body = Body(amount, "EUR", "ideal");
        var coordinator = new IdempotencyRaceCoordinator();
        await using var raceFactory = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddScoped<IIdempotencyRecordRepository>(provider => new RacingIdempotencyRecordRepository(
                new IdempotencyRecordRepository(provider.GetRequiredService<AppDbContext>()),
                coordinator))));
        using var client = raceFactory.CreateClient();

        // The coordinator makes both requests look up the key before either one saves.
        var responses = await Task.WhenAll(PostAsync(client, key, body), PostAsync(client, key, body));
        var results = new List<(HttpStatusCode StatusCode, string Content)>();
        foreach (var response in responses)
        {
            results.Add((response.StatusCode, await response.Content.ReadAsStringAsync()));
            response.Dispose();
        }

        var createdIds = results
            .Where(r => r.StatusCode == HttpStatusCode.Created)
            .Select(r => ParsePaymentId(r.Content))
            .ToList();
        _paymentIds.AddRange(createdIds);
        var conflicts = results.Where(r => r.StatusCode == HttpStatusCode.Conflict).ToList();
        var paymentCount = await CountPaymentsAsync(amount);
        var record = await FindRecordAsync(key);
        var statuses = string.Join(", ", results.Select(r => (int)r.StatusCode));
        // Both requests found no record, so the save that runs second loses on the unique key and gets 409 (no retry).
        Assert.Multiple(
            () => Assert.Equal(2, coordinator.LookupCount),
            () => Assert.Equal(1, paymentCount),
            () => Assert.True(createdIds.Count == 1, $"Expected one 201 and one 409; got {statuses}."),
            () => Assert.Equal(createdIds.FirstOrDefault(), record?.PaymentId),
            () => AssertProblemContent(
                Assert.Single(conflicts).Content,
                HttpStatusCode.Conflict,
                KeyInProgressDetail));
    }

    [Fact]
    public async Task SaveAsync_KeyAlreadyStored_ReturnsKeyConflictAndSavesNothing()
    {
        var key = NewKey();
        var firstPayment = NewPayment();
        var secondPayment = NewPayment();
        var now = DateTimeOffset.UtcNow;

        var firstResult = await SaveInNewScopeAsync(new IdempotencyRecord(key, Hash(firstPayment), firstPayment.Id, now), firstPayment);
        var secondResult = await SaveInNewScopeAsync(new IdempotencyRecord(key, Hash(secondPayment), secondPayment.Id, now), secondPayment);

        var firstExists = await PaymentExistsAsync(firstPayment.Id);
        var secondExists = await PaymentExistsAsync(secondPayment.Id);
        var record = await FindRecordAsync(key);
        Assert.Multiple(
            () => Assert.Equal(IdempotencySaveResult.Saved, firstResult),
            () => Assert.Equal(IdempotencySaveResult.KeyConflict, secondResult),
            () => Assert.True(firstExists),
            () => Assert.False(secondExists),
            () => Assert.Equal(firstPayment.Id, record?.PaymentId));
    }

    [Fact]
    public async Task SaveAsync_ExpiredRecordRenewedByOtherScope_ReturnsKeyConflictAndSavesNothing()
    {
        var key = NewKey();
        var oldPayment = NewPayment();
        var paymentA = NewPayment();
        var paymentB = NewPayment();
        var now = DateTimeOffset.UtcNow;
        var seedResult = await SaveInNewScopeAsync(
            new IdempotencyRecord(key, Hash(oldPayment), oldPayment.Id, now - TimeSpan.FromHours(25)),
            oldPayment);
        await using var scopeA = factory.Services.CreateAsyncScope();
        await using var scopeB = factory.Services.CreateAsyncScope();
        var repositoryA = scopeA.ServiceProvider.GetRequiredService<IIdempotencyRecordRepository>();
        var repositoryB = scopeB.ServiceProvider.GetRequiredService<IIdempotencyRecordRepository>();

        // Both scopes load the expired record before either one saves its renewal.
        var recordA = await repositoryA.FindAsync(key, CancellationToken.None);
        var recordB = await repositoryB.FindAsync(key, CancellationToken.None);
        Assert.NotNull(recordA);
        Assert.NotNull(recordB);
        recordA.Renew(Hash(paymentA), paymentA.Id, now);
        recordB.Renew(Hash(paymentB), paymentB.Id, now);
        var resultA = await repositoryA.SaveAsync(recordA, paymentA, CancellationToken.None);
        var resultB = await repositoryB.SaveAsync(recordB, paymentB, CancellationToken.None);

        var paymentAExists = await PaymentExistsAsync(paymentA.Id);
        var paymentBExists = await PaymentExistsAsync(paymentB.Id);
        var record = await FindRecordAsync(key);
        Assert.Multiple(
            () => Assert.Equal(IdempotencySaveResult.Saved, seedResult),
            () => Assert.Equal(IdempotencySaveResult.Saved, resultA),
            () => Assert.Equal(IdempotencySaveResult.KeyConflict, resultB),
            () => Assert.True(paymentAExists),
            () => Assert.False(paymentBExists),
            () => Assert.Equal(paymentA.Id, record?.PaymentId),
            () => Assert.Equal(Hash(paymentA), record?.RequestHash));
    }

    private string NewKey(int length = 36)
    {
        var key = _fixture.Create<Guid>().ToString().PadRight(length, 'k');
        _keys.Add(key);
        return key;
    }

    private decimal NewAmount()
    {
        var amount = _fixture.Create<int>() + 0.50m;
        TrackAmount(amount);
        return amount;
    }

    private void TrackAmount(decimal amount) => _amounts.Add(amount);

    private Payment NewPayment()
    {
        var payment = new Payment(NewAmount(), _fixture.Create<Currency>(), _fixture.Create<PaymentMethod>());
        _paymentIds.Add(payment.Id);
        return payment;
    }

    private static string Hash(Payment payment) =>
        CreatePaymentRequestHash.Compute(payment.Amount, payment.Currency, payment.Method);

    private static string Body(decimal amount, string currency, string method) =>
        $$"""{"amount":{{amount.ToString(CultureInfo.InvariantCulture)}},"currency":"{{currency}}","method":"{{method}}"}""";

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string? key, string json)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/payments")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation(KeyHeader, key);
        }

        return await client.SendAsync(request);
    }

    private async Task<(Guid PaymentId, string? RedirectUrl, string? Status)> ReadCreatedAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode == HttpStatusCode.Created,
            $"Expected 201 but got {(int)response.StatusCode}: {content}");
        using var body = JsonDocument.Parse(content);
        var paymentId = body.RootElement.GetProperty("paymentId").GetGuid();
        _paymentIds.Add(paymentId);
        return (
            paymentId,
            body.RootElement.GetProperty("redirectUrl").GetString(),
            body.RootElement.GetProperty("status").GetString());
    }

    private static Guid ParsePaymentId(string content)
    {
        using var body = JsonDocument.Parse(content);
        return body.RootElement.GetProperty("paymentId").GetGuid();
    }

    private static async Task<JsonElement> ReadValidationErrorsAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest,
            $"Expected 400 but got {(int)response.StatusCode}: {content}");
        using var problem = JsonDocument.Parse(content);
        Assert.Multiple(
            () => Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType),
            () => Assert.Equal("One or more validation errors occurred.", problem.RootElement.GetProperty("title").GetString()),
            () => Assert.Equal(400, problem.RootElement.GetProperty("status").GetInt32()));
        return problem.RootElement.GetProperty("errors").Clone();
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expectedStatus, string expectedDetail)
    {
        var content = await response.Content.ReadAsStringAsync();
        Assert.Multiple(
            () => Assert.Equal(expectedStatus, response.StatusCode),
            () => Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType),
            () => AssertProblemContent(content, expectedStatus, expectedDetail));
    }

    private static void AssertProblemContent(string content, HttpStatusCode expectedStatus, string expectedDetail)
    {
        using var problem = JsonDocument.Parse(content);
        Assert.Multiple(
            () => Assert.Equal((int)expectedStatus, problem.RootElement.GetProperty("status").GetInt32()),
            () => Assert.Equal(expectedDetail, problem.RootElement.GetProperty("detail").GetString()));
    }

    private async Task<IdempotencySaveResult> SaveInNewScopeAsync(IdempotencyRecord record, Payment payment)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IIdempotencyRecordRepository>();
        return await repository.SaveAsync(record, payment, CancellationToken.None);
    }

    private async Task<int> CountPaymentsAsync(decimal amount)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Payments.CountAsync(p => p.Amount == amount);
    }

    private async Task<bool> PaymentExistsAsync(Guid paymentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Payments.AnyAsync(p => p.Id == paymentId);
    }

    private async Task<IdempotencyRecord?> FindRecordAsync(string key)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(r => r.Key == key);
    }
}
