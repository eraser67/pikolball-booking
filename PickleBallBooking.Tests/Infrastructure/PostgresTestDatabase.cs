using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PickleBallBooking.Data;

namespace PickleBallBooking.Tests.Infrastructure;

internal static class PostgresTestDatabase
{
    private static string? _connectionString;

    public static string ConnectionString => _connectionString ??= LoadConnectionString();

    public static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new ApplicationDbContext(options);
    }

    private static string LoadConnectionString()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets(typeof(ApplicationDbContext).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("A PostgreSQL connection string named 'DefaultConnection' is required for integration tests.");
        }

        return connectionString;
    }
}
