namespace SaucyBot.Services;

public interface IDatabaseMigrator
{
    public Task<int> EnsureAllMigrationsHaveRun();
}
