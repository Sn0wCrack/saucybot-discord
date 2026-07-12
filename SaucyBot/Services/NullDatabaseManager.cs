using SaucyBot.Database;

namespace SaucyBot.Services;

public sealed class NullDatabaseManager : IDatabaseManager
{
    public Task<int> EnsureAllMigrationsHaveRun()
    {
        return Task.FromResult(0);
    }

    public DatabaseContext Context()
    {
        throw new InvalidOperationException("Database is disabled.");
    }
}
