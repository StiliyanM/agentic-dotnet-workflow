namespace AgenticPayments.IntegrationTests.Infrastructure;

// One PostgreSQL container for all test classes in this collection.
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "Api";
}
