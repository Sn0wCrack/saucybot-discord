using Microsoft.EntityFrameworkCore;
using SaucyBot.Database;

namespace SaucyBot.Services;

public sealed class DatabaseMigrator : IDatabaseMigrator
{
    private readonly IServiceScopeFactory _scopeFactory;

    public DatabaseMigrator(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<int> EnsureAllMigrationsHaveRun()
    {
        using var scope = _scopeFactory.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var pendingMigrations = await context.Database.GetPendingMigrationsAsync();

        var migrations = pendingMigrations.ToArray();

        if (migrations.Any())
        {
            await context.Database.MigrateAsync();
        }

        return migrations.Length;
    }
}
