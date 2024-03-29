﻿using Microsoft.EntityFrameworkCore;
using SaucyBot.Database;
using SaucyBot.Database.Models;

namespace SaucyBot.Extensions.Database;

public static class GuildConfigurationExtensions
{
    public static async Task<GuildConfiguration> FindOrCreateGuildConfigurationByGuildId(
        this DatabaseContext context,
        ulong guildId
    ) {
        var config = await context.Set<GuildConfiguration>().FirstOrDefaultAsync(gc => gc.GuildId == guildId);

        if (config is not null)
        {
            return config;
        }

        config = new GuildConfiguration { GuildId = guildId };
        await context.AddAsync(config);
        await context.SaveChangesAsync();

        return config;
    }
}
