using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microting.EntityFrameworkCore.MySql.Infrastructure;

namespace SaucyBot.Database;

public sealed class DatabaseContextFactory : IDesignTimeDbContextFactory<DatabaseContext>
{
    public DatabaseContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<DatabaseContext>()
            .UseSnakeCaseNamingConvention()
            .UseMySql(
                "server=localhost;port=3306;database=bot;user=root;password=",
                ServerVersion.Create(new Version(10, 6, 18), ServerType.MariaDb));

        return new DatabaseContext(builder.Options);
    }
}
