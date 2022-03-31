using Microsoft.EntityFrameworkCore;

namespace SaucyBot.Database;

public class SqliteDatabaseContext : DatabaseContext
{
    private readonly string _connectionString;

    public SqliteDatabaseContext(string connectionString)
    {
        _connectionString = connectionString;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        base.OnConfiguring(options);

        options
            .UseSqlite(_connectionString);
    }
}
