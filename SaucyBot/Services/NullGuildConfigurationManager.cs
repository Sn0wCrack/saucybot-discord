using Discord;
using SaucyBot.Database.Models;

namespace SaucyBot.Services;

public sealed class NullGuildConfigurationManager : IGuildConfigurationManager
{
    public Task<GuildConfiguration?> GetByChannel(IMessageChannel messageChannel)
    {
        return Task.FromResult<GuildConfiguration?>(null);
    }

    public Task<GuildConfiguration?> GetByGuildId(ulong guildId)
    {
        return Task.FromResult<GuildConfiguration?>(null);
    }

    public Task<bool> UpdateGuildConfiguration(GuildConfiguration guildConfiguration)
    {
        return Task.FromResult(true);
    }
}
