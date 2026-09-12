using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SaucyBot.Database.Models;
using SaucyBot.Library;
using SaucyBot.Options;

namespace SaucyBot.Database;

public sealed class DatabaseContext : DbContext
{
    private readonly string _connectionString;

    public DbSet<GuildConfiguration> GuildConfigurations => Set<GuildConfiguration>();

    public DbSet<GuildConfigurationRestrictedRole> GuildConfigurationRestrictedRoles => Set<GuildConfigurationRestrictedRole>();

    public DbSet<GuildConfigurationDisabledSite> GuildConfigurationDisabledSites => Set<GuildConfigurationDisabledSite>();

    public DatabaseContext(IOptions<DatabaseOptions> databaseOptions)
    {
        _connectionString = databaseOptions.Value.ConnectionString;
    }

    internal DatabaseContext(DbContextOptions<DatabaseContext> options) : base(options)
    {
        _connectionString = null!;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        base.OnConfiguring(options);

        if (!options.IsConfigured)
        {
            var version = ServerVersion.AutoDetect(_connectionString);

            options
                .UseSnakeCaseNamingConvention()
                .UseMySql(_connectionString, version);
        }

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
