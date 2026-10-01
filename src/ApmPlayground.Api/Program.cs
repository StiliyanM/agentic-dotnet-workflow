using ApmPlayground.Api.Payments;
using ApmPlayground.Application;
using ApmPlayground.Infrastructure;
using ApmPlayground.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
}

app.MapGet("/health", async (AppDbContext db) =>
    await db.Database.CanConnectAsync() ? Results.Ok() : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));

app.MapPaymentEndpoints();

app.Run();
