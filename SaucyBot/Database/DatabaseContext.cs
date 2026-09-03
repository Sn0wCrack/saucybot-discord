using System.Reflection;
using Microsoft.EntityFrameworkCore;
using SaucyBot.Database.Models;
using SaucyBot.Library;

namespace SaucyBot.Database;

public sealed class DatabaseContext : DbContext
{
    private readonly string _connectionString;

    public DbSet<GuildConfiguration> GuildConfigurations => Set<GuildConfiguration>();

    public DbSet<GuildConfigurationRestrictedRole> GuildConfigurationRestrictedRoles => Set<GuildConfigurationRestrictedRole>();

    public DatabaseContext(IConfiguration configuration)
    {
        _connectionString = configuration.GetSection("Database:ConnectionString").Get<string>() ?? "";
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        base.OnConfiguring(options);

        var version = ServerVersion.AutoDetect(_connectionString);

        options
            .UseSnakeCaseNamingConvention()
            .UseMySql(_connectionString, version);

#if DEBUG
        options
            .EnableSensitiveDataLogging()
            .EnableDetailedErrors();
#endif
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Automatically loads all classes implementing IEntityTypeConfiguration in this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}
