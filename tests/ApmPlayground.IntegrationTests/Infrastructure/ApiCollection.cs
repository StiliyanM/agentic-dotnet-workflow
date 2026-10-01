namespace ApmPlayground.IntegrationTests.Infrastructure;

// One PostgreSQL container for all test classes in this collection.
[CollectionDefinition(Name)]
public class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "Api";
}
