﻿using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using SaucyBot.Database.Models;
using SaucyBot.Extensions;
using SaucyBot.Extensions.Database;

namespace SaucyBot.Services;

public sealed class GuildConfigurationManager : IGuildConfigurationManager
{
    private readonly DatabaseManager _database;
    private readonly ICacheManager _cache;
    
    public GuildConfigurationManager(DatabaseManager database, ICacheManager cache)
    {
        _database = database;
        _cache = cache;
    }

    public async Task<GuildConfiguration?> GetByChannel(IMessageChannel messageChannel)
    {
        if (messageChannel is not SocketGuildChannel channel)
        {
            return null;
        }

        return await GetByGuildId(channel.Guild.Id);
    }

    public async Task<GuildConfiguration?> GetByGuildId(ulong guildId)
    {
        var result = await _cache.Remember(
            CacheKey(guildId), 
            TimeSpan.FromDays(7),
            async () => await _database
                .Context()
                .FindOrCreateGuildConfigurationByGuildId(guildId)
        );

        return result;
    }

    private static string CacheKey(ulong guildId) => $"database.guild_configuration_{guildId}";
}
