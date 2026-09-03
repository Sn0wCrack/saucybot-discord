namespace SaucyBot.Services;

public sealed class NullDatabaseMigrator : IDatabaseMigrator
{
    public Task<int> EnsureAllMigrationsHaveRun()
    {
        return Task.FromResult(0);
    }
}
