using Microsoft.EntityFrameworkCore;
using SaucyBot.Database.Models;

namespace SaucyBot.Database;

public abstract class DatabaseContext: DbContext
{
    public DbSet<GuildConfiguration> GuildConfigurations { get; set; }
}
