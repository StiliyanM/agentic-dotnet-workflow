using AgenticPayments.Api;
using AgenticPayments.Api.Payments;
using AgenticPayments.Api.Webhooks;
using AgenticPayments.Application;
using AgenticPayments.Infrastructure;
using AgenticPayments.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new StrictEnumConverterFactory()));
builder.Services.AddProblemDetails();

// The secret is never committed; it comes from configuration outside the repository.
// Failing at start prevents a webhook endpoint that runs without a secret.
builder.Services.AddOptions<ProviderWebhookOptions>()
    .BindConfiguration(ProviderWebhookOptions.SectionName)
    .Validate(o => !string.IsNullOrWhiteSpace(o.Secret), "Webhooks:Provider:Secret is required.")
    .ValidateOnStart();

var app = builder.Build();

app.UseExceptionHandler();

using (var scope = app.Services.CreateScope())
{
    await DatabaseInitializer.InitializeAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), CancellationToken.None);
}

app.MapGet("/health", async (AppDbContext db, CancellationToken cancellationToken) =>
    await db.Database.CanConnectAsync(cancellationToken) ? Results.Ok() : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));

app.MapPaymentEndpoints();
app.MapWebhookEndpoints();

app.Run();
