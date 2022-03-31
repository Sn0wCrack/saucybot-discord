using Microsoft.EntityFrameworkCore;

namespace SaucyBot.Database;

public sealed class MySqlDatabaseContext : DatabaseContext
{
    private readonly string _connectionString;
    
    public MySqlDatabaseContext(string connectionString)
    {
        _connectionString = connectionString;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        base.OnConfiguring(options);

        var version = ServerVersion.AutoDetect(_connectionString);

        options
            .UseMySql(_connectionString, version);
    }
}
