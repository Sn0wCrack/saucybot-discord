using Microsoft.EntityFrameworkCore;
using SaucyBot.Database;
using SaucyBot.Database.Models;

namespace SaucyBot.Extensions.Database;

public static class GuildConfigurationExtensions
{
    public static async Task<GuildConfiguration> FindOrCreateGuildConfigurationByGuildId(
        this DatabaseContext context,
        ulong guildId
    )
    {
        var guildConfiguration = await context.Set<GuildConfiguration>()
            .Include(gc => gc.RestrictedRoles)
            .FirstOrDefaultAsync(gc => gc.GuildId == guildId);

        if (guildConfiguration is not null)
        {
            return guildConfiguration;
        }

        guildConfiguration = new GuildConfiguration { GuildId = guildId };
        await context.AddAsync(guildConfiguration);
        await context.SaveChangesAsync();

        return guildConfiguration;
    }
}
