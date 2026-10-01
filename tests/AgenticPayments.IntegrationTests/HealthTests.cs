using System.Net;
using AgenticPayments.IntegrationTests.Infrastructure;

namespace AgenticPayments.IntegrationTests;

// Baseline smoke test: proves WebApplicationFactory + Testcontainers + PostgreSQL work together.
[Collection(ApiCollection.Name)]
public sealed class HealthTests(ApiFactory factory)
{
    [Fact]
    public async Task Health_WhenDatabaseIsReachable_ReturnsOk()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
