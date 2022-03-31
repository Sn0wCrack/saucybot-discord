using SaucyBot.Database;

namespace SaucyBot.Services;

public class DatabaseManager
{
    private readonly string _type;
    private readonly string _connectionString;

    public DatabaseManager(IConfiguration configuration)
    {
        _type = configuration.GetSection("Database:Type").Get<string>().ToLowerInvariant().Trim();
        _connectionString = configuration.GetSection("Database:ConnectionString").Get<string>();
    }

    private DatabaseContext CreateDatabaseContext()
    {
        switch (_type)
        {
            case "mysql":
            case "mariadb":
                return new MySqlDatabaseContext(_connectionString);
            case "sqlite":
                return new SqliteDatabaseContext(_connectionString);
            default:
                throw new NotSupportedException($"{_type} is not a support Database Context");
        }
    }

    public DatabaseContext Context => CreateDatabaseContext();
}
