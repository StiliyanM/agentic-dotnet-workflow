using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
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

        Assert.Multiple(
            () => Assert.Equal(HttpStatusCode.Created, response.StatusCode),
            () => Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType),
            () => Assert.Null(response.Headers.Location),
            () => Assert.NotEqual(Guid.Empty, body.PaymentId),
            () => Assert.Equal("Pending", body.Status),
            () => Assert.Equal($"https://pay.example.com/{method}/{body.PaymentId:D}", body.RedirectUrl));
    }

    [Theory]
    [InlineData("ideal", PaymentMethod.Ideal)]
    [InlineData("klarna", PaymentMethod.Klarna)]
    public async Task CreatePayment_ValidRequest_PersistsPayment(string method, PaymentMethod expectedMethod)
    {
        using var client = factory.CreateClient();
        var amount = ValidAmount();
        var currency = SupportedCurrency();

        using var response = await client.PostAsJsonAsync("/payments", new { amount, currency, method });
        var body = await ReadCreatedPaymentAsync(response);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var payment = await db.Payments.AsNoTracking().SingleAsync(p => p.Id == body.PaymentId);

        Assert.Multiple(
            () => Assert.Equal(amount, payment.Amount),
            () => Assert.Equal(currency, payment.Currency),
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

    [Fact]
    public async Task CreatePayment_UnsupportedCurrency_Returns400WithCurrencyError()
    {
        using var client = factory.CreateClient();
        var method = SupportedMethod();
        var currency = _fixture.Create<string>();

        using var response = await client.PostAsJsonAsync(
            "/payments",
            new { amount = ValidAmount(), currency, method });

        await AssertValidationProblemAsync(response, "currency", $"Currency '{currency}' is not supported.");
    }

    [Fact]
    public async Task CreatePayment_UnknownMethod_Returns400WithMethodError()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/payments",
            new { amount = ValidAmount(), currency = SupportedCurrency(), method = _fixture.Create<string>() });

        await AssertValidationProblemAsync(response, "method", "Method must be 'ideal' or 'klarna'.");
    }

    [Fact]
    public async Task CreatePayment_MalformedJson_Returns400()
    {
        using var client = factory.CreateClient();
        using var content = new StringContent("{ \"amount\": 10.50, \"currency\": ", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/payments", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private object ValidBody(string method) =>
        new { amount = ValidAmount(), currency = SupportedCurrency(), method };

    private decimal ValidAmount() => _fixture.Create<int>() + 0.50m;

    private string SupportedCurrency() => PickOne("EUR", "GBP", "USD");

    private string SupportedMethod() => PickOne("ideal", "klarna");

    private string PickOne(params string[] values) => values[_fixture.Create<int>() % values.Length];

    private async Task<CreatePaymentResponse> ReadCreatedPaymentAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CreatePaymentResponse>();
        Assert.NotNull(body);
        _createdPaymentIds.Add(body.PaymentId);
        return body;
    }

    private static async Task AssertValidationProblemAsync(HttpResponseMessage response, string field, string message)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = problem.RootElement.GetProperty("errors");

        Assert.Multiple(
            () => Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode),
            () => Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType),
            () => Assert.Equal(400, problem.RootElement.GetProperty("status").GetInt32()),
            () => Assert.Equal(message, Assert.Single(errors.GetProperty(field).EnumerateArray()).GetString()));
    }
}
