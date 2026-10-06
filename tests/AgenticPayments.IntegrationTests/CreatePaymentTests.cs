using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgenticPayments.Application.Payments;
using AgenticPayments.Domain.Payments;
using AgenticPayments.Infrastructure.Persistence;
using AgenticPayments.IntegrationTests.Infrastructure;
using AutoFixture;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgenticPayments.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class CreatePaymentTests(ApiFactory factory) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions ResponseJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Fixture _fixture = new();
    private readonly List<Guid> _createdPaymentIds = [];
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

        if (_createdPaymentIds.Count > 0)
        {
            await db.Payments.Where(p => _createdPaymentIds.Contains(p.Id)).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task CreatePayment_ValidRequest_Returns201WithPendingStatusAndRedirectUrl()
    {
        using var client = CreateClientWithNewKey();
        var method = SupportedMethod();

        using var response = await client.PostAsJsonAsync("/payments", ValidBody(method));
        var body = await ReadCreatedPaymentAsync(response);
        using var raw = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Multiple(
            () => Assert.Equal(HttpStatusCode.Created, response.StatusCode),
            () => Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType),
            () => Assert.Null(response.Headers.Location),
            () => Assert.NotEqual(Guid.Empty, body.PaymentId),
            () => Assert.Equal(JsonValueKind.String, raw.RootElement.GetProperty("status").ValueKind),
            () => Assert.Equal("Pending", raw.RootElement.GetProperty("status").GetString()),
            () => Assert.Equal(
                $"https://pay.example.com/{method}/{body.PaymentId:D}",
                raw.RootElement.GetProperty("redirectUrl").GetString()));
    }

    [Fact]
    public async Task CreatePayment_ValidRequest_PersistsPayment()
    {
        using var client = CreateClientWithNewKey();
        var amount = ValidAmount();

        using var response = await client.PostAsJsonAsync("/payments", new { amount, currency = "USD", method = "klarna" });
        var body = await ReadCreatedPaymentAsync(response);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var payment = await db.Payments.AsNoTracking().SingleAsync(p => p.Id == body.PaymentId);

        Assert.Multiple(
            () => Assert.Equal(amount, payment.Amount),
            () => Assert.Equal(Currency.Usd, payment.Currency),
            () => Assert.Equal(PaymentMethod.Klarna, payment.Method),
            () => Assert.Equal(PaymentStatus.Pending, payment.Status));
    }

    [Theory]
    [InlineData("IDEAL", "eur", "ideal", PaymentMethod.Ideal, Currency.Eur)]
    [InlineData("Ideal", "Eur", "ideal", PaymentMethod.Ideal, Currency.Eur)]
    [InlineData("KLARNA", "gbp", "klarna", PaymentMethod.Klarna, Currency.Gbp)]
    [InlineData("Klarna", "Usd", "klarna", PaymentMethod.Klarna, Currency.Usd)]
    public async Task CreatePayment_MethodAndCurrencyInAnyCase_Returns201(
        string method,
        string currency,
        string expectedSegment,
        PaymentMethod expectedMethod,
        Currency expectedCurrency)
    {
        using var client = CreateClientWithNewKey();

        using var response = await PostRawAsync(
            client,
            $$"""{"amount":{{RawAmount()}},"currency":"{{currency}}","method":"{{method}}"}""");
        var body = await ReadCreatedPaymentAsync(response);
        using var raw = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var payment = await db.Payments.AsNoTracking().SingleAsync(p => p.Id == body.PaymentId);

        Assert.Multiple(
            () => Assert.Equal(
                $"https://pay.example.com/{expectedSegment}/{body.PaymentId:D}",
                raw.RootElement.GetProperty("redirectUrl").GetString()),
            () => Assert.Equal(expectedMethod, payment.Method),
            () => Assert.Equal(expectedCurrency, payment.Currency));
    }

    [Theory]
    [InlineData("EUR", "EUR")]
    [InlineData("Gbp", "GBP")]
    [InlineData("usd", "USD")]
    public async Task CreatePayment_Currency_StoresIsoCodeInColumn(string currency, string expectedColumnValue)
    {
        using var client = CreateClientWithNewKey();

        using var response = await client.PostAsJsonAsync(
            "/payments",
            new { amount = ValidAmount(), currency, method = SupportedMethod() });
        var body = await ReadCreatedPaymentAsync(response);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var columnValue = await db.Database
            .SqlQuery<string>($"""SELECT "Currency" AS "Value" FROM "Payments" WHERE "Id" = {body.PaymentId}""")
            .SingleAsync();

        Assert.Equal(expectedColumnValue, columnValue);
    }

    [Fact]
    public async Task CreatePayment_AmountZero_Returns400WithAmountError()
    {
        using var client = CreateClientWithNewKey();
        var method = SupportedMethod();

        using var response = await client.PostAsJsonAsync(
            "/payments",
            new { amount = 0m, currency = SupportedCurrency(), method });

        await AssertValidationProblemAsync(response, "amount", "Amount must be greater than 0.");
    }

    // Binding cases: each row proves one JSON reader or enum converter behavior that a validator test cannot prove.
    [Theory]
    [InlineData("currency", "\"JPY\"", "Currency has an invalid value.")]
    [InlineData("currency", "0", "Currency has an invalid value.")]
    [InlineData("currency", "null", "Currency has an invalid value.")]
    [InlineData("method", "\"ideal, klarna\"", "Method has an invalid value.")]
    [InlineData("method", "\"1\"", "Method has an invalid value.")]
    [InlineData("amount", "\"abc\"", "Amount has an invalid value.")]
    [InlineData("amount", "true", "Amount has an invalid value.")]
    [InlineData("amount", "null", "Amount has an invalid value.")]
    public async Task CreatePayment_FieldWithInvalidValue_Returns400WithInvalidValueError(
        string field,
        string jsonValue,
        string expectedMessage)
    {
        using var client = CreateClientWithNewKey();

        using var response = await PostRawAsync(client, RawBody(field, jsonValue));

        await AssertValidationProblemAsync(response, field, expectedMessage);
    }

    [Theory]
    [InlineData("amount", "Amount is required.")]
    [InlineData("currency", "Currency is required.")]
    [InlineData("method", "Method is required.")]
    public async Task CreatePayment_MissingField_Returns400WithRequiredError(string field, string expectedMessage)
    {
        using var client = CreateClientWithNewKey();

        using var response = await PostRawAsync(client, BodyWithout(field));

        await AssertValidationProblemAsync(response, field, expectedMessage);
    }

    [Fact]
    public async Task CreatePayment_EmptyObject_Returns400WithErrorForEachField()
    {
        using var client = CreateClientWithNewKey();

        using var response = await PostRawAsync(client, """{}""");

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = problem.RootElement.GetProperty("errors");
        Assert.Multiple(
            () => Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode),
            () => Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType),
            () => Assert.Equal(400, problem.RootElement.GetProperty("status").GetInt32()),
            () => Assert.Equal(3, errors.EnumerateObject().Count()),
            () => Assert.Equal("Amount is required.", Assert.Single(errors.GetProperty("amount").EnumerateArray()).GetString()),
            () => Assert.Equal("Currency is required.", Assert.Single(errors.GetProperty("currency").EnumerateArray()).GetString()),
            () => Assert.Equal("Method is required.", Assert.Single(errors.GetProperty("method").EnumerateArray()).GetString()));
    }

    [Fact]
    public async Task CreatePayment_MissingFieldAndInvalidValue_Returns400WithBothErrors()
    {
        using var client = CreateClientWithNewKey();

        using var response = await PostRawAsync(
            client,
            $$"""{"currency":"JPY","method":"{{SupportedMethod()}}"}""");

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = problem.RootElement.GetProperty("errors");
        Assert.Multiple(
            () => Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode),
            () => Assert.Equal(2, errors.EnumerateObject().Count()),
            () => Assert.Equal("Amount is required.", Assert.Single(errors.GetProperty("amount").EnumerateArray()).GetString()),
            () => Assert.Equal("Currency has an invalid value.", Assert.Single(errors.GetProperty("currency").EnumerateArray()).GetString()));
    }

    [Fact]
    public async Task CreatePayment_ContentTypeNotJson_Returns415()
    {
        using var client = CreateClientWithNewKey();
        using var content = new StringContent(
            $$"""{"amount":{{RawAmount()}},"currency":"{{SupportedCurrency()}}","method":"{{SupportedMethod()}}"}""",
            Encoding.UTF8,
            "text/plain");

        using var response = await client.PostAsync("/payments", content);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Multiple(
            () => Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode),
            () => Assert.Empty(body));
    }

    [Fact]
    public async Task CreatePayment_PropertyNameInOtherCase_ErrorKeyIsCamelCase()
    {
        using var client = CreateClientWithNewKey();

        using var response = await PostRawAsync(
            client,
            $$"""{"amount":{{RawAmount()}},"currency":"{{SupportedCurrency()}}","Method":"paypal"}""");

        await AssertValidationProblemAsync(response, "method", "Method has an invalid value.");
    }

    [Fact]
    public async Task CreatePayment_MalformedJson_Returns400()
    {
        using var client = CreateClientWithNewKey();

        using var response = await PostRawAsync(client, """{ "amount": 10.50, "currency": """);

        await AssertPlainProblemAsync(response);
    }

    [Fact]
    public async Task CreatePayment_EmptyBody_Returns400()
    {
        using var client = CreateClientWithNewKey();

        using var response = await PostRawAsync(client, string.Empty);

        await AssertPlainProblemAsync(response);
    }

    // Each test sends one request per client, so a new key per client gives every request its own Idempotency-Key.
    private HttpClient CreateClientWithNewKey()
    {
        var key = _fixture.Create<Guid>().ToString();
        _keys.Add(key);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Idempotency-Key", key);
        return client;
    }

    private object ValidBody(string method) =>
        new { amount = ValidAmount(), currency = SupportedCurrency(), method };

    private decimal ValidAmount() => _fixture.Create<int>() + 0.50m;

    private string SupportedCurrency() => PickOne("EUR", "GBP", "USD");

    private string SupportedMethod() => PickOne("ideal", "klarna");

    private string PickOne(params string[] values) => values[_fixture.Create<int>() % values.Length];

    private (string Field, string JsonValue)[] ValidRawFields() =>
    [
        ("amount", RawAmount()),
        ("currency", $"\"{SupportedCurrency()}\""),
        ("method", $"\"{SupportedMethod()}\""),
    ];

    private string RawAmount() => ValidAmount().ToString(CultureInfo.InvariantCulture);

    private string RawBody(string field, string jsonValue) =>
        ToJsonObject(ValidRawFields().Select(f => (f.Field, f.Field == field ? jsonValue : f.JsonValue)));

    private string BodyWithout(string field) =>
        ToJsonObject(ValidRawFields().Where(f => f.Field != field));

    private static string ToJsonObject(IEnumerable<(string Field, string JsonValue)> fields) =>
        "{" + string.Join(",", fields.Select(f => $"\"{f.Field}\":{f.JsonValue}")) + "}";

    private static async Task<HttpResponseMessage> PostRawAsync(HttpClient client, string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await client.PostAsync("/payments", content);
    }

    private async Task<CreatePaymentResponse> ReadCreatedPaymentAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CreatePaymentResponse>(ResponseJsonOptions);
        Assert.NotNull(body);
        _createdPaymentIds.Add(body.PaymentId);
        return body;
    }

    private async Task AssertValidationProblemAsync(HttpResponseMessage response, string field, string message)
    {
        var content = await response.Content.ReadAsStringAsync();
        using var problem = JsonDocument.Parse(content);
        if (response.StatusCode == HttpStatusCode.Created)
        {
            _createdPaymentIds.Add(problem.RootElement.GetProperty("paymentId").GetGuid());
        }

        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest,
            $"Expected 400 but got {(int)response.StatusCode}: {content}");
        var errors = problem.RootElement.GetProperty("errors");

        Assert.Multiple(
            () => Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode),
            () => Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType),
            () => Assert.Equal("One or more validation errors occurred.", problem.RootElement.GetProperty("title").GetString()),
            () => Assert.Equal(400, problem.RootElement.GetProperty("status").GetInt32()),
            () => Assert.Equal(field, Assert.Single(errors.EnumerateObject()).Name),
            () => Assert.Equal(message, Assert.Single(errors.GetProperty(field).EnumerateArray()).GetString()));
    }

    private static async Task AssertPlainProblemAsync(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Multiple(
            () => Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode),
            () => Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType),
            () => Assert.Equal(400, problem.RootElement.GetProperty("status").GetInt32()),
            () => Assert.False(problem.RootElement.TryGetProperty("errors", out _)));
    }
}
