using Xunit;

namespace RoadOps.Tests.Shared;

/// <summary>Hosts the real RoadOps REST API in memory against PostgreSQL (see <see cref="PostgresAppFactory{TEntryPoint}"/>).</summary>
public sealed class RoadOpsApiFactory : PostgresAppFactory<Program>
{
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<RoadOpsApiFactory>
{
    public const string Name = "RoadOps API";
}
