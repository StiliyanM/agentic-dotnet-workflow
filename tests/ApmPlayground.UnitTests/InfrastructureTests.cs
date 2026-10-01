using AutoFixture;

namespace ApmPlayground.UnitTests;

// Baseline smoke test: proves xUnit + AutoFixture work. The test-writer can delete it when real unit tests exist.
public class InfrastructureTests
{
    private readonly Fixture _fixture = new();

    [Fact]
    public void Fixture_CreatesDistinctValues()
    {
        var first = _fixture.Create<string>();
        var second = _fixture.Create<string>();

        Assert.NotEqual(first, second);
    }
}
