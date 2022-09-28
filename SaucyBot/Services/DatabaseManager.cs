using Microsoft.EntityFrameworkCore;
using SaucyBot.Database;

namespace SaucyBot.Services;

public sealed class DatabaseManager
{
    private readonly IServiceProvider _provider;
    
    public DatabaseManager(IServiceProvider provider)
    {
        _provider = provider;
    }

    public async Task<int?> EnsureAllMigrationsHaveRun()
    {
        await using var context = Context();

        var pendingMigrations = await context.Database.GetPendingMigrationsAsync();

        var migrations = pendingMigrations as string[] ?? pendingMigrations.ToArray();
        
        if (migrations.Any())
        {
            await context.Database.MigrateAsync();
        }

        return migrations.Length;
    }

    private DatabaseContext CreateDatabaseContext()
    {
        return _provider.GetRequiredService<DatabaseContext>();
    }

    public DatabaseContext Context() => CreateDatabaseContext();
}
