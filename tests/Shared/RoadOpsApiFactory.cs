using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;
using Xunit;

namespace RoadOps.Tests.Shared;

/// <summary>
/// Hosts the real RoadOps API in memory against a real PostgreSQL database.
///
/// By default a throwaway PostgreSQL container is started with Testcontainers (Docker required).
/// Set ROADOPS_TEST_CONNECTION to point the tests at an existing database instead,
/// e.g. the one started by docker-compose. Migrations are applied on startup.
/// </summary>
public sealed class RoadOpsApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer? _container;
    private string _connectionString = Environment.GetEnvironmentVariable("ROADOPS_TEST_CONNECTION") ?? string.Empty;

    public RoadOpsApiFactory()
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            _container = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("roadops_test")
                .WithUsername("roadops_user")
                .WithPassword("roadops_test_password")
                .Build();
        }
    }

    public string ConnectionString => _connectionString;

    public async Task InitializeAsync()
    {
        if (_container is not null)
        {
            await _container.StartAsync();
            _connectionString = _container.GetConnectionString();
        }

        // Force the host to start now so migrations run once, before any test.
        _ = Server;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", _connectionString);
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "true");
        builder.UseSetting("Logging:LogLevel:Default", "Warning");
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<RoadOpsApiFactory>
{
    public const string Name = "RoadOps API";
}
