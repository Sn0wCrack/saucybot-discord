using SaucyBot.Database;

namespace SaucyBot.Services;

public interface IDatabaseManager
{
    public Task<int> EnsureAllMigrationsHaveRun();
    public DatabaseContext Context();
}
