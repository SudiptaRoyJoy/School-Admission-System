using Testcontainers.PostgreSql;
using Xunit;

namespace Admission.Tests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("admission_test")
        .WithUsername("admission_test")
        .WithPassword("admission_test_password")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition("postgres", DisableParallelization = true)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
}
