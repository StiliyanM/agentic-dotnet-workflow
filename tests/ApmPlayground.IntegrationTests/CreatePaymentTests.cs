using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ApmPlayground.Application.Payments;
using ApmPlayground.Domain.Payments;
using ApmPlayground.Infrastructure.Persistence;
using ApmPlayground.IntegrationTests.Infrastructure;
using AutoFixture;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApmPlayground.IntegrationTests;

[Collection(ApiCollection.Name)]
public class CreatePaymentTests(ApiFactory factory) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions ResponseJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Fixture _fixture = new();
    private readonly List<Guid> _createdPaymentIds = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_createdPaymentIds.Count == 0)
        {
            return;
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Payments.Where(p => _createdPaymentIds.Contains(p.Id)).ExecuteDeleteAsync();
    }

    [Theory]
    [InlineData("ideal")]
    [InlineData("klarna")]
    public async Task CreatePayment_ValidRequest_Returns201WithPendingStatusAndRedirectUrl(string method)
    {
        using var client = factory.CreateClient();

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

    [Theory]
    [InlineData("ideal", PaymentMethod.Ideal, "EUR", Currency.EUR)]
    [InlineData("klarna", PaymentMethod.Klarna, "USD", Currency.USD)]
    public async Task CreatePayment_ValidRequest_PersistsPayment(
        string method,
        PaymentMethod expectedMethod,
        string currency,
        Currency expectedCurrency)
    {
        using var client = factory.CreateClient();
        var amount = ValidAmount();

        using var response = await client.PostAsJsonAsync("/payments", new { amount, currency, method });
        var body = await ReadCreatedPaymentAsync(response);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var payment = await db.Payments.AsNoTracking().SingleAsync(p => p.Id == body.PaymentId);

        Assert.Multiple(
            () => Assert.Equal(amount, payment.Amount),
            () => Assert.Equal(expectedCurrency, payment.Currency),
            () => Assert.Equal(expectedMethod, payment.Method),
            () => Assert.Equal(PaymentStatus.Pending, payment.Status));
    }

    [Fact]
    public async Task CreatePayment_TwoRequests_ReturnDifferentPaymentIds()
    {
        using var client = factory.CreateClient();
        var method = SupportedMethod();

        using var firstResponse = await client.PostAsJsonAsync("/payments", ValidBody(method));
        var first = await ReadCreatedPaymentAsync(firstResponse);
        using var secondResponse = await client.PostAsJsonAsync("/payments", ValidBody(method));
        var second = await ReadCreatedPaymentAsync(secondResponse);

        Assert.NotEqual(first.PaymentId, second.PaymentId);
    }

    [Theory]
    [InlineData("IDEAL", "eur", "ideal", PaymentMethod.Ideal, Currency.EUR)]
    [InlineData("Ideal", "Eur", "ideal", PaymentMethod.Ideal, Currency.EUR)]
    [InlineData("KLARNA", "gbp", "klarna", PaymentMethod.Klarna, Currency.GBP)]
    [InlineData("Klarna", "Usd", "klarna", PaymentMethod.Klarna, Currency.USD)]
    public async Task CreatePayment_MethodAndCurrencyInAnyCase_Returns201(
        string method,
        string currency,
        string expectedSegment,
        PaymentMethod expectedMethod,
        Currency expectedCurrency)
    {
        using var client = factory.CreateClient();

        using var response = await PostRawAsync(
            client,
            $"{{\"amount\":{RawAmount()},\"currency\":\"{currency}\",\"method\":\"{method}\"}}");
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

    [Fact]
    public async Task CreatePayment_AmountZero_Returns400WithAmountError()
    {
        using var client = factory.CreateClient();
        var method = SupportedMethod();

        using var response = await client.PostAsJsonAsync(
            "/payments",
            new { amount = 0m, currency = SupportedCurrency(), method });

        await AssertValidationProblemAsync(response, "amount", "Amount must be greater than 0.");
    }

    [Fact]
    public async Task CreatePayment_NegativeAmount_Returns400WithAmountError()
    {
        using var client = factory.CreateClient();
        var method = SupportedMethod();

        using var response = await client.PostAsJsonAsync(
            "/payments",
            new { amount = -ValidAmount(), currency = SupportedCurrency(), method });

        await AssertValidationProblemAsync(response, "amount", "Amount must be greater than 0.");
    }

    [Fact]
    public async Task CreatePayment_AmountAboveMaximum_Returns400WithAmountError()
    {
        using var client = factory.CreateClient();
        var method = SupportedMethod();

        using var response = await client.PostAsJsonAsync(
            "/payments",
            new { amount = 10000000000000000m, currency = SupportedCurrency(), method });

        await AssertValidationProblemAsync(response, "amount", "Amount is too large.");
    }

    [Theory]
    [InlineData("currency", "\"JPY\"", "Currency has an invalid value.")]
    [InlineData("currency", "0", "Currency has an invalid value.")]
    [InlineData("currency", "null", "Currency has an invalid value.")]
    [InlineData("currency", "\"GBP, USD\"", "Currency has an invalid value.")]
    [InlineData("method", "\"ideal, klarna\"", "Method has an invalid value.")]
    [InlineData("method", "\"paypal\"", "Method has an invalid value.")]
    [InlineData("method", "1", "Method has an invalid value.")]
    [InlineData("method", "null", "Method has an invalid value.")]
    [InlineData("amount", "\"abc\"", "Amount has an invalid value.")]
    [InlineData("amount", "true", "Amount has an invalid value.")]
    [InlineData("amount", "null", "Amount has an invalid value.")]
    public async Task CreatePayment_FieldWithInvalidValue_Returns400WithInvalidValueError(
        string field,
        string jsonValue,
        string expectedMessage)
    {
        using var client = factory.CreateClient();

        using var response = await PostRawAsync(client, RawBody(field, jsonValue));

        await AssertValidationProblemAsync(response, field, expectedMessage);
    }

    [Theory]
    [InlineData("amount", "Amount is required.")]
    [InlineData("currency", "Currency is required.")]
    [InlineData("method", "Method is required.")]
    public async Task CreatePayment_MissingField_Returns400WithRequiredError(string field, string expectedMessage)
    {
        using var client = factory.CreateClient();

        using var response = await PostRawAsync(client, BodyWithout(field));

        await AssertValidationProblemAsync(response, field, expectedMessage);
    }

    [Fact]
    public async Task CreatePayment_EmptyObject_Returns400WithErrorForEachField()
    {
        using var client = factory.CreateClient();

        using var response = await PostRawAsync(client, "{}");

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
    public async Task CreatePayment_PropertyNameInOtherCase_ErrorKeyIsCamelCase()
    {
        using var client = factory.CreateClient();

        using var response = await PostRawAsync(
            client,
            $"{{\"amount\":{RawAmount()},\"currency\":\"{SupportedCurrency()}\",\"Method\":\"paypal\"}}");

        await AssertValidationProblemAsync(response, "method", "Method has an invalid value.");
    }

    [Fact]
    public async Task CreatePayment_MalformedJson_Returns400()
    {
        using var client = factory.CreateClient();

        using var response = await PostRawAsync(client, "{ \"amount\": 10.50, \"currency\": ");

        await AssertPlainProblemAsync(response);
    }

    [Fact]
    public async Task CreatePayment_EmptyBody_Returns400()
    {
        using var client = factory.CreateClient();

        using var response = await PostRawAsync(client, string.Empty);

        await AssertPlainProblemAsync(response);
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
