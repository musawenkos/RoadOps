using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;
using Xunit;

namespace RoadOps.Tests.Shared;

/// <summary>
/// Hosts a RoadOps web app (the REST API or the MCP server) in memory against a real PostgreSQL database.
///
/// By default a throwaway PostgreSQL container is started with Testcontainers (Docker required).
/// Set ROADOPS_TEST_CONNECTION to point the tests at an existing database instead,
/// e.g. the one started by docker-compose. Migrations are applied on startup.
/// </summary>
public abstract class PostgresAppFactory<TEntryPoint> : WebApplicationFactory<TEntryPoint>, IAsyncLifetime
    where TEntryPoint : class
{
    private readonly PostgreSqlContainer? _container;
    private string _connectionString = Environment.GetEnvironmentVariable("ROADOPS_TEST_CONNECTION") ?? string.Empty;

    protected PostgresAppFactory()
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
