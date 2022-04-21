using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using SaucyBot.Database.Models;

namespace SaucyBot.Services;

public class GuildConfigurationManager
{
    private readonly DatabaseManager _database;
    private readonly CacheManager _cache;
    
    public GuildConfigurationManager(DatabaseManager database, CacheManager cache)
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
        var result = await _cache.Remember(CacheKey(guildId), TimeSpan.FromDays(7), async () =>
        {
            return await _database
                .Context()
                .GuildConfigurations
                .FirstOrDefaultAsync(gc => gc.GuildId == guildId);
        });

        return result;
    }

    private string CacheKey(ulong guildId) => $"database.guild_configuration_{guildId}";
}
